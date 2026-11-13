using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDocumenter.Shared.Pages;

/// <summary>A tab in the top bar. It shows the view whose element has the id <c>view-</c><see cref="Name"/>.</summary>
/// <param name="Name">The view's name, such as <c>graph</c>; the tab's id is <c>tab-</c> and the name.</param>
/// <param name="Label">What the tab says.</param>
public sealed record ShellTab(string Name, string Label);

/// <summary>
/// A page built on the shell both tools share: the template in Resources/Shell with the shell's stylesheet and script,
/// and what the tool puts into it. The shell owns the top bar, tabs, theme, side panel, splitter, Load folder and source
/// view; the tool brings its views, its search box, its data and its scripts. Text is escaped here; HTML parts are put
/// in as they are.
/// </summary>
public sealed partial record ShellPage
{
  private static readonly Lazy<string> Template = new(() => EmbeddedResources.ReadText(typeof(ShellPage).Assembly, "Shell/shell.html"));
  private static readonly Lazy<string> ShellStylesheet = new(() => EmbeddedResources.ReadText(typeof(ShellPage).Assembly, "Shell/shell.css"));

  // The shell's own tab, after the tool's: its view is in the template.
  private static readonly ShellTab ExportTab = new("export", "Export");

  /// <summary>
  /// The tool's short name, such as <c>vbadoc</c>. It names the data block (<c>&lt;tool&gt;-data</c>), embedded source
  /// (<c>&lt;tool&gt;-source</c>) and the settings the page remembers (<c>&lt;tool&gt;-…</c>).
  /// </summary>
  public required string Tool { get; init; }

  /// <summary>The build of the tool, shown in the top bar and the generator tag.</summary>
  public required ToolVersion Version { get; init; }

  /// <summary>The browser tab's title, as text.</summary>
  public required string PageTitle { get; init; }

  /// <summary>The heading in the top bar, as text: what the page documents.</summary>
  public required string Title { get; init; }

  /// <summary>The views' tabs, in order. The first is shown until the tool's script says otherwise.</summary>
  public required IReadOnlyList<ShellTab> Tabs { get; init; }

  /// <summary>The search box's label and input, as HTML.</summary>
  public required string SearchHtml { get; init; }

  /// <summary>The tooltip on Load folder, as text.</summary>
  public required string LoadFolderHint { get; init; }

  /// <summary>
  /// What the page documents, in a sentence for the top bar, as text: its counts, such as <c>14 queries, 8
  /// dependencies.</c> Empty for none.
  /// </summary>
  public string Summary { get; init; } = string.Empty;

  /// <summary>
  /// When the workbook was last saved, from its own document properties, shown after <see cref="Summary"/>; null when
  /// not known. Never when the page was made, so the same workbook always gives the same page.
  /// </summary>
  public DateTimeOffset? WorkbookSaved { get; init; }

  /// <summary>Anything more for the top bar, before its switches, as HTML.</summary>
  public string TopBarHtml { get; init; } = string.Empty;

  /// <summary>The side panel's name, such as <c>Source</c>: its toggle's text and its label.</summary>
  public required string PanelName { get; init; }

  /// <summary>What the explorer's tree lists, for screen readers, such as <c>Modules and members</c>.</summary>
  public required string ExplorerLabel { get; init; }

  /// <summary>Anything between the top bar and the views, as HTML.</summary>
  public string BeforeLayoutHtml { get; init; } = string.Empty;

  /// <summary>The views, each an element with the id <c>view-</c> and its tab's name, as HTML.</summary>
  public required string ViewsHtml { get; init; }

  /// <summary>The tool's icon, as SVG.</summary>
  public required string FaviconSvg { get; init; }

  /// <summary>The tool's own stylesheet, which comes after the shell's.</summary>
  public required string Stylesheet { get; init; }

  /// <summary>The page's data as JSON. It is made safe inside its script element here.</summary>
  public required string DataJson { get; init; }

  /// <summary>For --embed-source, the hidden &lt;pre&gt; elements holding the source; otherwise empty.</summary>
  public string SourcesHtml { get; init; } = string.Empty;

  /// <summary>The assembly that embeds <see cref="Scripts"/>.</summary>
  public required Assembly ScriptAssembly { get; init; }

  /// <summary>The tool's own scripts, in load order after d3 and the shell's script.</summary>
  public required IReadOnlyList<string> Scripts { get; init; }

  /// <summary>Puts the page together.</summary>
  /// <exception cref="ArgumentException">The tool's name isn't a plain lower-case name.</exception>
  public string Render()
  {
    // The name goes into a script and ids unescaped, so it has to be plain.
    if (!ToolNamePattern().IsMatch(Tool))
    {
      throw new ArgumentException($"'{Tool}' isn't a plain lower-case tool name.", nameof(Tool));
    }

    var values = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["TOOL"] = Tool,
      ["TOOL_VERSION"] = PageBuilder.Escape(Version.ToString()),
      ["TOOL_VERSION_DESCRIPTION"] = PageBuilder.Escape(Version.Description),
      ["REPOSITORY_URL"] = PageBuilder.Escape(ToolVersion.RepositoryUrl),
      ["PAGE_TITLE"] = PageBuilder.Escape(PageTitle),
      ["TITLE"] = PageBuilder.Escape(Title),
      ["FAVICON"] = PageBuilder.FaviconDataUri(FaviconSvg),
      ["STYLE"] = ShellStylesheet.Value.TrimEnd() + "\n\n" + Stylesheet,
      ["TABS"] = TabsHtml([.. Tabs, ExportTab]),
      ["SEARCH"] = SearchHtml.TrimEnd(),
      ["LOAD_FOLDER_HINT"] = PageBuilder.Escape(LoadFolderHint),
      ["SUMMARY"] = SummaryHtml(Summary, WorkbookSaved),
      ["TOP_BAR"] = TopBarHtml.TrimEnd(),
      ["PANEL_NAME"] = PageBuilder.Escape(PanelName),
      ["PANEL_LABEL"] = PageBuilder.Escape(PanelName.ToLowerInvariant()),
      ["EXPLORER_LABEL"] = PageBuilder.Escape(ExplorerLabel),
      ["BEFORE_LAYOUT"] = BeforeLayoutHtml.TrimEnd(),
      ["VIEWS"] = ViewsHtml.TrimEnd(),
      ["DATA"] = PageBuilder.EscapeForScriptElement(DataJson),
      ["SOURCES"] = SourcesHtml,
      ["SCRIPTS"] = PageBuilder.Scripts(ScriptAssembly, Scripts),
    };

    return PageBuilder.Fill(Template.Value, values);
  }

  /// <summary>
  /// The summary in the top bar. The saved time is written in UTC, so the page is the same wherever it's made, and the
  /// shell's script shows it in the viewer's own time.
  /// </summary>
  private static string SummaryHtml(string summary, DateTimeOffset? saved)
  {
    if (summary.Length == 0 && saved is null)
    {
      return string.Empty;
    }

    var html = new StringBuilder("  <p class=\"summary muted\" id=\"page-summary\">").Append(PageBuilder.Escape(summary));
    if (saved is { } when)
    {
      var utc = when.ToUniversalTime();
      var iso = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
      html.Append(summary.Length > 0 ? " " : string.Empty)
        .Append("Workbook saved <time id=\"workbook-saved\" datetime=\"").Append(iso).Append("\" title=\"").Append(iso).Append("\">")
        .Append(utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append("</time>.");
    }

    return html.Append("</p>").ToString();
  }

  private static string TabsHtml(IReadOnlyList<ShellTab> tabs)
  {
    var html = new StringBuilder();
    for (var i = 0; i < tabs.Count; i++)
    {
      var name = PageBuilder.Escape(tabs[i].Name);
      html.Append("    <button type=\"button\" role=\"tab\" id=\"tab-").Append(name).Append("\" aria-controls=\"view-").Append(name)
        .Append(i == 0 ? "\" aria-selected=\"true\">" : "\" aria-selected=\"false\" tabindex=\"-1\">")
        .Append(PageBuilder.Escape(tabs[i].Label)).Append("</button>");
      if (i < tabs.Count - 1)
      {
        html.Append('\n');
      }
    }

    return html.ToString();
  }

  [GeneratedRegex("^[a-z][a-z0-9]*$")]
  private static partial Regex ToolNamePattern();
}
