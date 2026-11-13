using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CodeDocumenter.Shared;
using CodeDocumenter.Shared.Pages;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Importing;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>
/// Writes a single, self-contained interactive HTML page (FR-HTML-1 to FR-HTML-9) on the shell vbadoc's page shares
/// (<see cref="ShellPage"/>): pqdoc's view and search box from Resources/Page, with the styles, scripts, the vendored
/// D3.js library and the graph as JSON written inline, so the file works offline and its Content-Security-Policy can
/// block all network access.
/// </summary>
public sealed class HtmlExporter : IExporter
{
  /// <summary>Our own scripts, in load order after D3.js.</summary>
  private static readonly string[] PageScripts = ["Page/m-highlight.js", "Page/page.js", "Page/graph.js"];

  private static readonly Assembly Resources = typeof(HtmlExporter).Assembly;
  private static readonly Lazy<string> Search = new(() => EmbeddedResources.ReadText(Resources, "Page/search.html"));
  private static readonly Lazy<string> Views = new(() => EmbeddedResources.ReadText(Resources, "Page/views.html"));
  private static readonly Lazy<string> Stylesheet = new(() => EmbeddedResources.ReadText(Resources, "Page/page.css"));
  private static readonly Lazy<string> Favicon = new(() => EmbeddedResources.ReadText(Resources, "Page/favicon.svg"));

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

    var counts = $"{Plural(graph.Nodes.Count, "query", "queries")}, {Plural(graph.Edges.Count, "dependency", "dependencies")}";
    var cycles = graph.Cycles.Count == 0
      ? string.Empty
      : $", {Plural(graph.Cycles.Count, "circular dependency", "circular dependencies")}";

    var views = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["NODE_COUNT"] = graph.Nodes.Count.ToString(CultureInfo.InvariantCulture),
    };

    var page = new ShellPage
    {
      Tool = "pqdoc",
      Version = Version,
      PageTitle = $"{context.Title}: {counts}",
      Title = context.Title,
      Tabs = [new ShellTab("graph", "Dependency graph")],
      SearchHtml = Search.Value,
      LoadFolderHint = "Choose the folder that holds this page's .m files, to see each query's source",
      Summary = $"{counts}{cycles}.",
      WorkbookSaved = context.WorkbookSaved,
      PanelName = "Details",
      ExplorerLabel = "Queries",
      ViewsHtml = PageBuilder.Fill(Views.Value, views),
      FaviconSvg = Favicon.Value,
      Stylesheet = Stylesheet.Value,
      DataJson = SerialiseGraph(graph, context),
      ViewsJson = context.ViewsJson ?? string.Empty,
      SourcesHtml = context.EmbedSource && context.Sources is not null ? EmbeddedSources(context.Sources) : string.Empty,
      ScriptAssembly = Resources,
      Scripts = PageScripts,
    };

    return page.Render();
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

    // For the explorer (FR-HTML-11): each query's kind and group, and the groups in Excel's order.
    var details = new Dictionary<string, QueryDetails>(DependencyGraph.NameComparer);
    foreach (var detail in context.Details ?? [])
    {
      details.TryAdd(detail.Query, detail);
    }

    var groups = GroupDocuments(details.Values.Select(d => d.Group), out var groupIndex);
    var document = new GraphDocument(
      context.Title,
      sourceMode,
      graph.Nodes.Select(n => new QueryDocument(
        n.Name, RoleName(n.Role), n.IsInCycle, n.UpstreamCount, n.DownstreamCount,
        files.GetValueOrDefault(n.Name)?.FileName, files.GetValueOrDefault(n.Name)?.Hash,
        details.GetValueOrDefault(n.Name)?.IsFunction == true ? "function" : "query",
        details.GetValueOrDefault(n.Name)?.Group is { } group ? groupIndex[group] : null,
        graph.StepsOf(n.Name).Select(s => new StepDocument(s.Name, s.Line, s.Uses)).ToArray(),
        files.GetValueOrDefault(n.Name) is { } file ? QueryImport.NeedsOf(file.Text).Select(need => new NeedDocument(need.What, need.Why)).ToArray() : [])).ToArray(),
      graph.Edges.Select(e => new EdgeDocument(
        e.Upstream,
        e.Downstream,
        cycleGroupOf.TryGetValue(e.Upstream, out var a) && cycleGroupOf.TryGetValue(e.Downstream, out var b) && a == b,
        graph.LinesOf(e))).ToArray(),
      graph.Cycles.Select(c => c.Members).ToArray(),
      groups);

    return PageBuilder.EscapeForScriptElement(JsonSerializer.Serialize(document, JsonOptions));
  }

  /// <summary>
  /// The groups that hold a query, directly or through the groups inside them, as Excel's Queries pane lists them: each
  /// group before the groups inside it, and groups beside each other in Excel's order, then by name. A group with no
  /// queries in it isn't listed, as the explorer has nothing to show under it. <paramref name="index"/> gives each
  /// group's place in the list, which is how queries and groups refer to their group.
  /// </summary>
  private static GroupDocument[] GroupDocuments(IEnumerable<QueryGroup?> groupsOfQueries, out Dictionary<QueryGroup, int> index)
  {
    var used = new HashSet<QueryGroup>();
    foreach (var group in groupsOfQueries)
    {
      foreach (var step in group?.Path() ?? [])
      {
        used.Add(step);
      }
    }

    var childrenOf = used.ToLookup(g => g.Parent);
    var list = new List<GroupDocument>();
    var places = new Dictionary<QueryGroup, int>();
    void Add(QueryGroup? parent)
    {
      foreach (var group in childrenOf[parent].OrderBy(g => g.Order).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Name, StringComparer.Ordinal))
      {
        places[group] = list.Count;
        list.Add(new GroupDocument(group.Name, parent is null ? null : places[parent], group.Description));
        Add(group);
      }
    }

    Add(null);
    index = places;
    return [.. list];
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
    string SourceMode,
    IReadOnlyList<QueryDocument> Queries,
    IReadOnlyList<EdgeDocument> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    IReadOnlyList<GroupDocument> Groups);

  /// <param name="File">The query's .m file name, or null when it has no readable file.</param>
  /// <param name="Hash">The first 16 hex digits of the SHA-256 of that file, or null.</param>
  /// <param name="Kind">"function" or "query".</param>
  /// <param name="Group">Its Excel query group, as its place in <see cref="GraphDocument.Groups"/>, or null.</param>
  /// <param name="Steps">Its <c>let</c> steps, then its <c>in</c> expression (FR-HTML-20); empty without <c>let</c>.</param>
  /// <param name="Needs">What it relies on that doesn't come with it, for the Export tab (see <see cref="QueryImport"/>).</param>
  private sealed record QueryDocument(
    string Id, string Role, bool InCycle, int Upstream, int Downstream, string? File, string? Hash, string Kind, int? Group,
    IReadOnlyList<StepDocument> Steps, IReadOnlyList<NeedDocument> Needs);

  /// <summary>What a query relies on that doesn't come with it.</summary>
  private sealed record NeedDocument(string What, string Why);

  /// <param name="Name">The step's name, or null for the <c>in</c> expression.</param>
  private sealed record StepDocument(string? Name, int Line, IReadOnlyList<string> Uses);

  /// <param name="Parent">The group it's in, as its place in <see cref="GraphDocument.Groups"/>, or null at the top level.</param>
  /// <param name="Description">Its description in Excel, or null.</param>
  private sealed record GroupDocument(string Name, int? Parent, string? Description);

  /// <param name="Lines">The lines of <paramref name="To"/>'s source that name <paramref name="From"/>, for the details
  /// panel's links (FR-HTML-19); empty when they aren't known.</param>
  private sealed record EdgeDocument(string From, string To, bool InCycle, IReadOnlyList<int> Lines);
}
