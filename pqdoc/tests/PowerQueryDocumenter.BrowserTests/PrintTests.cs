using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Printing (FR-HTML-25, step L5 of the generalisation plan), which is the shell's, as vbadoc's is: here, that the
/// graph and the queries it shows print, with each query's description, what it uses and its M code when asked, and
/// that every saved view prints from a new page.
/// </summary>
[Collection("Browser")]
public sealed partial class PrintTests(PageFixture fixture)
{
  [GeneratedRegex(@"/Type\s*/Page\b(?!s)")]
  private static partial Regex PageObject();

  private static int Pages(byte[] pdf) => PageObject().Count(Encoding.Latin1.GetString(pdf));

  private static Task Prepare(IPage page, string which, bool source = false) =>
    page.EvaluateAsync("([which, source]) => window.pqdoc.preparePrint(which, { source })", new object[] { which, source });

  private static string Item(string name) => $"#print-document .print-item:has(> h4:text-matches('^{Regex.Escape(name)}\\\\s'))";

  [Fact]
  public async Task FR_HTML_25_this_view_prints_the_graph_and_each_query_it_shows()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;

    await Prepare(page, "current", source: true);

    Assert.Equal("sales-model", await page.TextContentAsync("#print-document .print-header h1"));
    Assert.Equal(["The page as it is now"], await page.Locator("#print-document .print-view > h2").AllTextContentsAsync());
    Assert.Equal(11, await page.Locator("#print-document .print-diagram svg.print-svg g.node").CountAsync());
    Assert.Equal(11, await page.Locator("#print-document .print-item").CountAsync());
    Assert.Equal("Query", await page.TextContentAsync(Item("Orders") + " .print-kind"));
    Assert.Equal("Intermediate query.", await page.TextContentAsync(Item("Orders") + " .print-note"));
    Assert.Equal("Uses: fnCleanText, Sales Region Map, Source", await page.TextContentAsync(Item("Orders") + " .print-links >> nth=0"));
    Assert.Equal("Function", await page.TextContentAsync(Item("fnCleanText") + " .print-kind"));
    Assert.Contains("common step name", await page.TextContentAsync(Item("Source") + " .print-description")); // from its comments
    Assert.Contains("Table.NestedJoin", await page.TextContentAsync(Item("Orders") + " .print-code"));

    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
    var pdf = await page.PdfAsync(new() { Format = "A4" });
    Assert.True(Pages(pdf) >= 1);

    await page.EvaluateAsync("() => window.pqdoc.finishPrint()");
    Assert.Equal("dark", await page.GetAttributeAsync("html", "data-theme"));
    Assert.Equal(string.Empty, await page.InnerHTMLAsync("#print-document"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_25_every_saved_view_prints_from_a_new_page_and_the_page_goes_back_as_it_was()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltInPage);
    var page = opened.Page;
    // A second view, of the person's own.
    await page.ClickAsync("#views > summary");
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", "Everything");
    await page.ClickAsync("#views-form-save");
    await page.Keyboard.PressAsync("Escape");

    await Prepare(page, "all");

    Assert.Equal(["Orders and its source", "Everything"], await page.Locator("#print-document .print-view > h2").AllTextContentsAsync());
    var orders = "#print-document .print-view:has(> h2:text-is('Orders and its source'))";
    Assert.Equal("Where orders come from", await page.TextContentAsync(orders + " > .print-description"));
    Assert.Equal(["Orders Query", "Source Query"], await page.Locator(orders + " .print-item > h4").AllTextContentsAsync());
    Assert.Equal(2, await page.Locator(orders + " .print-diagram g.node").EvaluateAllAsync<int>("ns => ns.filter(n => n.style.display !== 'none').length"));
    var pdf = await page.PdfAsync(new() { Format = "A4" });
    Assert.True(Pages(pdf) >= 2, $"a page for each view, but the PDF has {Pages(pdf)}");

    await page.EvaluateAsync("() => window.pqdoc.finishPrint()");
    Assert.False(await page.EvaluateAsync<bool>("() => window.pqdoc.filterActive()"));
    Assert.Equal(0, await page.Locator("#graph-svg g.node.out-of-focus").CountAsync());
    Assert.Empty(opened.Errors);
  }
}
