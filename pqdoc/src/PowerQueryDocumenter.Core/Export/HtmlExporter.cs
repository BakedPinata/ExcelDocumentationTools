using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Pages;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>
/// Writes a single, self-contained interactive HTML page (FR-HTML-1 to FR-HTML-9): the template in
/// Resources/Page with its styles, scripts, the vendored D3.js library and the graph as JSON written
/// inline, so the file works offline and its Content-Security-Policy can block all network access. It's put together
/// as vbadoc's page is (<see cref="PageBuilder"/>).
/// </summary>
public sealed class HtmlExporter : IExporter
{
  /// <summary>Our own scripts, in load order after D3.js.</summary>
  private static readonly string[] PageScripts = ["Page/m-highlight.js", "Page/page.js", "Page/graph.js"];

  private static readonly Assembly Resources = typeof(HtmlExporter).Assembly;
  private static readonly Lazy<string> Template = new(() => EmbeddedResources.ReadText(Resources, "Page/page.html"));
  private static readonly Lazy<string> Stylesheet = new(() => EmbeddedResources.ReadText(Resources, "Page/page.css"));
  private static readonly Lazy<string> Favicon = new(() => PageBuilder.FaviconDataUri(EmbeddedResources.ReadText(Resources, "Page/favicon.svg")));
  private static readonly Lazy<string> Scripts = new(() => PageBuilder.Scripts(Resources, PageScripts));

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  /// <summary>The build of pqdoc that writes the page, shown in its top bar.</summary>
  public static ToolVersion Version { get; } = ToolVersion.Of(typeof(HtmlExporter).Assembly);

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
      ["PAGE_TITLE"] = PageBuilder.Escape($"{context.Title}: {counts}, generated {generated}"),
      ["TITLE"] = PageBuilder.Escape(context.Title),
      ["SUMMARY"] = PageBuilder.Escape($"{counts}{cycles}. Generated {generated}."),
      ["NODE_COUNT"] = graph.Nodes.Count.ToString(CultureInfo.InvariantCulture),
      ["TOOL_VERSION"] = PageBuilder.Escape(Version.ToString()),
      ["TOOL_VERSION_DESCRIPTION"] = PageBuilder.Escape(Version.Description),
      ["REPOSITORY_URL"] = PageBuilder.Escape(ToolVersion.RepositoryUrl),
      ["FAVICON"] = Favicon.Value,
      ["STYLE"] = Stylesheet.Value,
      ["DATA"] = SerialiseGraph(graph, context),
      ["SOURCES"] = context.EmbedSource && context.Sources is not null ? EmbeddedSources(context.Sources) : string.Empty,
      ["SCRIPTS"] = Scripts.Value,
    };

    return PageBuilder.Fill(Template.Value, values);
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

    // Each query's file name and hash, so the page can find and check its source; the text itself goes in the page
    // only with --embed-source (FR-SRC-2). A query whose file couldn't be read has neither.
    var files = new Dictionary<string, QueryFile>(DependencyGraph.NameComparer);
    foreach (var file in context.Sources ?? [])
    {
      files.TryAdd(file.Query, file);
    }

    var sourceMode = files.Count == 0 ? "none" : context.EmbedSource ? "embedded" : "folder";
    var document = new GraphDocument(
      context.Title,
      FormatTimestamp(context.GeneratedAt),
      sourceMode,
      graph.Nodes.Select(n => new QueryDocument(
        n.Name, RoleName(n.Role), n.IsInCycle, n.UpstreamCount, n.DownstreamCount,
        files.GetValueOrDefault(n.Name)?.FileName, files.GetValueOrDefault(n.Name)?.Hash)).ToArray(),
      graph.Edges.Select(e => new EdgeDocument(
        e.Upstream,
        e.Downstream,
        cycleGroupOf.TryGetValue(e.Upstream, out var a) && cycleGroupOf.TryGetValue(e.Downstream, out var b) && a == b)).ToArray(),
      graph.Cycles.Select(c => c.Members).ToArray());

    return PageBuilder.EscapeForScriptElement(JsonSerializer.Serialize(document, JsonOptions));
  }

  /// <summary>
  /// For --embed-source: each query's text in its own hidden &lt;pre&gt;, as vbadoc does, so a change to one query
  /// changes only its own lines of the page. The page reads the text back with textContent.
  /// </summary>
  private static string EmbeddedSources(IReadOnlyList<QueryFile> sources)
  {
    var html = new StringBuilder();
    foreach (var file in sources.OrderBy(f => f.Query, DependencyGraph.NameComparer))
    {
      // The HTML parser drops a line break straight after <pre>, so add one to keep a leading blank line intact.
      html.Append("<pre hidden class=\"pqdoc-source\" data-query=\"").Append(PageBuilder.Escape(file.Query)).Append("\">\n")
        .Append(PageBuilder.Escape(file.Text))
        .Append("</pre>\n");
    }

    return html.ToString();
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

  /// <param name="SourceMode">"embedded" (the page holds the text), "folder" (Load folder) or "none".</param>
  private sealed record GraphDocument(
    string Title,
    string Generated,
    string SourceMode,
    IReadOnlyList<QueryDocument> Queries,
    IReadOnlyList<EdgeDocument> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles);

  /// <param name="File">The query's .m file name, or null when it has no readable file.</param>
  /// <param name="Hash">The first 16 hex digits of the SHA-256 of that file, or null.</param>
  private sealed record QueryDocument(string Id, string Role, bool InCycle, int Upstream, int Downstream, string? File, string? Hash);

  private sealed record EdgeDocument(string From, string To, bool InCycle);
}
