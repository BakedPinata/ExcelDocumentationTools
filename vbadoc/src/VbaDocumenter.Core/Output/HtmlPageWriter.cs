using System.Text;
using System.Text.RegularExpressions;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Output;

/// <summary>
/// Builds the single, self-contained documentation page: the template from Resources/Page with the styles,
/// scripts, data and (optionally) source written inline. Nothing in the page refers to another file or URL.
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

  public static string Build(DependencyModel model, VbaProject project, string title, bool embedSource)
  {
    var data = PageData.From(model, project, title, embedSource);
    var parts = new Dictionary<string, string>
    {
      ["TITLE"] = Escape(title),
      ["TOOL_VERSION"] = Escape(ToolVersion.Current.ToString()),
      ["TOOL_VERSION_DESCRIPTION"] = Escape(ToolVersion.Current.Description),
      ["REPOSITORY_URL"] = Escape(ToolVersion.RepositoryUrl),
      // A data URI, so the icon is part of the page and the CSP (img-src data:) allows it.
      ["FAVICON"] = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(EmbeddedResources.ReadText("Page/favicon.svg"))),
      ["STYLE"] = EmbeddedResources.ReadText("Page/page.css"),
      ["DATA"] = data.ToJson(),
      ["SOURCES"] = embedSource ? EmbeddedSources(project) : "",
      ["SCRIPTS"] = Scripts(),
    };

    // Each {{NAME}} in the template is replaced once. Regex.Replace only scans the template itself, so a
    // "{{...}}" that happens to appear inside inserted content is left alone.
    var template = EmbeddedResources.ReadText("Page/page.html");
    var html = Regex.Replace(template, @"\{\{(\w+)\}\}", m => parts[m.Groups[1].Value]);
    return html.ReplaceLineEndings("\n");
  }

  /// <summary>Writes the page if its content changed. Returns true if the file was written.</summary>
  public static bool Write(string outputFolder, string fileName, string html)
  {
    var path = Path.Combine(outputFolder, fileName);
    var bytes = new UTF8Encoding(false).GetBytes(html);
    if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return false;
    File.WriteAllBytes(path, bytes);
    return true;
  }

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

  static string Scripts()
  {
    var sb = new StringBuilder();
    // Licence first, as a comment, so it travels with the code.
    var d3 = VendoredScripts.D3;
    var licence = $"/*! {d3.Name} {d3.Version} ({d3.LicenceName} licence)\n\n{d3.LicenceText.Trim()}\n*/\n";
    AppendScript(sb, $"{d3.Name} {d3.Version}", licence + d3.Code);
    foreach (var name in PageScripts)
      AppendScript(sb, name, EmbeddedResources.ReadText(name));
    return sb.ToString();
  }

  static void AppendScript(StringBuilder sb, string name, string code)
  {
    // Inside <script>, "</script" would end the element early and "<!--" with "<script" changes how the end
    // tag is found. Refuse rather than write a page that silently breaks.
    if (code.Contains("</script", StringComparison.OrdinalIgnoreCase) || code.Contains("<script", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"Script '{name}' contains '<script' or '</script' and can't be inlined.");
    sb.Append("<script>\n").Append(code.TrimEnd()).Append("\n</script>\n");
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
      sb.Append($"<pre hidden class=\"vbadoc-source\" data-module=\"{Escape(module.Name)}\">\n")
        .Append(Escape(text))
        .Append("</pre>\n");
    }
    return sb.ToString();
  }

  /// <summary>Escapes text for HTML content or a double-quoted attribute. Leaves non-ASCII alone (the page is UTF-8).</summary>
  static string Escape(string text) =>
      text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
