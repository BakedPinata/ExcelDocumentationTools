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

  private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

  private static ReadOnlySpan<byte> CompoundFileSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
}
