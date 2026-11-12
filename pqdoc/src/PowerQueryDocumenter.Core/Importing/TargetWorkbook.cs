using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Model;
using Serilog;

namespace PowerQueryDocumenter.Core.Importing;

/// <summary>A query to write into a workbook.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Expression">Its M.</param>
/// <param name="Description">Its description, or null.</param>
/// <param name="ResultType">What it returns, as the metadata says it, such as <c>Table</c> or <c>Function</c>.</param>
public sealed record QueryToWrite(string Name, string Expression, string? Description, string ResultType);

/// <summary>What to write into a workbook.</summary>
/// <param name="Add">The queries to add.</param>
/// <param name="Replace">The queries to put in place of those the target has by those names.</param>
public sealed record QueryChange(IReadOnlyList<QueryToWrite> Add, IReadOnlyList<QueryToWrite> Replace);

/// <summary>
/// The workbook queries are written into: an existing one, read here and changed only in memory, or a new one. Only the
/// parts that have to change are touched, so a sensitivity label, which is metadata, comes through as it was.
/// </summary>
public sealed class TargetWorkbook
{
  private const string ConnectionsRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/connections";
  private const string ConnectionsContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml";
  private const string CustomXmlRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
  private const string CustomXmlPropertiesRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps";
  private const string CustomXmlPropertiesContentType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";

  private static readonly XNamespace DataMashup = WorkbookReader.DataMashupNamespace;

  private readonly OpcPackage package;
  private readonly string? mashupPart;
  private readonly byte[][]? sections;

  private TargetWorkbook(OpcPackage package, string? mashupPart, byte[][]? sections, IReadOnlyList<WorkbookQuery> queries)
  {
    this.package = package;
    this.mashupPart = mashupPart;
    this.sections = sections;
    Queries = queries;
  }

  /// <summary>Its queries. It may have none.</summary>
  public IReadOnlyList<WorkbookQuery> Queries { get; }

  /// <summary>Whether it's set to ignore privacy levels, a setting Excel only trusts while the queries are as they were saved.</summary>
  public bool IgnoresPrivacyLevels =>
    sections is { } read && read[1].Length > 0 && TryReadPermissions(read[1])?.Descendants("FirewallEnabled").FirstOrDefault()?.Value == "false";

  /// <summary>What queries can be written into, with their dots.</summary>
  public static IReadOnlyList<string> Extensions => TargetFile.Workbooks;

  /// <summary>The kinds of new workbook <see cref="New"/> builds, with their dots.</summary>
  public static IReadOnlyList<string> NewExtensions { get; } = [".xlsx", ".xlsm"];

  /// <summary>A new workbook with one sheet.</summary>
  /// <param name="macroEnabled">Whether it's to be an <c>.xlsm</c>.</param>
  public static TargetWorkbook New(bool macroEnabled) => new(BlankWorkbook.Build(macroEnabled), null, null, []);

  /// <summary>Reads a workbook to write into. It's only read: the result is the bytes <see cref="Write"/> returns.</summary>
  /// <param name="path">The workbook.</param>
  /// <param name="logger">Where to warn about metadata that can't be read; null for nowhere.</param>
  /// <exception cref="ExtractionException">It can't be a target, and says why.</exception>
  public static TargetWorkbook Read(string path, ILogger? logger = null)
  {
    var package = OpcPackage.Read(TargetFile.ReadBytes(path, "queries", ".xlsx", Extensions));
    foreach (var name in package.Names.Where(WorkbookReader.IsCustomXmlPart).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
    {
      XDocument document;
      try
      {
        document = package.Xml(name);
      }
      catch (ExtractionException)
      {
        continue; // some other custom XML, which isn't ours to judge
      }

      if (document.Root?.Name == DataMashup + "DataMashup")
      {
        var sections = WorkbookReader.SectionsOf(document.Root.Value);
        return new TargetWorkbook(package, name, sections, WorkbookReader.QueriesOf(sections, logger));
      }
    }

    return new TargetWorkbook(package, null, null, []);
  }

  /// <summary>Makes the change, reads the result back and returns it, without touching any file.</summary>
  /// <exception cref="ExtractionException">What was written didn't read back.</exception>
  public byte[] Write(QueryChange change)
  {
    ArgumentNullException.ThrowIfNull(change);
    var (part, written) = mashupPart is null ? CreateMashup() : (mashupPart, sections!);
    written[0] = QueryWriter.EditPackageParts(written[0], change);
    written[2] = QueryWriter.EditMetadata(written[2], change);
    Save(part, written);

    // An .xlsb keeps its connections in a binary part. Excel listed queries without one, and makes it when it's needed.
    if (package.WorkbookPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
    {
      AddConnections(change.Add);
    }

    var output = package.Write();
    CheckReadsBack(output, change);
    return output;
  }

  private void CheckReadsBack(byte[] output, QueryChange change)
  {
    var read = WorkbookReader.Read(output).ToDictionary(q => q.Name, DependencyGraph.NameComparer);
    var replaced = change.Replace.Select(q => q.Name).ToHashSet(DependencyGraph.NameComparer);
    var expected = Queries.Where(q => !replaced.Contains(q.Name)).Select(q => (q.Name, q.Expression))
      .Concat(change.Add.Concat(change.Replace).Select(q => (q.Name, Expression: q.Expression.Trim())));

    foreach (var (name, expression) in expected)
    {
      if (!read.TryGetValue(name, out var query) || !query.Expression.Equals(expression, StringComparison.Ordinal))
      {
        throw new ExtractionException($"The result didn't read back as written ({name}), so nothing was written.");
      }
    }
  }

  // A workbook without queries gets a DataMashup part: UTF-16 with a byte order mark as Excel writes it, and its properties.
  private (string Part, byte[][] Sections) CreateMashup()
  {
    var n = 1;
    while (package.Has($"customXml/item{n}.xml") || package.Has($"customXml/itemProps{n}.xml"))
    {
      n++;
    }

    var part = $"customXml/item{n}.xml";
    var properties = $"customXml/itemProps{n}.xml";
    package.Set(properties, Encoding.UTF8.GetBytes(
      "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\r\n"
      + $"<ds:datastoreItem ds:itemID=\"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\" xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\">"
      + $"<ds:schemaRefs><ds:schemaRef ds:uri=\"{WorkbookReader.DataMashupNamespace}\"/></ds:schemaRefs></ds:datastoreItem>"));
    package.AddRelationship(part, CustomXmlPropertiesRelationship, $"itemProps{n}.xml");
    package.SetOverride(properties, CustomXmlPropertiesContentType);
    package.AddRelationship(package.WorkbookPart, CustomXmlRelationship, $"../{part}");
    package.Set(part, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(
      $"<?xml version=\"1.0\" encoding=\"utf-16\"?><DataMashup xmlns=\"{WorkbookReader.DataMashupNamespace}\"></DataMashup>")]);

    return (part, [QueryWriter.NewPackageParts(), QueryWriter.NewPermissions(), QueryWriter.NewMetadata(), []]);
  }

  private void Save(string part, byte[][] written)
  {
    var data = new MemoryStream();
    data.Write(BitConverter.GetBytes(0));
    foreach (var section in written)
    {
      data.Write(BitConverter.GetBytes(section.Length));
      data.Write(section);
    }

    var document = package.Xml(part);
    document.Root!.Value = Convert.ToBase64String(data.ToArray());
    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = Encoding.Unicode }))
    {
      document.Save(writer);
    }

    package.Set(part, buffer.ToArray());
  }

  // One per new query, connection only, as Excel has for every query. A query that's replaced keeps the one it has.
  private void AddConnections(IReadOnlyList<QueryToWrite> queries)
  {
    if (queries.Count == 0)
    {
      return;
    }

    var main = OpcPackage.Main;
    var workbook = package.WorkbookPart;
    var target = package.RelationshipsOf(workbook).FirstOrDefault(r => r.Type == ConnectionsRelationship).Target;
    string part;
    XDocument document;
    if (target is null)
    {
      part = OpcPackage.Resolve(workbook, "connections.xml");
      document = new XDocument(new XElement(main + "connections"));
      package.AddRelationship(workbook, ConnectionsRelationship, "connections.xml");
      package.SetOverride(part, ConnectionsContentType);
    }
    else
    {
      part = OpcPackage.Resolve(workbook, target);
      document = package.Xml(part);
    }

    var root = document.Root!;
    var id = root.Elements(main + "connection").Select(c => (int?)c.Attribute("id") ?? 0).DefaultIfEmpty(0).Max();
    var names = root.Elements(main + "connection").Select(c => (string?)c.Attribute("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var query in queries)
    {
      root.Add(new XElement(
        main + "connection",
        new XAttribute("id", ++id),
        new XAttribute("keepAlive", "1"),
        new XAttribute("name", FreeName(names, "Query - " + query.Name)),
        new XAttribute("description", $"Connection to the '{query.Name}' query in the workbook."),
        new XAttribute("type", "5"),
        new XAttribute("refreshedVersion", "0"),
        new XAttribute("background", "1"),
        new XElement(
          main + "dbPr",
          new XAttribute("connection", $"Provider=Microsoft.Mashup.OleDb.1;Data Source=$Workbook$;Location={Location(query.Name)};Extended Properties=\"\""),
          new XAttribute("command", $"SELECT * FROM [{query.Name.Replace("]", "]]", StringComparison.Ordinal)}]"))));
    }

    package.SetXml(part, document);
  }

  // Excel adds a number when a connection's name is taken, as one left behind by a query since deleted can be.
  private static string FreeName(HashSet<string> names, string name)
  {
    var free = name;
    for (var n = 1; !names.Add(free); n++)
    {
      free = $"{name} ({n})";
    }

    return free;
  }

  private static string Location(string name) =>
    name.IndexOfAny([' ', ';', '"', '\'', '=']) < 0 ? name : $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

  private static XDocument? TryReadPermissions(byte[] permissions)
  {
    try
    {
      var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
      using var reader = XmlReader.Create(new MemoryStream(permissions), settings);
      return XDocument.Load(reader);
    }
    catch (XmlException)
    {
      return null; // it only decides what's said
    }
  }
}
