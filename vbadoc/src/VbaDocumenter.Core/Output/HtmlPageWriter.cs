using System.Globalization;
using System.Reflection;
using System.Text;
using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Pages;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Output;

/// <summary>The page, as the shared <see cref="OverwriteResolver"/> sees it: the only output vbadoc asks about.</summary>
public sealed record PlannedPage(string Path) : IPlannedOutput
{
  public string Name => "page";
}

/// <summary>
/// Builds the single, self-contained documentation page on the shell pqdoc's page shares (<see cref="ShellPage"/>):
/// vbadoc's views, search box and trail from Resources/Page, with the styles, scripts, data and (optionally) source
/// written inline. Nothing in the page refers to another file or URL.
/// </summary>
public static class HtmlPageWriter
{
  /// <summary>Added to the workbook's name to name the page, so a page that travels on its own says what it documents.</summary>
  public const string NameSuffix = "-vba";

  /// <summary>
  /// The page's default name, without ".html": the workbook's name and <see cref="NameSuffix"/>, so
  /// <c>Sales.xlsm</c> gives <c>Sales-vba</c>.
  /// </summary>
  public static string DefaultName(string workbookPath) => SafeForAddress(Path.GetFileNameWithoutExtension(workbookPath)) + NameSuffix;

  /// <summary>
  /// Replaces characters that break or confuse a browser's address: <c>#</c> starts the part the page uses for Back
  /// and Forward, and <c>%</c> starts an escape. Only the file name changes; the page still shows the real name.
  /// </summary>
  public static string SafeForAddress(string name) => name.Replace('#', '_').Replace('%', '_');

  /// <summary>The page's file name for a name chosen by <see cref="DefaultName"/> or <c>--output-name</c>.</summary>
  public static string FileName(string name) => name + ".html";

  // The page template's generator tag starts with the tool's name, which is how older pages are recognised.
  const string GeneratorTag = "<meta name=\"generator\" content=\"vbadoc ";

  /// <summary>Our own scripts, in load order after the vendored libraries.</summary>
  static readonly string[] PageScripts = ["Page/vba-highlight.js", "Page/page.js", "Page/graph.js", "Page/classes.js"];

  static Assembly Resources => typeof(HtmlPageWriter).Assembly;

  /// <summary>The build of vbadoc that writes the page, shown in its top bar.</summary>
  public static ToolVersion Version { get; } = ToolVersion.Of(typeof(HtmlPageWriter).Assembly);

  public static string Build(DependencyModel model, VbaProject project, string title, bool embedSource)
  {
    var data = PageData.From(model, project, title, embedSource);
    var page = new ShellPage
    {
      Tool = "vbadoc",
      Version = Version,
      PageTitle = title,
      Title = title,
      Tabs = [new ShellTab("graph", "Dependency graph"), new ShellTab("classes", "Class diagram")],
      SearchHtml = EmbeddedResources.ReadText(Resources, "Page/search.html"),
      LoadFolderHint = "Choose the folder that holds this page's module files, to see their source",
      Summary = Summary(model),
      WorkbookSaved = project.Saved,
      PanelName = "Source",
      ExplorerLabel = "Modules and members",
      BeforeLayoutHtml = EmbeddedResources.ReadText(Resources, "Page/trail.html"),
      ViewsHtml = EmbeddedResources.ReadText(Resources, "Page/views.html"),
      FaviconSvg = EmbeddedResources.ReadText(Resources, "Page/favicon.svg"),
      Stylesheet = EmbeddedResources.ReadText(Resources, "Page/page.css"),
      DataJson = data.ToJson(),
      SourcesHtml = embedSource ? EmbeddedSources(project) : "",
      ScriptAssembly = Resources,
      Scripts = PageScripts,
    };

    var html = page.Render();
    return html.ReplaceLineEndings("\n");
  }

  /// <summary>
  /// The page's summary, as pqdoc's says its queries and dependencies: <c>12 modules, 85 members, 20 module and 110
  /// member dependencies.</c>
  /// </summary>
  public static string Summary(DependencyModel model)
  {
    ArgumentNullException.ThrowIfNull(model);
    var members = model.Members.Count(m => !m.IsModuleNode);
    return $"{Plural(model.Modules.Count, "module", "modules")}, {Plural(members, "member", "members")}, " +
        $"{model.ModuleEdges.Count.ToString(CultureInfo.InvariantCulture)} module and " +
        $"{Plural(model.MemberEdges.Count, "member dependency", "member dependencies")}.";
  }

  static string Plural(int count, string one, string many) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

  /// <summary>Writes the page if its content changed. Returns true if the file was written.</summary>
  public static bool Write(string outputFolder, string fileName, string html)
  {
    var path = Path.Combine(outputFolder, fileName);
    if (!WouldChange(path, html)) return false;
    AtomicFileWriter.Write(path, Bytes(html));
    return true;
  }

  /// <summary>
  /// Whether writing the page would change the file at <paramref name="path"/>: it's missing or holds something else.
  /// An unchanged page has nothing to overwrite, so vbadoc doesn't ask about it.
  /// </summary>
  public static bool WouldChange(string path, string html) =>
      !File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(Bytes(html));

  static byte[] Bytes(string html) => new UTF8Encoding(false).GetBytes(html);

  /// <summary>
  /// Deletes pages vbadoc wrote earlier under another name in the output folder, which belongs to vbadoc: a renamed
  /// workbook's page, or <c>index.html</c> from before 0.8.0. They're recognised by their generator tag, so a page
  /// written by hand or by another tool is left alone. Returns the names of the files deleted.
  /// </summary>
  public static IReadOnlyList<string> RemoveOlderPages(string outputFolder, string currentFileName)
  {
    var removed = new List<string>();
    if (!Directory.Exists(outputFolder)) return removed;
    foreach (var path in Directory.GetFiles(outputFolder, "*.html"))
    {
      var name = Path.GetFileName(path);
      if (name.Equals(currentFileName, StringComparison.OrdinalIgnoreCase) || !WrittenByVbadoc(path)) continue;
      File.Delete(path);
      removed.Add(name);
    }
    removed.Sort(StringComparer.OrdinalIgnoreCase);
    return removed;
  }

  /// <summary>Whether a page carries vbadoc's generator tag, which is within its first lines.</summary>
  static bool WrittenByVbadoc(string path)
  {
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var head = new byte[2048];
    var length = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
    return Encoding.UTF8.GetString(head, 0, length).Contains(GeneratorTag, StringComparison.Ordinal);
  }

  /// <summary>
  /// For --embed-source: each module's source in its own hidden &lt;pre&gt;, so a change to one module changes only
  /// its own lines of the page. The text is HTML-escaped and the page reads it back with textContent.
  /// </summary>
  static string EmbeddedSources(VbaProject project)
  {
    var encoding = CodePages.Get(project.CodePage);
    var sb = new StringBuilder();
    foreach (var module in project.Modules)
    {
      var text = encoding.GetString(ProjectExporter.FileContent(module)).ReplaceLineEndings("\n");
      // The HTML parser drops a newline straight after <pre>, so add one to keep a leading blank line intact.
      sb.Append($"<pre hidden class=\"vbadoc-source\" data-module=\"{PageBuilder.Escape(module.Name)}\">\n")
        .Append(PageBuilder.Escape(text))
        .Append("</pre>\n");
    }
    return sb.ToString();
  }
}
