using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ExcelDocumenter.Shared.Workbooks;

namespace PowerQueryDocumenter.Core.Extraction;

/// <summary>One query read from a workbook.</summary>
/// <param name="Name">The query's name, as Excel shows it.</param>
/// <param name="Expression">Its M expression as Excel stores it, without <c>shared Name =</c> or the final <c>;</c>.</param>
/// <param name="Description">Its description, or null if it has none.</param>
/// <param name="Group">The query group it's in, from the workbook's metadata, or null.</param>
/// <param name="ResultType">What it returns, such as <c>Table</c> or <c>Function</c>, from the metadata, or null.</param>
public sealed record WorkbookQuery(string Name, string Expression, string? Description, QueryGroup? Group = null, string? ResultType = null);

/// <summary>
/// Reads the Power Query queries out of an Excel workbook without Excel, following [MS-QDEFF]. What was confirmed
/// about the format against real workbooks is in docs/DECISIONS.md. Opening the file and telling which kind it is are
/// shared with vbadoc (<see cref="WorkbookFile"/>).
/// </summary>
/// <remarks>
/// The queries are in a custom XML part whose root is a <c>DataMashup</c> element holding base64. Decoded, that's a
/// 4-byte version and four sections, each after a 4-byte length: Package Parts (a zip holding
/// <c>Formulas/Section1.m</c>), Permissions, Metadata and Permission Bindings. Package Parts holds the queries, and
/// Metadata their groups and result types (<see cref="QueryMetadata"/>). The others are only checked for length, so a
/// damaged file is reported rather than read past its end.
/// </remarks>
public static partial class WorkbookReader
{
  /// <summary>The namespace of the <c>DataMashup</c> element.</summary>
  public const string DataMashupNamespace = "http://schemas.microsoft.com/DataMashup";

  /// <summary>The part of the package that holds every query.</summary>
  public const string FormulasPart = "Formulas/Section1.m";

  private static readonly string[] SectionNames = ["Package Parts", "Permissions", "Metadata", "Permission Bindings"];

  /// <summary>Reads the queries of the workbook at <paramref name="workbookPath"/>.</summary>
  /// <exception cref="ExtractionException">The file can't be read, or holds no readable queries.</exception>
  public static IReadOnlyList<WorkbookQuery> Read(string workbookPath) => Read(WorkbookFile.ReadAllBytes(workbookPath));

  /// <summary>Reads the queries of a workbook held in memory.</summary>
  /// <exception cref="ExtractionException">The bytes aren't a workbook, or hold no readable queries.</exception>
  public static IReadOnlyList<WorkbookQuery> Read(byte[] fileBytes)
  {
    ArgumentNullException.ThrowIfNull(fileBytes);
    var container = WorkbookFile.ContainerOf(fileBytes);
    if (container == WorkbookContainer.Zip)
    {
      return ReadZipBased(fileBytes);
    }

    // An encrypted workbook (password, IRM or sensitivity label) wraps the whole package in a compound file, which is
    // also what an older .xls is. Neither has queries that can be read, so there's no need to look inside.
    if (container == WorkbookContainer.CompoundFile)
    {
      throw new ExtractionException(
        "The workbook is encrypted (password, IRM or sensitivity label) or is an older .xls workbook, and neither has " +
        "queries that can be read. If it's encrypted, save a copy without encryption and run pqdoc on that.");
    }

    throw new ExtractionException("This is not an Excel workbook: it isn't a zip-based file such as .xlsx, .xlsm or .xlsb.");
  }

  private static IReadOnlyList<WorkbookQuery> ReadZipBased(byte[] fileBytes)
  {
    string base64;
    try
    {
      using var zip = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read);
      base64 = FindDataMashup(zip)
        ?? throw new ExtractionException("The workbook has no Power Query queries (it has no DataMashup part).");
    }
    catch (InvalidDataException ex)
    {
      throw new ExtractionException($"The workbook is damaged: it isn't a valid zip file ({ex.Message}).", ex);
    }

    byte[] data;
    try
    {
      data = Convert.FromBase64String(base64);
    }
    catch (FormatException ex)
    {
      throw Damaged("its DataMashup part isn't valid base64", ex);
    }

    var sections = Sections(data);
    var queries = SectionSplitter.Split(ReadSection(sections[0]));
    if (queries.Count == 0)
    {
      throw new ExtractionException("The workbook has no Power Query queries (its DataMashup part holds none).");
    }

    // Groups and result types come from the Metadata section, which fails safe: without it they're simply missing.
    var metadata = QueryMetadata.Read(sections[2]);
    return queries
      .Select(q => metadata.TryGetValue(q.Name, out var entry) ? q with { Group = entry.Group, ResultType = entry.ResultType } : q)
      .ToArray();
  }

  /// <summary>
  /// Finds the DataMashup by what it is, not where it is: the first custom XML part whose root is a
  /// <c>DataMashup</c> element in its namespace. The reader takes each part's encoding from its byte order mark.
  /// </summary>
  private static string? FindDataMashup(ZipArchive zip)
  {
    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreComments = true,
      IgnoreProcessingInstructions = true,
      IgnoreWhitespace = true,
      CloseInput = true,
    };

    var parts = zip.Entries
      .Where(e => CustomXmlPart().IsMatch(e.FullName))
      .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
    foreach (var entry in parts)
    {
      using var reader = XmlReader.Create(entry.Open(), settings);
      try
      {
        reader.MoveToContent();
      }
      catch (XmlException)
      {
        continue; // some other custom XML, which isn't ours to judge
      }

      if (reader.LocalName != "DataMashup" || reader.NamespaceURI != DataMashupNamespace)
      {
        continue;
      }

      try
      {
        return reader.ReadElementContentAsString();
      }
      catch (XmlException ex)
      {
        throw Damaged($"its DataMashup part isn't well-formed XML ({ex.Message})", ex);
      }
    }

    return null;
  }

  /// <summary>
  /// Checks the version and every section's length against the bytes left, and returns the sections in order: Package
  /// Parts, Permissions, Metadata and Permission Bindings.
  /// </summary>
  private static byte[][] Sections(byte[] data)
  {
    if (data.Length < 4)
    {
      throw Damaged("its DataMashup part is too short to hold a version number");
    }

    var version = BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (version != 0)
    {
      throw new ExtractionException($"The workbook's Power Query data is in a version pqdoc doesn't know ({version}).");
    }

    var offset = 4;
    var sections = new List<byte[]>(SectionNames.Length);
    foreach (var name in SectionNames)
    {
      if (data.Length - offset < 4)
      {
        throw Damaged($"it ends before the length of its {name} section");
      }

      var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
      offset += 4;
      if (length > (uint)(data.Length - offset))
      {
        throw Damaged($"its {name} section says it's {length} bytes long, but only {data.Length - offset} are left");
      }

      sections.Add(data.AsSpan(offset, (int)length).ToArray());
      offset += (int)length;
    }

    return [.. sections];
  }

  private static string ReadSection(byte[] packageParts)
  {
    try
    {
      using var zip = new ZipArchive(new MemoryStream(packageParts), ZipArchiveMode.Read);
      var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, FormulasPart, StringComparison.OrdinalIgnoreCase))
        ?? throw Damaged($"its package has no {FormulasPart}");
      using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
      return reader.ReadToEnd();
    }
    catch (InvalidDataException ex)
    {
      throw Damaged($"its package isn't a valid zip file ({ex.Message})", ex);
    }
    catch (DecoderFallbackException ex)
    {
      throw Damaged($"{FormulasPart} isn't valid UTF-8", ex);
    }
  }

  private static ExtractionException Damaged(string detail, Exception? inner = null) =>
    new($"The workbook's Power Query data is damaged: {detail}.", inner);

  [GeneratedRegex(@"^customXml/item\d+\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex CustomXmlPart();
}
