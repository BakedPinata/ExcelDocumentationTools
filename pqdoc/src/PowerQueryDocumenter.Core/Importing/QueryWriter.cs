using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Core.Output;

namespace PowerQueryDocumenter.Core.Importing;

/// <summary>
/// Changes the sections of a DataMashup ([MS-QDEFF]) to hold more queries, or other text for some it has: their M in
/// <c>Formulas/Section1.m</c>, and an item for each in the Metadata. What was found about these is in docs/DECISIONS.md.
/// </summary>
internal static partial class QueryWriter
{
  private const string XmlHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
  private const string Namespaces = "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";
  private const string SectionPrefix = "Section1/";

  /// <summary>The Package Parts section of a new mashup, with no queries.</summary>
  public static byte[] NewPackageParts()
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      AddEntry(zip, "Config/Package.xml", Encoding.UTF8.GetBytes(
        $"{XmlHeader}<Package {Namespaces}><Version>2.159.229.0</Version><MinVersion>2.21.0.0</MinVersion><Culture>en-US</Culture></Package>"));
      AddEntry(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(
        XmlHeader + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"xml\" ContentType=\"text/xml\" /><Default Extension=\"m\" ContentType=\"application/x-ms-m\" /></Types>"));
      AddEntry(zip, WorkbookReader.FormulasPart, Encoding.UTF8.GetBytes("section Section1;"));
    }

    return buffer.ToArray();
  }

  /// <summary>The Permissions section of a new mashup, as Excel starts one.</summary>
  public static byte[] NewPermissions() => WithByteOrderMark(
    $"{XmlHeader}<PermissionList {Namespaces}><CanEvaluateFuturePackages>false</CanEvaluateFuturePackages><FirewallEnabled>true</FirewallEnabled></PermissionList>");

  /// <summary>The Metadata section of a new mashup: its version, its XML, and an empty content zip after its length.</summary>
  public static byte[] NewMetadata()
  {
    byte[] emptyZip = [0x50, 0x4B, 0x05, 0x06, .. new byte[18]];
    return Metadata(
      WithByteOrderMark($"{XmlHeader}<LocalPackageMetadataFile {Namespaces}><Items><Item><ItemLocation><ItemType>AllFormulas</ItemType><ItemPath />"
        + "</ItemLocation><StableEntries /></Item></Items></LocalPackageMetadataFile>"),
      [.. BitConverter.GetBytes(emptyZip.Length), .. emptyZip]);
  }

  /// <summary>The Package Parts section with the change made to its <c>Formulas/Section1.m</c>, and every other entry as it was.</summary>
  public static byte[] EditPackageParts(byte[] packageParts, QueryChange change)
  {
    var entries = new List<(string Name, byte[] Data)>();
    using (var zip = new ZipArchive(new MemoryStream(packageParts), ZipArchiveMode.Read))
    {
      foreach (var entry in zip.Entries)
      {
        var bytes = new MemoryStream();
        using (var stream = entry.Open())
        {
          stream.CopyTo(bytes);
        }

        entries.Add((entry.FullName, bytes.ToArray()));
      }
    }

    var at = entries.FindIndex(e => e.Name.Equals(WorkbookReader.FormulasPart, StringComparison.OrdinalIgnoreCase));
    if (at < 0)
    {
      throw WorkbookReader.Damaged($"its package has no {WorkbookReader.FormulasPart}");
    }

    var data = entries[at].Data;
    var preamble = Encoding.UTF8.GetPreamble();
    var bom = data.AsSpan().StartsWith(preamble);
    var section = EditSection(Encoding.UTF8.GetString(bom ? data[preamble.Length..] : data), change);
    entries[at] = (entries[at].Name, [.. bom ? preamble : [], .. Encoding.UTF8.GetBytes(section)]);

    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach (var (name, content) in entries)
      {
        AddEntry(zip, name, content);
      }
    }

    return buffer.ToArray();
  }

  /// <summary>
  /// A section document with some members replaced and others added at its end. A replaced member keeps the target's
  /// spelling of the name, as the target's other queries use it.
  /// </summary>
  public static string EditSection(string section, QueryChange change)
  {
    var replacing = change.Replace.ToDictionary(q => q.Name, DependencyGraph.NameComparer);
    var text = new StringBuilder();
    var at = 0;
    foreach (var member in SectionSplitter.Members(section))
    {
      text.Append(section, at, member.Start - at);
      if (replacing.TryGetValue(member.Query.Name, out var query))
      {
        text.Append(Member(member.Query.Name, query));
      }
      else
      {
        text.Append(section, member.Start, member.End - member.Start);
      }

      at = member.End;
    }

    text.Append(section, at, section.Length - at);
    var result = new StringBuilder(text.ToString().TrimEnd());
    foreach (var query in change.Add)
    {
      result.Append("\r\n\r\n").Append(Member(query.Name, query));
    }

    return result.ToString();
  }

  /// <summary>Whether two expressions say the same, as Excel stores some with CRLF and some with LF.</summary>
  public static bool SameText(string a, string b) =>
    QueryFileText.WithCrlfOutsideText(a).Trim().Equals(QueryFileText.WithCrlfOutsideText(b).Trim(), StringComparison.Ordinal);

  /// <summary>The Metadata section with an item for each query added, and each replaced query's item brought up to date.</summary>
  public static byte[] EditMetadata(byte[] metadata, QueryChange change)
  {
    if (metadata.Length == 0)
    {
      metadata = NewMetadata();
    }

    var xmlLength = metadata.Length < 8 ? -1L : BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(4));
    if (xmlLength < 0 || xmlLength > metadata.Length - 8)
    {
      throw WorkbookReader.Damaged("its metadata is too short");
    }

    var document = ReadXml(metadata.AsSpan(8, (int)xmlLength).ToArray());
    var items = document.Root?.Element("Items") ?? throw WorkbookReader.Damaged("its metadata has no list of items");
    foreach (var query in change.Replace)
    {
      Update(items, query);
    }

    foreach (var query in change.Add)
    {
      items.Add(NewItem(query));
    }

    var buffer = new MemoryStream();
    using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(true) }))
    {
      document.Save(writer);
    }

    return Metadata(buffer.ToArray(), metadata[(8 + (int)xmlLength)..]);
  }

  // Everything but the query's text belongs to the target, and where it loads to above all. The items for its old steps
  // go, as they name steps it no longer has; a query added from outside has none either, and Excel makes them.
  private static void Update(XElement items, QueryToWrite query)
  {
    foreach (var item in items.Elements("Item").ToList())
    {
      if (PathOf(item) is not { } path || !path.StartsWith(SectionPrefix, StringComparison.Ordinal))
      {
        continue;
      }

      var pieces = path[SectionPrefix.Length..].Split('/');
      if (!DependencyGraph.NameComparer.Equals(Uri.UnescapeDataString(pieces[0]), query.Name))
      {
        continue;
      }

      if (pieces.Length > 1)
      {
        item.Remove();
        continue;
      }

      var resultType = item.Element("StableEntries")?.Elements("Entry").FirstOrDefault(e => (string?)e.Attribute("Type") == "ResultType");
      resultType?.SetAttributeValue("Value", "s" + query.ResultType);
    }
  }

  private static string? PathOf(XElement item)
  {
    var location = item.Element("ItemLocation");
    return (string?)location?.Element("ItemType") == "Formula" ? (string?)location.Element("ItemPath") : null;
  }

  // As Excel writes a connection-only query.
  private static XElement NewItem(QueryToWrite query)
  {
    static XElement Entry(string type, string value) => new("Entry", new XAttribute("Type", type), new XAttribute("Value", value));

    return new XElement(
      "Item",
      new XElement("ItemLocation", new XElement("ItemType", "Formula"), new XElement("ItemPath", SectionPrefix + Uri.EscapeDataString(query.Name))),
      new XElement(
        "StableEntries",
        Entry("IsPrivate", "l0"),
        Entry("QueryID", "s" + Guid.NewGuid()),
        Entry("LoadedToAnalysisServices", "l0"),
        Entry("NavigationStepName", "sNavigation"),
        Entry("NameUpdatedAfterFill", "l0"),
        Entry("ResultType", "s" + query.ResultType),
        Entry("BufferNextRefresh", "l1"),
        Entry("FillEnabled", "l0"),
        Entry("FillObjectType", "sConnectionOnly"),
        Entry("FillToDataModelEnabled", "l0"),
        Entry("FilledCompleteResultToWorksheet", "l0"),
        Entry("AddedToDataModel", "l0")));
  }

  private static string Member(string name, QueryToWrite query)
  {
    var expression = query.Expression.Trim();
    var attributes = query.Description is null ? string.Empty : $"[ Description = \"{query.Description.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ]\r\n";

    // A line comment at the end would swallow the semicolon, so it goes on a line of its own, as Excel writes it.
    var tokens = MTokeniser.Tokenise(expression);
    var endsInComment = tokens.Count > 0 && expression.AsSpan(tokens[^1].Position).Contains("//", StringComparison.Ordinal);
    return $"{attributes}shared {MName(name)} = {expression}{(endsInComment ? "\r\n" : string.Empty)};";
  }

  // A name M can use as it stands, or #"quoted".
  private static string MName(string name) =>
    PlainName().IsMatch(name) && !MLanguage.IsKeyword(name) ? name : $"#\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

  private static XDocument ReadXml(byte[] xml)
  {
    try
    {
      var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
      using var reader = XmlReader.Create(new MemoryStream(xml), settings);
      return XDocument.Load(reader);
    }
    catch (XmlException ex)
    {
      throw WorkbookReader.Damaged($"its metadata isn't well-formed XML ({ex.Message})", ex);
    }
  }

  private static byte[] Metadata(byte[] xml, byte[] rest) => [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(xml.Length), .. xml, .. rest];

  private static byte[] WithByteOrderMark(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

  private static void AddEntry(ZipArchive zip, string name, byte[] data)
  {
    using var stream = zip.CreateEntry(name).Open();
    stream.Write(data);
  }

  [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
  private static partial Regex PlainName();
}
