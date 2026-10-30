using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Model;

namespace PowerQueryDocumenter.Export;

/// <summary>
/// Writes a single, self-contained interactive HTML page (FR-HTML-1 to FR-HTML-9): the page
/// template and the vendored D3.js library are embedded resources, and the graph is embedded as
/// JSON, so the file works offline and makes no external requests.
/// </summary>
public sealed partial class HtmlExporter : IExporter
{
  /// <summary>Resource name of the page template.</summary>
  public const string TemplateResourceName = "PowerQueryDocumenter.Resources.graph-template.html";

  /// <summary>Resource name of the vendored D3.js v7.9.0 library (ISC licence).</summary>
  public const string D3ResourceName = "Vendor/d3.min.js";

  private static readonly Lazy<string> Template = new(() => ReadResource(TemplateResourceName));
  private static readonly Lazy<string> D3Library = new(() => ReadResource(D3ResourceName));

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  /// <inheritdoc />
  public OutputFormat Format => OutputFormat.Html;

  /// <inheritdoc />
  public string FileExtension => Format.FileExtension();

  /// <inheritdoc />
  public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(output);
    var html = Render(graph, context);
    await output.WriteAsync(new UTF8Encoding(false).GetBytes(html), cancellationToken).ConfigureAwait(false);
  }

  /// <summary>Renders the complete page.</summary>
  public static string Render(DependencyGraph graph, ExportContext context)
  {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(context);

    var generated = FormatTimestamp(context.GeneratedAt);
    var counts = $"{Plural(graph.Nodes.Count, "query", "queries")}, {Plural(graph.Edges.Count, "dependency", "dependencies")}";
    var cycles = graph.Cycles.Count == 0
      ? string.Empty
      : $", {Plural(graph.Cycles.Count, "circular dependency", "circular dependencies")}";

    var values = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["PAGE_TITLE"] = WebUtility.HtmlEncode($"{context.OutputName}: {counts}, generated {generated}"),
      ["OUTPUT_NAME"] = WebUtility.HtmlEncode(context.OutputName),
      ["SUMMARY"] = WebUtility.HtmlEncode($"{counts}{cycles}. Generated {generated}."),
      ["NODE_COUNT"] = graph.Nodes.Count.ToString(CultureInfo.InvariantCulture),
      ["GRAPH_JSON"] = SerializeGraph(graph, context),
      ["D3_LIBRARY"] = D3Library.Value,
    };

    // A single pass, so text inserted for one placeholder is never scanned for another.
    return PlaceholderPattern().Replace(Template.Value, match =>
      values.TryGetValue(match.Groups[1].Value, out var value)
        ? value
        : throw new InvalidOperationException($"The HTML template contains an unknown placeholder '{match.Value}'."));
  }

  /// <summary>
  /// Serializes the graph as JSON that is safe inside a <c>&lt;script&gt;</c> element (FR-HTML-7).
  /// </summary>
  public static string SerializeGraph(DependencyGraph graph, ExportContext context)
  {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(context);

    var ids = new Dictionary<string, int>(DependencyGraph.NameComparer);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      ids[graph.Nodes[i].Name] = i;
    }

    var cycleGroupOf = new Dictionary<string, int>(DependencyGraph.NameComparer);
    for (var i = 0; i < graph.Cycles.Count; i++)
    {
      foreach (var member in graph.Cycles[i].Members)
      {
        cycleGroupOf[member] = i;
      }
    }

    var document = new GraphDocument(
      context.OutputName,
      FormatTimestamp(context.GeneratedAt),
      graph.Nodes.Select((n, i) => new NodeDocument(i, n.Name, RoleName(n.Role), n.IsInCycle, n.UpstreamCount, n.DownstreamCount)).ToArray(),
      graph.Edges.Select(e => new LinkDocument(
        ids[e.Upstream],
        ids[e.Downstream],
        cycleGroupOf.TryGetValue(e.Upstream, out var a) && cycleGroupOf.TryGetValue(e.Downstream, out var b) && a == b)).ToArray(),
      graph.Cycles.Select(c => c.Path).ToArray());

    return EscapeForScriptElement(JsonSerializer.Serialize(document, JsonOptions));
  }

  /// <summary>
  /// Escapes JSON so it cannot terminate or alter the <c>&lt;script&gt;</c> element that holds it:
  /// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> become <c><</c>, <c>></c> and <c>&</c>,
  /// so a name such as <c>&lt;/script&gt;</c> stays inert. JSON only allows these characters inside
  /// strings, where the escapes mean the same thing. U+2028 and U+2029 are escaped as well.
  /// The default System.Text.Json encoder already does this; it is repeated here so the guarantee
  /// does not depend on serializer settings.
  /// </summary>
  public static string EscapeForScriptElement(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    return json
      .Replace("<", "\\u003C", StringComparison.Ordinal)
      .Replace(">", "\\u003E", StringComparison.Ordinal)
      .Replace("&", "\\u0026", StringComparison.Ordinal)
      .Replace("\u2028", "\\u2028", StringComparison.Ordinal)
      .Replace("\u2029", "\\u2029", StringComparison.Ordinal);
  }

  /// <summary>Formats the generation time, for example "2026-10-02 10:03 (UTC+08:00)".</summary>
  public static string FormatTimestamp(DateTimeOffset value) =>
    value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
    + " (UTC" + value.ToString("zzz", CultureInfo.InvariantCulture) + ")";

  private static string RoleName(QueryRole role) => role switch
  {
    QueryRole.Root => "root",
    QueryRole.Leaf => "leaf",
    QueryRole.Isolated => "isolated",
    _ => "intermediate",
  };

  private static string Plural(int count, string singular, string plural) =>
    count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);

  private static string ReadResource(string name)
  {
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
      ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing from the build.");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  [GeneratedRegex(@"\{\{([A-Z0-9_]+)\}\}")]
  private static partial Regex PlaceholderPattern();

  private sealed record GraphDocument(
    string OutputName,
    string Generated,
    IReadOnlyList<NodeDocument> Nodes,
    IReadOnlyList<LinkDocument> Links,
    IReadOnlyList<IReadOnlyList<string>> Cycles);

  private sealed record NodeDocument(int Id, string Name, string Role, bool InCycle, int Upstream, int Downstream);

  private sealed record LinkDocument(int Source, int Target, bool InCycle);
}
