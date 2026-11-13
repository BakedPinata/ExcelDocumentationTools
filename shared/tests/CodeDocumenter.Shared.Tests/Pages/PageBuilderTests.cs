using System.Text;
using CodeDocumenter.Shared.Pages;

namespace CodeDocumenter.Shared.Tests.Pages;

public sealed class PageBuilderTests
{
  [Fact]
  public void Placeholders_are_filled_in_a_single_pass()
  {
    var values = new Dictionary<string, string> { ["TITLE"] = "{{DATA}}", ["DATA"] = "data" };

    var html = PageBuilder.Fill("<h1>{{TITLE}}</h1>{{DATA}}", values);

    Assert.Equal("<h1>{{DATA}}</h1>data", html);
  }

  [Fact]
  public void An_unknown_placeholder_is_an_error()
  {
    var exception = Assert.Throws<InvalidOperationException>(() => PageBuilder.Fill("{{MISSING}}", new Dictionary<string, string>()));

    Assert.Contains("{{MISSING}}", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Only_upper_case_names_are_placeholders()
  {
    Assert.Equal("{{data}}", PageBuilder.Fill("{{data}}", new Dictionary<string, string>()));
  }

  [Theory]
  [InlineData("R&D <draft>", "R&amp;D &lt;draft&gt;")]
  [InlineData("say \"hi\"", "say &quot;hi&quot;")]
  [InlineData("It's Région", "It's Région")]
  public void Escaping_covers_HTML_content_and_double_quoted_attributes_and_nothing_else(string text, string expected)
  {
    Assert.Equal(expected, PageBuilder.Escape(text));
  }

  [Fact]
  public void JSON_is_made_inert_inside_a_script_element()
  {
    var separators = new string([(char)0x2028, (char)0x2029]);

    var json = PageBuilder.EscapeForScriptElement("{\"name\":\"</script><b>&" + separators + "\"}");

    Assert.Equal("{\"name\":\"\\u003C/script\\u003E\\u003Cb\\u003E\\u0026\\u2028\\u2029\"}", json);
  }

  [Fact]
  public void The_favicon_is_a_data_URI()
  {
    var uri = PageBuilder.FaviconDataUri("<svg/>");

    Assert.Equal("data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg/>")), uri);
  }

  [Fact]
  public void D3_comes_first_with_its_licence_then_the_shells_script_then_the_tools()
  {
    // A tool's scripts come from its own assembly, so the list is empty here; the tools' tests cover theirs.
    var scripts = PageBuilder.Scripts(typeof(PageBuilder).Assembly, []);

    Assert.StartsWith($"<script>\n/*! d3 {VendoredScripts.D3Version} (ISC licence)\n\n", scripts, StringComparison.Ordinal);
    Assert.Contains($"*/\n// https://d3js.org v{VendoredScripts.D3Version} ", scripts, StringComparison.Ordinal);
    Assert.Contains("\n</script>\n<script>\n// shell.js:", scripts, StringComparison.Ordinal);
    Assert.EndsWith("window.CodeDocumenterShell = { create: create };\n})();\n</script>\n", scripts, StringComparison.Ordinal);
  }

  [Fact]
  public void A_missing_script_names_itself_and_its_assembly()
  {
    var exception = Assert.Throws<InvalidOperationException>(() => PageBuilder.Scripts(typeof(PageBuilder).Assembly, ["Page/missing.js"]));

    Assert.Contains("'Page/missing.js'", exception.Message, StringComparison.Ordinal);
    Assert.Contains("CodeDocumenter.Shared", exception.Message, StringComparison.Ordinal);
  }
}
