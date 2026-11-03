using System.IO.Compression;
using System.Text;
using PowerQueryDocumenter.Core.Extraction;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>
/// Builds the smallest file the workbook reader accepts: a zip holding <c>customXml/item1.xml</c>, whose
/// <c>DataMashup</c> element holds base64 of a version and four length-prefixed sections. Each layer can be built
/// separately, to damage it on purpose.
/// </summary>
internal static class MashupWorkbook
{
  /// <summary>A workbook whose queries are <paramref name="section1"/>.</summary>
  public static byte[] With(string section1) => FromMashup(Sections(0, PackageParts(section1)));

  /// <summary>The package zip, holding <c>Formulas/Section1.m</c> unless <paramref name="section1"/> is null.</summary>
  public static byte[] PackageParts(string? section1)
  {
    var files = new Dictionary<string, byte[]> { ["[Content_Types].xml"] = "<Types/>"u8.ToArray() };
    if (section1 is not null)
    {
      files[WorkbookReader.FormulasPart] = Encoding.UTF8.GetBytes(section1);
    }

    return Zip(files);
  }

  /// <summary>
  /// A version, then each section after its 4-byte length. With just one section given, the other three are added
  /// empty.
  /// </summary>
  public static byte[] Sections(uint version, params byte[][] sections)
  {
    if (sections.Length == 1)
    {
      sections = [sections[0], [], [], []];
    }

    using var bytes = new MemoryStream();
    using var writer = new BinaryWriter(bytes);
    writer.Write(version);
    foreach (var section in sections)
    {
      writer.Write((uint)section.Length);
      writer.Write(section);
    }

    writer.Flush();
    return bytes.ToArray();
  }

  /// <summary>A workbook whose DataMashup holds <paramref name="mashup"/>, in UTF-16 with a byte order mark as Excel writes it.</summary>
  public static byte[] FromMashup(byte[] mashup) => FromBase64(Convert.ToBase64String(mashup));

  /// <summary>A workbook whose DataMashup element holds <paramref name="content"/> as its text.</summary>
  public static byte[] FromBase64(string content, Encoding? encoding = null) =>
    FromXml($"<?xml version=\"1.0\" encoding=\"{(encoding ?? Encoding.Unicode).WebName}\"?>" +
      $"<DataMashup xmlns=\"{WorkbookReader.DataMashupNamespace}\">{content}</DataMashup>", encoding);

  /// <summary>A workbook whose one custom XML part is <paramref name="xml"/>, with the encoding's byte order mark.</summary>
  public static byte[] FromXml(string xml, Encoding? encoding = null)
  {
    encoding ??= Encoding.Unicode;
    return Zip(new() { ["customXml/item1.xml"] = [.. encoding.GetPreamble(), .. encoding.GetBytes(xml)] });
  }

  /// <summary>A zip of the given files.</summary>
  public static byte[] Zip(Dictionary<string, byte[]> files)
  {
    using var bytes = new MemoryStream();
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (name, content) in files)
      {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(content);
      }
    }

    return bytes.ToArray();
  }
}
