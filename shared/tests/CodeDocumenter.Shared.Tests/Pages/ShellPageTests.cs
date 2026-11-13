using CodeDocumenter.Shared.Pages;

namespace CodeDocumenter.Shared.Tests.Pages;

public sealed class ShellPageTests
{
  private static ShellPage Page(Func<ShellPage, ShellPage>? change = null)
  {
    var page = new ShellPage
    {
      Tool = "pqdoc",
      Version = ToolVersion.Parse("pqdoc", "1.0.0+b7c0ebe8d952f156a36dd4ed8b545d572397f368"),
      PageTitle = "Book.xlsx: 2 queries",
      Title = "Book.xlsx",
      Tabs = [new ShellTab("graph", "Dependency graph"), new ShellTab("other", "Other <view>")],
      SearchHtml = "    <input id=\"search\" type=\"search\">\n",
      LoadFolderHint = "Choose the folder",
      PanelName = "Details",
      ExplorerLabel = "Queries",
      ViewsHtml = "  <section class=\"view\" id=\"view-graph\"></section>\n  <section class=\"view\" id=\"view-other\" hidden></section>\n",
      FaviconSvg = "<svg/>",
      Stylesheet = ".tool-rule { color: red; }",
      DataJson = "{\"name\":\"</script>\"}",
      ScriptAssembly = typeof(ShellPage).Assembly,
      Scripts = [],
    };
    return change is null ? page : change(page);
  }

  [Fact]
  public void Every_placeholder_is_filled()
  {
    var html = Page().Render();

    Assert.DoesNotMatch(@"\{\{[A-Z0-9_]+\}\}", html);
    Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
  }

  [Fact]
  public void The_tool_names_its_data_block_its_settings_and_its_repository_link()
  {
    var html = Page().Render();

    Assert.Contains("<script type=\"application/json\" id=\"pqdoc-data\">", html, StringComparison.Ordinal);
    Assert.Contains("localStorage.getItem(\"pqdoc-theme\")", html, StringComparison.Ordinal);
    Assert.Contains("Opens pqdoc on GitHub.", html, StringComparison.Ordinal);
    Assert.Contains("<meta name=\"generator\" content=\"pqdoc 1.0.0 (b7c0ebe)\">", html, StringComparison.Ordinal);
  }

  [Fact]
  public void The_first_tab_is_selected_and_the_rest_are_out_of_the_tab_order()
  {
    var html = Page().Render();

    Assert.Contains(
      "<button type=\"button\" role=\"tab\" id=\"tab-graph\" aria-controls=\"view-graph\" aria-selected=\"true\">Dependency graph</button>",
      html, StringComparison.Ordinal);
    Assert.Contains(
      "<button type=\"button\" role=\"tab\" id=\"tab-other\" aria-controls=\"view-other\" aria-selected=\"false\" tabindex=\"-1\">Other &lt;view&gt;</button>",
      html, StringComparison.Ordinal);
  }

  [Fact]
  public void Text_is_escaped_and_HTML_parts_are_not()
  {
    var html = Page(p => p with { Title = "R&D <draft>", PanelName = "Source & more" }).Render();

    Assert.Contains("<h1 class=\"title\" id=\"page-title\" title=\"R&amp;D &lt;draft&gt;\">R&amp;D &lt;draft&gt;</h1>", html, StringComparison.Ordinal);
    Assert.Contains("aria-label=\"Source &amp; more\" title=\"Show or hide the source &amp; more panel\"", html, StringComparison.Ordinal);
    Assert.Contains("aria-label=\"Hide the source &amp; more panel\"", html, StringComparison.Ordinal);
    Assert.Contains("<input id=\"search\" type=\"search\">", html, StringComparison.Ordinal);
    Assert.Contains("<section class=\"view\" id=\"view-other\" hidden></section>", html, StringComparison.Ordinal);
  }

  [Fact]
  public void The_data_cannot_end_its_script_element()
  {
    var html = Page().Render();

    Assert.Contains("{\"name\":\"\\u003C/script\\u003E\"}", html, StringComparison.Ordinal);
  }

  [Fact]
  public void Saved_views_go_in_a_block_of_their_own_which_they_cannot_end()
  {
    var html = Page(p => p with { ViewsJson = "{\"views\":[{\"name\":\"</script><b>\"}]}" }).Render();

    Assert.Contains("<script type=\"application/json\" id=\"pqdoc-views\">\n{\"views\":[{\"name\":\"\\u003C/script\\u003E\\u003Cb\\u003E\"}]}\n</script>", html, StringComparison.Ordinal);
  }

  [Fact]
  public void Without_saved_views_there_is_no_block_for_them()
  {
    Assert.DoesNotContain("id=\"pqdoc-views\"", Page().Render(), StringComparison.Ordinal);
  }

  [Fact]
  public void The_shells_stylesheet_comes_before_the_tools()
  {
    var html = Page().Render();

    var shell = html.IndexOf(".side-panel-header {", StringComparison.Ordinal);
    var tool = html.IndexOf(".tool-rule", StringComparison.Ordinal);
    Assert.True(shell >= 0 && tool > shell, "The tool's rules should come after the shell's, so they can override them.");
  }

  [Fact]
  public void The_shells_script_comes_after_d3_and_before_the_tools()
  {
    var html = Page().Render();

    var d3 = html.IndexOf("// https://d3js.org", StringComparison.Ordinal);
    var shell = html.IndexOf("window.CodeDocumenterShell = ", StringComparison.Ordinal);
    Assert.True(d3 >= 0 && shell > d3, "d3 then the shell");
  }

  [Theory]
  [InlineData("")]
  [InlineData("PQdoc")]
  [InlineData("pq-doc")]
  [InlineData("pq\"doc")]
  public void The_tool_name_must_be_plain(string tool)
  {
    Assert.Throws<ArgumentException>(() => Page(p => p with { Tool = tool }).Render());
  }

  [Fact]
  public void The_summary_says_when_the_workbook_was_saved_in_UTC()
  {
    var html = Page(p => p with { Summary = "3 things & more.", WorkbookSaved = new DateTimeOffset(2026, 11, 4, 11, 57, 36, TimeSpan.FromHours(10)) }).Render();

    Assert.Contains(
      "<p class=\"summary muted\" id=\"page-summary\">3 things &amp; more. Workbook saved <time id=\"workbook-saved\" " +
      "datetime=\"2026-11-04T01:57:36Z\" title=\"2026-11-04T01:57:36Z\">2026-11-04 01:57 UTC</time>.</p>",
      html,
      StringComparison.Ordinal);
  }

  [Fact]
  public void Without_a_summary_or_a_saved_time_there_is_no_summary()
  {
    Assert.DoesNotContain("page-summary", Page().Render(), StringComparison.Ordinal);
    Assert.Contains(">2 things.</p>", Page(p => p with { Summary = "2 things." }).Render(), StringComparison.Ordinal);
  }
}
