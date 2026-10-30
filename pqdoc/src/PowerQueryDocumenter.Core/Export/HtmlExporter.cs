using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>
/// Writes a single, self-contained interactive HTML page (FR-HTML-1 to FR-HTML-9): the template in
/// Resources/Page with its styles, scripts, the vendored D3.js library and the graph as JSON written
/// inline, so the file works offline and its Content-Security-Policy can block all network access.
/// </summary>
public sealed partial class HtmlExporter : IExporter
{
  /// <summary>The vendored D3.js version, named in the licence comment in the page.</summary>
  public const string D3Version = "7.9.0";

  /// <summary>Our own scripts, in load order after D3.js.</summary>
  private static readonly string[] PageScripts = ["Page/page.js", "Page/graph.js"];

  private static readonly Lazy<string> Template = new(() => ReadResource("Page/page.html"));
  private static readonly Lazy<string> Stylesheet = new(() => ReadResource("Page/page.css"));
  private static readonly Lazy<string> Favicon = new(() =>
    "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(ReadResource("Page/favicon.svg"))));
  private static readonly Lazy<string> Scripts = new(BuildScripts);

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
      ["TOOL_VERSION"] = WebUtility.HtmlEncode(ToolVersion.Current.ToString()),
      ["TOOL_VERSION_DESCRIPTION"] = WebUtility.HtmlEncode(ToolVersion.Current.Description),
      ["REPOSITORY_URL"] = WebUtility.HtmlEncode(ToolVersion.RepositoryUrl),
      // A data URI, so the icon is part of the page and the Content-Security-Policy (img-src data:) allows it.
      ["FAVICON"] = Favicon.Value,
      ["STYLE"] = Stylesheet.Value,
      ["DATA"] = SerialiseGraph(graph, context),
      ["SCRIPTS"] = Scripts.Value,
    };

    // A single pass, so text inserted for one placeholder is never scanned for another.
    return PlaceholderPattern().Replace(Template.Value, match =>
      values.TryGetValue(match.Groups[1].Value, out var value)
        ? value
        : throw new InvalidOperationException($"The HTML template contains an unknown placeholder '{match.Value}'."));
  }

  /// <summary>
  /// Serialises the graph as JSON that is safe inside a <c>&lt;script&gt;</c> element (FR-HTML-7).
  /// A query's id is its name, as a module's is in vbadoc's page. The graph always spells a name the
  /// same way, so the page can compare names exactly.
  /// </summary>
  public static string SerialiseGraph(DependencyGraph graph, ExportContext context)
  {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(context);

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
      graph.Nodes.Select(n => new QueryDocument(n.Name, RoleName(n.Role), n.IsInCycle, n.UpstreamCount, n.DownstreamCount)).ToArray(),
      graph.Edges.Select(e => new EdgeDocument(
        e.Upstream,
        e.Downstream,
        cycleGroupOf.TryGetValue(e.Upstream, out var a) && cycleGroupOf.TryGetValue(e.Downstream, out var b) && a == b)).ToArray(),
      graph.Cycles.Select(c => c.Members).ToArray());

    return EscapeForScriptElement(JsonSerializer.Serialize(document, JsonOptions));
  }

  /// <summary>
  /// Escapes JSON so it cannot terminate or alter the <c>&lt;script&gt;</c> element that holds it:
  /// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> become <c><</c>, <c>></c> and <c>&</c>,
  /// so a name such as <c>&lt;/script&gt;</c> stays inert. JSON only allows these characters inside
  /// strings, where the escapes mean the same thing. U+2028 and U+2029 are escaped as well.
  /// The default System.Text.Json encoder already does this; it is repeated here so the guarantee
  /// does not depend on serialiser settings.
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

  private static string BuildScripts()
  {
    var scripts = new StringBuilder();

    // Licence first, as a comment, so it travels with the code.
    var licence = $"/*! d3 {D3Version} (ISC licence)\n\n{ReadResource("Vendor/d3.LICENSE.txt").Trim()}\n*/\n";
    AppendScript(scripts, $"d3 {D3Version}", licence + ReadResource("Vendor/d3.min.js"));

    foreach (var name in PageScripts)
    {
      AppendScript(scripts, name, ReadResource(name));
    }

    return scripts.ToString();
  }

  private static void AppendScript(StringBuilder scripts, string name, string code)
  {
    // Inside <script>, "</script" would end the element early and "<!--" with "<script" changes how
    // the end tag is found. Refuse rather than write a page that silently breaks.
    if (code.Contains("</script", StringComparison.OrdinalIgnoreCase) || code.Contains("<script", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"Script '{name}' contains '<script' or '</script' and can't be inlined.");
    }

    scripts.Append("<script>\n").Append(code.TrimEnd()).Append("\n</script>\n");
  }

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
    IReadOnlyList<QueryDocument> Queries,
    IReadOnlyList<EdgeDocument> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles);

  private sealed record QueryDocument(string Id, string Role, bool InCycle, int Upstream, int Downstream);

  private sealed record EdgeDocument(string From, string To, bool InCycle);
}
