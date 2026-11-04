using System.Buffers.Binary;
using System.Text;
using System.Xml;
using Serilog;

namespace PowerQueryDocumenter.Core.Extraction;

/// <summary>A query group, as Excel's Queries pane shows it. Groups can hold other groups, to any depth.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Order">Where Excel lists it among the groups beside it, from 0.</param>
/// <param name="Description">Its description, or null when it has none.</param>
/// <param name="Parent">The group it's in, or null for a group at the top level.</param>
public sealed record QueryGroup(string Name, int Order, string? Description = null, QueryGroup? Parent = null)
{
  /// <summary>The groups from the top level down to this one.</summary>
  public IReadOnlyList<QueryGroup> Path()
  {
    var path = new List<QueryGroup>();
    for (var at = this; at is not null; at = at.Parent)
    {
      path.Add(at);
    }

    path.Reverse();
    return path;
  }
}

/// <summary>What a workbook's Power Query metadata says about one query.</summary>
/// <param name="Group">The group the query is in, or null.</param>
/// <param name="ResultType">What the query returns, such as <c>Table</c> or <c>Function</c>, or null when not recorded.</param>
public sealed record QueryMetadataEntry(QueryGroup? Group, string? ResultType);

/// <summary>
/// Reads the Metadata section of a workbook's <c>DataMashup</c> ([MS-QDEFF]) for each query's group and result type.
/// What's known about the format is in docs/DECISIONS.md. None of it is needed to analyse the queries, so it fails safe:
/// metadata that can't be read means no groups and no result types, never a failed run, with a warning to say so.
/// </summary>
public static class QueryMetadata
{
  /// <summary>The metadata for each query, by name (case-insensitive, as query names are).</summary>
  /// <param name="metadataSection">The Metadata section's bytes.</param>
  /// <param name="logger">Where to warn that the groups couldn't be read; null for nowhere.</param>
  public static IReadOnlyDictionary<string, QueryMetadataEntry> Read(byte[] metadataSection, ILogger? logger = null)
  {
    ArgumentNullException.ThrowIfNull(metadataSection);
    var result = new Dictionary<string, QueryMetadataEntry>(StringComparer.OrdinalIgnoreCase);
    try
    {
      // An empty section says nothing, so there's nothing to warn about. Otherwise it's a 4-byte version, then the XML
      // after its 4-byte length (then the content zip, which isn't needed).
      if (metadataSection.Length == 0)
      {
        return result;
      }

      var length = metadataSection.Length < 8 ? -1L : BinaryPrimitives.ReadUInt32LittleEndian(metadataSection.AsSpan(4));
      if (length < 0 || length > metadataSection.Length - 8)
      {
        Unreadable(logger, "its metadata is too short");
        return result;
      }

      var items = ReadItems(metadataSection.AsSpan(8, (int)length).ToArray());
      var groups = new Dictionary<Guid, QueryGroup>();
      if (items.TryGetValue("AllFormulas", out var all) && all.TryGetValue("QueryGroups", out var list))
      {
        var read = ReadGroups(TextValue(list));
        if (read is null)
        {
          Unreadable(logger, "its list of groups is in a layout pqdoc doesn't know");
        }

        groups = read ?? groups;
      }

      foreach (var (path, entries) in items)
      {
        // A query's item is "Section1/<name>", with the name URL-encoded; its steps' items have a further "/".
        const string Prefix = "Section1/";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal) || path.IndexOf('/', Prefix.Length) >= 0)
        {
          continue;
        }

        var name = Uri.UnescapeDataString(path[Prefix.Length..]);
        QueryGroup? group = null;
        if (entries.TryGetValue("QueryGroupID", out var id) && Guid.TryParse(TextValue(id), out var guid)
          && !groups.TryGetValue(guid, out group) && groups.Count > 0)
        {
          logger?.Debug("The query {Query} is in a group the workbook's list of groups doesn't have", name);
        }

        var resultType = entries.TryGetValue("ResultType", out var type) ? TextValue(type) : null;
        result[name] = new QueryMetadataEntry(group, resultType);
      }
    }
    catch (Exception ex) when (ex is XmlException or FormatException or ArgumentException or UriFormatException)
    {
      result.Clear();
      Unreadable(logger, "its metadata isn't in a form pqdoc knows");
      logger?.Debug(ex, "Metadata failure detail");
    }

    return result;
  }

  private static void Unreadable(ILogger? logger, string reason) =>
    logger?.Warning("Could not read the workbook's query groups ({Reason:l}), so the page lists its queries without them", reason);

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

  /// <summary>One group as the list stores it, before its parent is found.</summary>
  private sealed record StoredGroup(string Name, string? Description, Guid? Parent, int Order);

  /// <summary>
  /// The groups in a <c>QueryGroups</c> entry, by GUID: base64 of a count, then for each group an int32 (0 in every
  /// workbook seen), its GUID, its name and description (each a length-prefixed UTF-8 string), a byte saying whether it
  /// has a parent group, the parent's GUID when it does, and its order among the groups beside it as an int32. A group
  /// can come before its parent. Anything else, including bytes left over, means the layout isn't the one known, so
  /// the result is null.
  /// </summary>
  private static Dictionary<Guid, QueryGroup>? ReadGroups(string base64)
  {
    var stored = new Dictionary<Guid, StoredGroup>();
    try
    {
      using var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(base64)), Encoding.UTF8);
      var count = reader.ReadInt32();
      if (count is < 0 or > 10_000)
      {
        return null;
      }

      for (var i = 0; i < count; i++)
      {
        reader.ReadInt32();
        var id = new Guid(reader.ReadBytes(16));
        var name = reader.ReadString();
        var description = reader.ReadString();
        Guid? parent = reader.ReadByte() switch
        {
          0 => null,
          1 => new Guid(reader.ReadBytes(16)),
          _ => throw new FormatException("A group's parent flag is neither 0 nor 1."),
        };
        var order = reader.ReadInt32();
        stored[id] = new StoredGroup(name, description.Length > 0 ? description : null, parent, order);
      }

      if (reader.BaseStream.Position != reader.BaseStream.Length)
      {
        return null;
      }
    }
    catch (Exception ex) when (ex is EndOfStreamException or FormatException or ArgumentException or IOException)
    {
      return null;
    }

    // Each group is built after its parent. A parent that isn't in the list, or one that would make a loop, leaves the
    // group at the top level rather than losing it.
    var groups = new Dictionary<Guid, QueryGroup>();
    QueryGroup Build(Guid id, HashSet<Guid> building)
    {
      if (groups.TryGetValue(id, out var built))
      {
        return built;
      }

      var group = stored[id];
      building.Add(id);
      var parent = group.Parent is { } parentId && stored.ContainsKey(parentId) && !building.Contains(parentId)
        ? Build(parentId, building)
        : null;
      building.Remove(id);
      return groups[id] = new QueryGroup(group.Name, group.Order, group.Description, parent);
    }

    foreach (var id in stored.Keys)
    {
      Build(id, []);
    }

    return groups;
  }
}
