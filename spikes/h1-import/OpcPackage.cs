using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ImportSpike;

/// <summary>A workbook's zip package held in memory, part by part, with what's needed to add and change parts.</summary>
sealed class OpcPackage
{
  public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
  public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
  public static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  const string OfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

  readonly List<(string Name, byte[] Data)> parts = [];

  public static OpcPackage Read(byte[] zipBytes)
  {
    var package = new OpcPackage();
    using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
    foreach (var entry in zip.Entries)
    {
      var buffer = new MemoryStream();
      using (var s = entry.Open()) s.CopyTo(buffer);
      package.parts.Add((entry.FullName, buffer.ToArray()));
    }
    return package;
  }

  public IEnumerable<string> Names => parts.Select(p => p.Name);

  public bool Has(string name) => parts.Any(p => p.Name == name);

  public byte[] Get(string name) => parts.First(p => p.Name == name).Data;

  public void Set(string name, byte[] data)
  {
    int at = parts.FindIndex(p => p.Name == name);
    if (at >= 0) parts[at] = (name, data); else parts.Add((name, data));
  }

  public XDocument Xml(string name) => XDocument.Load(new MemoryStream(Get(name)));

  /// <summary>Saves XML as Excel does: UTF-8 without a byte order mark, standalone="yes".</summary>
  public void SetXml(string name, XDocument doc)
  {
    doc.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
      doc.Save(writer);
    Set(name, buffer.ToArray());
  }

  public byte[] Write()
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (name, data) in parts.OrderBy(p => p.Name == "[Content_Types].xml" ? 0 : 1))
      {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(data);
      }
    }
    return buffer.ToArray();
  }

  /// <summary>The workbook part, from the package's own relationships, such as xl/workbook.xml or xl/workbook.bin.</summary>
  public string WorkbookPart => Xml("_rels/.rels").Root!.Elements(Relationships + "Relationship")
    .First(r => (string?)r.Attribute("Type") == OfficeDocument).Attribute("Target")!.Value.TrimStart('/');

  /// <summary>The relationships part of <paramref name="part"/>; "" is the package itself.</summary>
  public static string RelsOf(string part)
  {
    if (part == "") return "_rels/.rels";
    var dir = Path.GetDirectoryName(part)!.Replace('\\', '/');
    return (dir == "" ? "" : dir + "/") + "_rels/" + Path.GetFileName(part) + ".rels";
  }

  /// <summary>Adds a relationship from <paramref name="part"/> and returns its id.</summary>
  public string AddRelationship(string part, string type, string target)
  {
    var rels = RelsOf(part);
    var doc = Has(rels) ? Xml(rels) : new XDocument(new XElement(Relationships + "Relationships"));
    var ids = doc.Root!.Elements().Select(e => (string)e.Attribute("Id")!).ToHashSet();
    int n = 1;
    while (ids.Contains("rId" + n)) n++;
    doc.Root.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", "rId" + n), new XAttribute("Type", type), new XAttribute("Target", target)));
    SetXml(rels, doc);
    return "rId" + n;
  }

  public IEnumerable<(string Type, string Target)> RelationshipsOf(string part) =>
    Has(RelsOf(part))
      ? Xml(RelsOf(part)).Root!.Elements().Select(e => ((string)e.Attribute("Type")!, (string)e.Attribute("Target")!))
      : [];

  public string? ContentTypeOf(string partName) =>
    (string?)Xml("[Content_Types].xml").Root!.Elements(ContentTypes + "Override")
      .FirstOrDefault(o => (string?)o.Attribute("PartName") == "/" + partName)?.Attribute("ContentType");

  public void SetOverride(string partName, string contentType)
  {
    var doc = Xml("[Content_Types].xml");
    var existing = doc.Root!.Elements(ContentTypes + "Override").FirstOrDefault(o => (string?)o.Attribute("PartName") == "/" + partName);
    if (existing is not null) existing.SetAttributeValue("ContentType", contentType);
    else doc.Root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/" + partName), new XAttribute("ContentType", contentType)));
    SetXml("[Content_Types].xml", doc);
  }
}
