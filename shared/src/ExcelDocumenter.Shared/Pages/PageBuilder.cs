using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ExcelDocumenter.Shared.Pages;

/// <summary>
/// The pieces every self-contained page is built from: filling the template's placeholders, inlining scripts with
/// vendored d3 first, the favicon as a data URI, and escaping text and JSON for where they go. Nothing in a page
/// refers to another file or URL.
/// </summary>
public static partial class PageBuilder
{
  // Written as character codes, because the characters themselves end a line in C# source.
  private static readonly string LineSeparator = ((char)0x2028).ToString();
  private static readonly string ParagraphSeparator = ((char)0x2029).ToString();

  /// <summary>
  /// Replaces each <c>{{NAME}}</c> in <paramref name="template"/> with its value. A single pass, so text inserted for
  /// one placeholder is never scanned for another.
  /// </summary>
  /// <exception cref="InvalidOperationException">The template has a placeholder with no value.</exception>
  public static string Fill(string template, IReadOnlyDictionary<string, string> values)
  {
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(values);
    return PlaceholderPattern().Replace(template, match =>
      values.TryGetValue(match.Groups[1].Value, out var value)
        ? value
        : throw new InvalidOperationException($"The HTML template contains an unknown placeholder '{match.Value}'."));
  }

  /// <summary>
  /// The page's scripts, each in its own <c>&lt;script&gt;</c> element: vendored d3 with its licence as a comment, then
  /// the tool's own scripts, embedded in <paramref name="assembly"/>, in the order given.
  /// </summary>
  /// <exception cref="InvalidOperationException">A script can't be inlined safely.</exception>
  public static string Scripts(Assembly assembly, IEnumerable<string> pageScripts)
  {
    ArgumentNullException.ThrowIfNull(assembly);
    ArgumentNullException.ThrowIfNull(pageScripts);
    var scripts = new StringBuilder();

    // Licence first, as a comment, so it travels with the code.
    var d3 = VendoredScripts.D3;
    var licence = $"/*! {d3.Name} {d3.Version} ({d3.LicenceName} licence)\n\n{d3.LicenceText.Trim()}\n*/\n";
    AppendScript(scripts, $"{d3.Name} {d3.Version}", licence + d3.Code);

    foreach (var name in pageScripts)
    {
      AppendScript(scripts, name, EmbeddedResources.ReadText(assembly, name));
    }

    return scripts.ToString();
  }

  /// <summary>An SVG icon as a data URI, so it's part of the page and the Content-Security-Policy (img-src data:) allows it.</summary>
  public static string FaviconDataUri(string svg)
  {
    ArgumentNullException.ThrowIfNull(svg);
    return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
  }

  /// <summary>
  /// Escapes text for HTML content or a double-quoted attribute. Other characters are left as they are, because the
  /// pages are UTF-8.
  /// </summary>
  public static string Escape(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    return text
      .Replace("&", "&amp;", StringComparison.Ordinal)
      .Replace("<", "&lt;", StringComparison.Ordinal)
      .Replace(">", "&gt;", StringComparison.Ordinal)
      .Replace("\"", "&quot;", StringComparison.Ordinal);
  }

  /// <summary>
  /// Makes JSON safe inside a <c>&lt;script&gt;</c> element: <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> become
  /// <c><</c>, <c>></c> and <c>&</c>, so a name such as <c>&lt;/script&gt;</c> stays inert. JSON only
  /// allows these characters inside strings, where the escapes mean the same thing. U+2028 and U+2029 are escaped as
  /// well. System.Text.Json's default encoder already does this; repeating it means the guarantee doesn't depend on
  /// serialiser settings.
  /// </summary>
  public static string EscapeForScriptElement(string json)
  {
    ArgumentNullException.ThrowIfNull(json);
    return json
      .Replace("<", "\\u003C", StringComparison.Ordinal)
      .Replace(">", "\\u003E", StringComparison.Ordinal)
      .Replace("&", "\\u0026", StringComparison.Ordinal)
      .Replace(LineSeparator, "\\u2028", StringComparison.Ordinal)
      .Replace(ParagraphSeparator, "\\u2029", StringComparison.Ordinal);
  }

  private static void AppendScript(StringBuilder scripts, string name, string code)
  {
    // Inside <script>, "</script" would end the element early and "<!--" with "<script" changes how the end tag is
    // found. Refuse rather than write a page that silently breaks.
    if (code.Contains("</script", StringComparison.OrdinalIgnoreCase) || code.Contains("<script", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"Script '{name}' contains '<script' or '</script' and can't be inlined.");
    }

    scripts.Append("<script>\n").Append(code.TrimEnd()).Append("\n</script>\n");
  }

  [GeneratedRegex(@"\{\{([A-Z0-9_]+)\}\}")]
  private static partial Regex PlaceholderPattern();
}
