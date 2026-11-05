using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ImportSpike;

/// <summary>A query to write: its name and its M expression, as pqdoc's .m files hold them.</summary>
sealed record QueryToWrite(string Name, string Expression, bool IsFunction);

/// <summary>
/// Writes queries into a workbook's DataMashup part ([MS-QDEFF]): their M into Formulas/Section1.m in the Package Parts
/// zip, an item for each in the Metadata, as connection-only queries, and a connection each in xl/connections.xml, as
/// Excel has for every query. Permissions are kept. Permission Bindings, a per-user DPAPI-protected check of the
/// package and permissions, can't be remade outside Excel: an existing one is kept, which no longer matches, and a new
/// mashup has none. MS-QDEFF says Excel then ignores the permissions; what that means in practice is for Excel to show.
/// </summary>
static partial class Mashup
{
  static readonly XNamespace DataMashup = "http://schemas.microsoft.com/DataMashup";
  const string CustomXmlType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
  const string ConnectionsType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/connections";

  public static QueryToWrite ReadQuery(string path)
  {
    var expression = File.ReadAllText(path).ReplaceLineEndings("\r\n").TrimEnd();
    var isFunction = expression.TrimStart().StartsWith('(') && expression.Contains("=>", StringComparison.Ordinal);
    return new(Path.GetFileNameWithoutExtension(path), expression, isFunction);
  }

  public static void AddQueries(OpcPackage package, IReadOnlyList<QueryToWrite> queries, bool withConnections)
  {
    var (part, sections) = Find(package) ?? Create(package);
    sections[0] = AddToPackageParts(sections[0], queries);
    sections[2] = AddToMetadata(sections[2], queries);
    Save(package, part, sections);
    if (withConnections) AddConnections(package, queries);
  }

  // ---- Finding or making the DataMashup part ----

  static (string Part, byte[][] Sections)? Find(OpcPackage package)
  {
    foreach (var name in package.Names.Where(n => CustomXmlItem().IsMatch(n)).ToList())
    {
      XDocument doc;
      try { doc = package.Xml(name); } catch (XmlException) { continue; }
      if (doc.Root?.Name != DataMashup + "DataMashup") continue;
      var data = Convert.FromBase64String(doc.Root.Value);
      var sections = new byte[4][];
      int at = 4; // after the version, 0
      for (int i = 0; i < 4; i++)
      {
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        sections[i] = data.AsSpan(at + 4, length).ToArray();
        at += 4 + length;
      }
      return (name, sections);
    }
    return null;
  }

  static (string Part, byte[][] Sections) Create(OpcPackage package)
  {
    int n = 1;
    while (package.Has($"customXml/item{n}.xml")) n++;
    var part = $"customXml/item{n}.xml";
    package.Set($"customXml/itemProps{n}.xml", Encoding.UTF8.GetBytes(
      "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\r\n" +
      $"<ds:datastoreItem ds:itemID=\"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\" xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\">" +
      "<ds:schemaRefs><ds:schemaRef ds:uri=\"http://schemas.microsoft.com/DataMashup\"/></ds:schemaRefs></ds:datastoreItem>"));
    package.AddRelationship(part, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps", $"itemProps{n}.xml");
    package.SetOverride($"customXml/itemProps{n}.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
    package.AddRelationship(package.WorkbookPart, CustomXmlType, $"../customXml/item{n}.xml");
    package.Set(part, MashupXml(""));

    var permissions = Bom("<?xml version=\"1.0\" encoding=\"utf-8\"?><PermissionList xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
      "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><CanEvaluateFuturePackages>false</CanEvaluateFuturePackages>" +
      "<FirewallEnabled>true</FirewallEnabled></PermissionList>");
    var metadataXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><LocalPackageMetadataFile xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
      "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Items><Item><ItemLocation><ItemType>AllFormulas</ItemType><ItemPath />" +
      "</ItemLocation><StableEntries /></Item></Items></LocalPackageMetadataFile>";
    return (part, [NewPackageParts(), permissions, Metadata(Bom(metadataXml), EmptyContent()), []]);
  }

  // The XML holding the base64, UTF-16 with a byte order mark, as Excel writes it.
  static byte[] MashupXml(string base64) =>
    [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-16\"?><DataMashup xmlns=\"{DataMashup.NamespaceName}\">{base64}</DataMashup>")];

  static void Save(OpcPackage package, string part, byte[][] sections)
  {
    var data = new List<byte>(BitConverter.GetBytes(0));
    foreach (var s in sections)
    {
      data.AddRange(BitConverter.GetBytes(s.Length));
      data.AddRange(s);
    }
    var doc = package.Xml(part);
    doc.Root!.Value = Convert.ToBase64String(data.ToArray());
    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = Encoding.Unicode }))
      doc.Save(writer);
    package.Set(part, buffer.ToArray());
  }

  // ---- Package Parts: the queries' M ----

  static byte[] NewPackageParts()
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      Entry(zip, "Config/Package.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><Package xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Version>2.159.229.0</Version><MinVersion>2.21.0.0</MinVersion><Culture>en-US</Culture></Package>"));
      Entry(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"xml\" ContentType=\"text/xml\" /><Default Extension=\"m\" ContentType=\"application/x-ms-m\" /></Types>"));
      Entry(zip, "Formulas/Section1.m", Encoding.UTF8.GetBytes("section Section1;"));
    }
    return buffer.ToArray();
  }

  static byte[] AddToPackageParts(byte[] packageParts, IReadOnlyList<QueryToWrite> queries)
  {
    var entries = new List<(string Name, byte[] Data)>();
    using (var zip = new ZipArchive(new MemoryStream(packageParts), ZipArchiveMode.Read))
    {
      foreach (var e in zip.Entries)
      {
        var b = new MemoryStream();
        using (var s = e.Open()) s.CopyTo(b);
        entries.Add((e.FullName, b.ToArray()));
      }
    }
    int at = entries.FindIndex(e => e.Name == "Formulas/Section1.m");
    var bytes = entries[at].Data;
    bool bom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
    var text = new StringBuilder(Encoding.UTF8.GetString(bom ? bytes[3..] : bytes).TrimEnd());
    foreach (var q in queries) text.Append($"\r\n\r\nshared {MName(q.Name)} = {q.Expression};");
    entries[at] = (entries[at].Name, [.. (bom ? Encoding.UTF8.GetPreamble() : []), .. Encoding.UTF8.GetBytes(text.ToString())]);

    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (name, data) in entries) Entry(zip, name, data);
    }
    return buffer.ToArray();
  }

  // A name M can use as it is, or #"quoted".
  static string MName(string name) => PlainName().IsMatch(name) ? name : "#\"" + name.Replace("\"", "\"\"") + "\"";

  // ---- Metadata: one item per query, as Excel writes a connection-only one ----

  static byte[] AddToMetadata(byte[] metadata, IReadOnlyList<QueryToWrite> queries)
  {
    int xmlLength = BinaryPrimitives.ReadInt32LittleEndian(metadata.AsSpan(4));
    var doc = XDocument.Load(new MemoryStream(metadata, 8, xmlLength));
    var items = doc.Root!.Element("Items")!;
    foreach (var q in queries)
    {
      XElement Entry(string type, string value) => new("Entry", new XAttribute("Type", type), new XAttribute("Value", value));
      items.Add(new XElement("Item",
        new XElement("ItemLocation", new XElement("ItemType", "Formula"), new XElement("ItemPath", "Section1/" + Uri.EscapeDataString(q.Name))),
        new XElement("StableEntries",
          Entry("IsPrivate", "l0"), Entry("QueryID", "s" + Guid.NewGuid()), Entry("LoadedToAnalysisServices", "l0"),
          Entry("NavigationStepName", "sNavigation"), Entry("NameUpdatedAfterFill", "l0"),
          Entry("ResultType", q.IsFunction ? "sFunction" : "sTable"), Entry("BufferNextRefresh", "l1"),
          Entry("FillEnabled", "l0"), Entry("FillObjectType", "sConnectionOnly"), Entry("FillToDataModelEnabled", "l0"),
          Entry("FilledCompleteResultToWorksheet", "l0"), Entry("AddedToDataModel", "l0"))));
    }
    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(true) }))
      doc.Save(writer);
    return Metadata(buffer.ToArray(), metadata[(8 + xmlLength)..]);
  }

  // The Metadata section: version 0, the XML after its length, then the content (its length and an empty zip).
  static byte[] Metadata(byte[] xml, byte[] rest) => [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(xml.Length), .. xml, .. rest];

  static byte[] EmptyContent() => [.. BitConverter.GetBytes(22), 0x50, 0x4B, 0x05, 0x06, .. new byte[18]];

  // ---- Connections: one per query, connection only ----

  static void AddConnections(OpcPackage package, IReadOnlyList<QueryToWrite> queries)
  {
    var workbook = package.WorkbookPart;
    var target = package.RelationshipsOf(workbook).FirstOrDefault(r => r.Type == ConnectionsType).Target;
    string part;
    if (target is null)
    {
      part = "xl/connections.xml";
      package.SetXml(part, new XDocument(new XElement(OpcPackage.Main + "connections")));
      package.AddRelationship(workbook, ConnectionsType, "connections.xml");
      package.SetOverride(part, "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml");
    }
    else
    {
      part = Workbooks.Resolve(workbook, target);
      if (!part.EndsWith(".xml", StringComparison.Ordinal)) throw new InvalidOperationException("An .xlsb keeps its connections in a binary part.");
    }

    var doc = package.Xml(part);
    var main = OpcPackage.Main;
    int id = doc.Root!.Elements(main + "connection").Select(c => (int)c.Attribute("id")!).DefaultIfEmpty(0).Max();
    foreach (var q in queries)
    {
      var location = q.Name.Contains(' ') ? $"\"{q.Name}\"" : q.Name;
      doc.Root.Add(new XElement(main + "connection",
        new XAttribute("id", ++id), new XAttribute("keepAlive", "1"), new XAttribute("name", "Query - " + q.Name),
        new XAttribute("description", $"Connection to the '{q.Name}' query in the workbook."), new XAttribute("type", "5"),
        new XAttribute("refreshedVersion", "0"), new XAttribute("background", "1"),
        new XElement(main + "dbPr",
          new XAttribute("connection", $"Provider=Microsoft.Mashup.OleDb.1;Data Source=$Workbook$;Location={location};Extended Properties=\"\""),
          new XAttribute("command", $"SELECT * FROM [{q.Name}]"))));
    }
    package.SetXml(part, doc);
  }

  static byte[] Bom(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

  static void Entry(ZipArchive zip, string name, byte[] data)
  {
    using var s = zip.CreateEntry(name).Open();
    s.Write(data);
  }

  [GeneratedRegex(@"^customXml/item\d+\.xml$")]
  private static partial Regex CustomXmlItem();

  [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
  private static partial Regex PlainName();
}
