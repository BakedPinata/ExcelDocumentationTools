using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using OpenMcdf;

namespace ExcelDocumenter.Shared.Workbooks;

/// <summary>What kind of file a workbook is, from its first bytes.</summary>
public enum WorkbookContainer
{
  /// <summary>Neither of the others, so not an Excel workbook.</summary>
  Unknown,

  /// <summary>A zip-based workbook: .xlsx, .xlsm, .xlsb or .xlam.</summary>
  Zip,

  /// <summary>An OLE compound file: an older .xls or .xla, or an encrypted workbook of any kind.</summary>
  CompoundFile,
}

/// <summary>
/// Opening a workbook without Excel: reading its bytes while Excel may have it open, telling a zip-based file from a
/// compound file, and spotting encryption. What each tool then reads from it is the tool's own business.
/// </summary>
public static class WorkbookFile
{
  /// <summary>
  /// The stream that holds the whole workbook when it's encrypted. Password-protected, IRM-protected and
  /// sensitivity-labelled files all wrap the workbook in a compound file this way, so nothing inside can be read.
  /// </summary>
  public const string EncryptedPackageStream = "EncryptedPackage";

  /// <summary>Reads the whole file, letting Excel keep it open.</summary>
  /// <exception cref="ExtractionException">The file can't be read.</exception>
  public static byte[] ReadAllBytes(string workbookPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
    try
    {
      // FileShare.ReadWrite lets the file be read while Excel has it open.
      using var file = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var memory = new MemoryStream();
      file.CopyTo(memory);
      return memory.ToArray();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw new ExtractionException($"Can't read {workbookPath}: {ex.Message}", ex);
    }
  }

  /// <summary>What kind of file the bytes are, from their signature.</summary>
  public static WorkbookContainer ContainerOf(ReadOnlySpan<byte> bytes) =>
    bytes.StartsWith(ZipSignature) ? WorkbookContainer.Zip
    : bytes.StartsWith(CompoundFileSignature) ? WorkbookContainer.CompoundFile
    : WorkbookContainer.Unknown;

  /// <summary>Whether a compound file is an encrypted workbook rather than an older .xls.</summary>
  public static bool IsEncrypted(RootStorage root)
  {
    ArgumentNullException.ThrowIfNull(root);
    return root.ContainsEntry(EncryptedPackageStream);
  }

  /// <summary>
  /// When the workbook was last saved, from its own document properties: <c>dcterms:modified</c> in a zip-based
  /// workbook's core properties (<c>docProps/core.xml</c>), or the last-saved time in an older .xls file's summary
  /// information. Null when it has none, or they can't be read: nothing depends on it, so it fails safe.
  /// </summary>
  public static DateTimeOffset? LastSaved(byte[] fileBytes)
  {
    ArgumentNullException.ThrowIfNull(fileBytes);
    try
    {
      return ContainerOf(fileBytes) switch
      {
        WorkbookContainer.Zip => LastSavedInZip(fileBytes),
        WorkbookContainer.CompoundFile => LastSavedInCompoundFile(fileBytes),
        _ => null,
      };
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or FormatException
      or ArgumentException or OpenMcdf.FileFormatException)
    {
      return null;
    }
  }

  private static DateTimeOffset? LastSavedInZip(byte[] fileBytes)
  {
    using var zip = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read);
    var entry = zip.GetEntry("docProps/core.xml");
    if (entry is null)
    {
      return null;
    }

    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
    using var reader = XmlReader.Create(entry.Open(), settings);
    while (reader.Read())
    {
      if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "modified" && reader.NamespaceURI == "http://purl.org/dc/terms/")
      {
        var text = reader.ReadElementContentAsString().Trim();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var saved)
          ? saved
          : null;
      }
    }

    return null;
  }

  // "\u0005SummaryInformation", written this way so the control character can't be lost.
  private static readonly string SummaryInformationStream = (char)5 + "SummaryInformation";

  /// <summary>
  /// The last-saved time (PIDSI_LASTSAVE_DTM, a FILETIME) from the summary information property set ([MS-OLEPS]): a
  /// 28-byte header, then the first set's format id and offset; at that offset the set's size, its number of
  /// properties, and each property's id and offset from the start of the set.
  /// </summary>
  private static DateTimeOffset? LastSavedInCompoundFile(byte[] fileBytes)
  {
    using var root = RootStorage.Open(new MemoryStream(fileBytes));
    if (IsEncrypted(root) || !root.ContainsEntry(SummaryInformationStream))
    {
      return null;
    }

    byte[] bytes;
    using (var stream = root.OpenStream(SummaryInformationStream))
    {
      bytes = new byte[stream.Length];
      stream.ReadExactly(bytes);
    }

    var span = bytes.AsSpan();
    const uint LastSavedId = 0x0D;
    const ushort FileTimeType = 0x40;
    if (span.Length < 48 || BinaryPrimitives.ReadUInt16LittleEndian(span) != 0xFFFE)
    {
      return null;
    }

    var set = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[44..]));
    var count = BinaryPrimitives.ReadUInt32LittleEndian(span[(set + 4)..]);
    for (var i = 0; i < count && i < 1_000; i++)
    {
      var entry = set + 8 + (i * 8);
      if (BinaryPrimitives.ReadUInt32LittleEndian(span[entry..]) != LastSavedId)
      {
        continue;
      }

      var at = checked(set + (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(entry + 4)..]));
      if (BinaryPrimitives.ReadUInt16LittleEndian(span[at..]) != FileTimeType)
      {
        return null;
      }

      var fileTime = BinaryPrimitives.ReadInt64LittleEndian(span[(at + 4)..]);
      return fileTime > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime)) : null;
    }

    return null;
  }

  private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

  private static ReadOnlySpan<byte> CompoundFileSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
}
