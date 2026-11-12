using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ExcelDocumenter.Shared.Workbooks;

/// <summary>
/// A workbook's zip package held in memory, part by part, so that parts can be added and changed and every other part
/// written back exactly as it was. That's what keeps a sensitivity label, which is only metadata: nothing here touches a
/// part it wasn't asked to.
/// </summary>
public sealed class OpcPackage
{
  /// <summary>The namespace of <c>[Content_Types].xml</c>.</summary>
  public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

  /// <summary>The namespace of a relationships part.</summary>
  public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

  /// <summary>The namespace of a workbook's and a sheet's XML.</summary>
  public static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

  /// <summary>The namespace of an <c>r:id</c>.</summary>
  public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

  /// <summary>The name of the part listing every part's type.</summary>
  public const string ContentTypesPart = "[Content_Types].xml";

  private const string OfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

  private readonly List<(string Name, byte[] Data)> parts = [];

  /// <summary>Reads a package.</summary>
  /// <exception cref="ExtractionException">It isn't a zip that can be read.</exception>
  public static OpcPackage Read(byte[] zipBytes)
  {
    ArgumentNullException.ThrowIfNull(zipBytes);
    var package = new OpcPackage();
    try
    {
      using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
      foreach (var entry in zip.Entries)
      {
        var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
          stream.CopyTo(buffer);
        }

        package.parts.Add((entry.FullName, buffer.ToArray()));
      }
    }
    catch (InvalidDataException ex)
    {
      throw new ExtractionException($"The workbook is damaged: {ex.Message}", ex);
    }

    return package;
  }

  /// <summary>Every part's name.</summary>
  public IEnumerable<string> Names => parts.Select(p => p.Name);

  /// <summary>Whether there's a part of this name.</summary>
  public bool Has(string name) => parts.Exists(p => p.Name == name);

  /// <summary>A part's bytes.</summary>
  public byte[] Get(string name) => parts.First(p => p.Name == name).Data;

  /// <summary>Changes a part, or adds it.</summary>
  public void Set(string name, byte[] data)
  {
    var at = parts.FindIndex(p => p.Name == name);
    if (at >= 0)
    {
      parts[at] = (name, data);
    }
    else
    {
      parts.Add((name, data));
    }
  }

  /// <summary>Takes a part out, if it's there.</summary>
  public void Remove(string name) => parts.RemoveAll(p => p.Name == name);

  /// <summary>A part's XML, keeping its whitespace, since a cell's text can be nothing else.</summary>
  /// <exception cref="ExtractionException">It isn't XML that can be read.</exception>
  public XDocument Xml(string name)
  {
    try
    {
      var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
      using var reader = XmlReader.Create(new MemoryStream(Get(name)), settings);
      return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    catch (XmlException ex)
    {
      throw new ExtractionException($"The workbook is damaged: {name} can't be read ({ex.Message}).", ex);
    }
  }

  /// <summary>Saves XML as Excel does: UTF-8 without a byte order mark, <c>standalone="yes"</c>.</summary>
  public void SetXml(string name, XDocument document)
  {
    ArgumentNullException.ThrowIfNull(document);
    document.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
    {
      document.Save(writer);
    }

    Set(name, buffer.ToArray());
  }

  /// <summary>The whole package as a zip, with the part listing the types first, as Excel writes it.</summary>
  public byte[] Write()
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (name, data) in parts.OrderBy(p => p.Name == ContentTypesPart ? 0 : 1))
      {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(data);
      }
    }

    return buffer.ToArray();
  }

  /// <summary>The workbook part, from the package's own relationships, such as xl/workbook.xml or xl/workbook.bin.</summary>
  /// <exception cref="ExtractionException">The package doesn't say.</exception>
  public string WorkbookPart
  {
    get
    {
      var target = RelationshipsOf(string.Empty).FirstOrDefault(r => r.Type == OfficeDocument).Target;
      return target is not null && Has(Resolve(string.Empty, target))
        ? Resolve(string.Empty, target)
        : throw new ExtractionException("The workbook is damaged: its package doesn't say where the workbook is.");
    }
  }

  /// <summary>The relationships part of a part; "" is the package itself.</summary>
  public static string RelsOf(string part)
  {
    ArgumentNullException.ThrowIfNull(part);
    var slash = part.LastIndexOf('/');
    return part[..(slash + 1)] + "_rels/" + part[(slash + 1)..] + ".rels";
  }

  /// <summary>A relationship's target as a part's name, from the part it's relative to.</summary>
  public static string Resolve(string fromPart, string target)
  {
    ArgumentNullException.ThrowIfNull(fromPart);
    ArgumentNullException.ThrowIfNull(target);
    if (target.StartsWith('/'))
    {
      return target.TrimStart('/');
    }

    var pieces = fromPart.Split('/').SkipLast(1).ToList();
    foreach (var piece in target.Split('/'))
    {
      if (piece != "..")
      {
        pieces.Add(piece);
      }
      else if (pieces.Count > 0)
      {
        pieces.RemoveAt(pieces.Count - 1);
      }
    }

    return string.Join('/', pieces);
  }

  /// <summary>Adds a relationship from a part and returns its id.</summary>
  public string AddRelationship(string part, string type, string target)
  {
    var rels = RelsOf(part);
    var document = Has(rels) ? Xml(rels) : new XDocument(new XElement(Relationships + "Relationships"));
    var ids = document.Root!.Elements().Select(e => (string?)e.Attribute("Id")).ToHashSet();
    var n = 1;
    while (ids.Contains("rId" + n))
    {
      n++;
    }

    document.Root.Add(new XElement(
      Relationships + "Relationship",
      new XAttribute("Id", "rId" + n),
      new XAttribute("Type", type),
      new XAttribute("Target", target)));
    SetXml(rels, document);
    return "rId" + n;
  }

  /// <summary>Takes out the relationships a part has of the types chosen, and the relationships part if that empties it.</summary>
  public void RemoveRelationships(string part, Func<string, bool> ofType)
  {
    ArgumentNullException.ThrowIfNull(ofType);
    var rels = RelsOf(part);
    if (!Has(rels))
    {
      return;
    }

    var document = Xml(rels);
    document.Root!.Elements().Where(e => ofType((string?)e.Attribute("Type") ?? string.Empty)).Remove();
    if (document.Root.Elements().Any())
    {
      SetXml(rels, document);
    }
    else
    {
      Remove(rels);
    }
  }

  /// <summary>A part's relationships.</summary>
  public IEnumerable<(string Id, string Type, string Target)> RelationshipsOf(string part) =>
    Has(RelsOf(part))
      ? Xml(RelsOf(part)).Root!.Elements().Select(e => ((string?)e.Attribute("Id") ?? string.Empty, (string?)e.Attribute("Type") ?? string.Empty, (string?)e.Attribute("Target") ?? string.Empty))
      : [];

  /// <summary>Gives a part a type of its own.</summary>
  public void SetOverride(string partName, string contentType)
  {
    var document = Xml(ContentTypesPart);
    var existing = OverrideOf(document, partName);
    if (existing is not null)
    {
      existing.SetAttributeValue("ContentType", contentType);
    }
    else
    {
      document.Root!.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/" + partName), new XAttribute("ContentType", contentType)));
    }

    SetXml(ContentTypesPart, document);
  }

  /// <summary>A part's own type, or null if it has none and so takes its extension's.</summary>
  public string? ContentTypeOf(string partName) => (string?)OverrideOf(Xml(ContentTypesPart), partName)?.Attribute("ContentType");

  /// <summary>Whether the package is an add-in, which only its workbook part's type says.</summary>
  public bool IsAddin => ContentTypeOf(WorkbookPart) == BlankWorkbook.AddinContentType;

  /// <summary>Takes out a part's own type, if it has one.</summary>
  public void RemoveOverride(string partName)
  {
    var document = Xml(ContentTypesPart);
    if (OverrideOf(document, partName) is { } existing)
    {
      existing.Remove();
      SetXml(ContentTypesPart, document);
    }
  }

  private static XElement? OverrideOf(XDocument document, string partName) =>
    document.Root!.Elements(ContentTypes + "Override").FirstOrDefault(o => (string?)o.Attribute("PartName") == "/" + partName);
}
