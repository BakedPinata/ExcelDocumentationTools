using System.Buffers.Binary;
using System.Text;
using System.Xml;

namespace PowerQueryDocumenter.Core.Extraction;

/// <summary>A query group, as Excel's Queries pane shows it.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Order">Where Excel lists it among the groups, from 0.</param>
public sealed record QueryGroup(string Name, int Order);

/// <summary>What a workbook's Power Query metadata says about one query.</summary>
/// <param name="Group">The group the query is in, or null.</param>
/// <param name="ResultType">What the query returns, such as <c>Table</c> or <c>Function</c>, or null when not recorded.</param>
public sealed record QueryMetadataEntry(QueryGroup? Group, string? ResultType);

/// <summary>
/// Reads the Metadata section of a workbook's <c>DataMashup</c> ([MS-QDEFF]) for each query's group and result type.
/// What's known about the format is in docs/DECISIONS.md. None of it is needed to analyse the queries, so it fails safe:
/// metadata that can't be read means no groups and no result types, never a failed run.
/// </summary>
public static class QueryMetadata
{
  /// <summary>The metadata for each query, by name (case-insensitive, as query names are).</summary>
  public static IReadOnlyDictionary<string, QueryMetadataEntry> Read(byte[] metadataSection)
  {
    ArgumentNullException.ThrowIfNull(metadataSection);
    var result = new Dictionary<string, QueryMetadataEntry>(StringComparer.OrdinalIgnoreCase);
    try
    {
      // A 4-byte version, then the XML after its 4-byte length (then the content zip, which isn't needed).
      if (metadataSection.Length < 8)
      {
        return result;
      }

      var length = BinaryPrimitives.ReadUInt32LittleEndian(metadataSection.AsSpan(4));
      if (length > (uint)(metadataSection.Length - 8))
      {
        return result;
      }

      var items = ReadItems(metadataSection.AsSpan(8, (int)length).ToArray());
      var groups = items.TryGetValue("AllFormulas", out var all) && all.TryGetValue("QueryGroups", out var list)
        ? ReadGroups(TextValue(list))
        : new Dictionary<Guid, QueryGroup>();

      foreach (var (path, entries) in items)
      {
        // A query's item is "Section1/<name>", with the name URL-encoded; its steps' items have a further "/".
        const string Prefix = "Section1/";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal) || path.IndexOf('/', Prefix.Length) >= 0)
        {
          continue;
        }

        QueryGroup? group = null;
        if (entries.TryGetValue("QueryGroupID", out var id) && Guid.TryParse(TextValue(id), out var guid))
        {
          groups.TryGetValue(guid, out group);
        }

        var resultType = entries.TryGetValue("ResultType", out var type) ? TextValue(type) : null;
        result[Uri.UnescapeDataString(path[Prefix.Length..])] = new QueryMetadataEntry(group, resultType);
      }
    }
    catch (Exception ex) when (ex is XmlException or FormatException or ArgumentException or UriFormatException)
    {
      result.Clear();
    }

    return result;
  }

  /// <summary>Each item's entries, by item path (or type, for items such as AllFormulas that have no path).</summary>
  private static Dictionary<string, Dictionary<string, string>> ReadItems(byte[] xml)
  {
    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreComments = true,
      IgnoreProcessingInstructions = true,
      IgnoreWhitespace = true,
    };

    var items = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    using var reader = XmlReader.Create(new MemoryStream(xml), settings);
    string? type = null;
    string? path = null;
    Dictionary<string, string>? entries = null;
    reader.Read();
    while (!reader.EOF)
    {
      // Reading an element's text moves past it, so the reader only moves on here when nothing else did.
      if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ItemType")
      {
        type = reader.ReadElementContentAsString();
        continue;
      }

      if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ItemPath")
      {
        path = reader.ReadElementContentAsString();
        continue;
      }

      if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Item")
      {
        type = null;
        path = null;
        entries = new Dictionary<string, string>(StringComparer.Ordinal);
      }
      else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Entry" && entries is not null
        && reader.GetAttribute("Type") is { } name && reader.GetAttribute("Value") is { } value)
      {
        entries[name] = value;
      }
      else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Item" && entries is not null)
      {
        var key = !string.IsNullOrEmpty(path) ? path : type;
        if (key is not null)
        {
          items[key] = entries;
        }

        entries = null;
      }

      reader.Read();
    }

    return items;
  }

  /// <summary>An entry's value without its type prefix (<c>s</c> for text).</summary>
  private static string TextValue(string value) => value.StartsWith('s') ? value[1..] : value;

  /// <summary>
  /// The groups in a <c>QueryGroups</c> entry: base64 of a count, then for each group an int32, its GUID, its name and
  /// description (each a length-prefixed UTF-8 string), a byte and its order as an int32. Anything else, including
  /// bytes left over, means the layout isn't the one known, so there are no groups.
  /// </summary>
  private static Dictionary<Guid, QueryGroup> ReadGroups(string base64)
  {
    var groups = new Dictionary<Guid, QueryGroup>();
    try
    {
      using var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(base64)), Encoding.UTF8);
      var count = reader.ReadInt32();
      if (count is < 0 or > 10_000)
      {
        return [];
      }

      for (var i = 0; i < count; i++)
      {
        reader.ReadInt32();
        var id = new Guid(reader.ReadBytes(16));
        var name = reader.ReadString();
        reader.ReadString(); // description
        reader.ReadByte();
        var order = reader.ReadInt32();
        groups[id] = new QueryGroup(name, order);
      }

      return reader.BaseStream.Position == reader.BaseStream.Length ? groups : [];
    }
    catch (Exception ex) when (ex is EndOfStreamException or FormatException or ArgumentException or IOException)
    {
      return [];
    }
  }
}
