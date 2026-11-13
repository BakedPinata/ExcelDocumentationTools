using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Printing (step L5 of the generalisation plan): the Print menu makes a document of the view showing, or of every
/// saved view from a new page each, with each view's diagram and its items' kind, signature, description and Uses /
/// Used by, and their source if asked. The browser's Print, then Save as PDF, makes the PDF; here headless Chromium
/// prints it straight to one.
/// </summary>
[Collection("Browser")]
public sealed partial class PrintTests(PageFixture fixture)
{
  [GeneratedRegex(@"/Type\s*/Page\b(?!s)")]
  private static partial Regex PageObject();

  /// <summary>How many pages a PDF has: its page objects, which Chromium writes uncompressed.</summary>
  static int Pages(byte[] pdf) => PageObject().Count(Encoding.Latin1.GetString(pdf));

  static Task Prepare(IPage page, string which, bool source = false) =>
      page.EvaluateAsync("([which, source]) => window.vbadoc.preparePrint(which, { source })", new object[] { which, source });

  static Task<string[]> Sections(IPage page) =>
      page.Locator("#print-document .print-view > h2").EvaluateAllAsync<string[]>("hs => hs.map(h => h.textContent)");

  static string Item(string name) => $"#print-document .print-item:has(> h4:text-matches('^{Regex.Escape(name)}\\\\s'))";

  /// <summary>What print shows: only the document.</summary>
  static async Task<bool> OnlyTheDocumentPrints(IPage page)
  {
    await page.EmulateMediaAsync(new() { Media = Media.Print });
    var only = await page.EvaluateAsync<bool>(@"() => [...document.body.children].every(el =>
        (getComputedStyle(el).display !== 'none') === el.classList.contains('print-document'))");
    await page.EmulateMediaAsync(new() { Media = Media.Null }); // back to the browser's own, so a PDF is printed as print
    return only;
  }

  [Fact]
  public async Task Every_saved_view_prints_from_a_new_page_with_its_diagram_and_items_and_the_page_goes_back_as_it_was()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;
    await opened.SelectAsync("Utils");
    var shown = await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

    await Prepare(page, "all");

    Assert.Equal(["Orders", "Old logging"], await Sections(page));
    Assert.Equal("SampleVba.xlsm", await page.TextContentAsync("#print-document .print-header h1"));
    Assert.Contains("12 modules", await page.TextContentAsync("#print-document .print-header"));
    Assert.StartsWith("vbadoc ", await page.TextContentAsync("#print-document .print-stamp"));
    var orders = "#print-document .print-view:has(> h2:text-is('Orders'))";
    Assert.Equal("The classes that handle an order", await page.TextContentAsync(orders + " > .print-description"));
    Assert.Equal("Class diagram · filtered: 2 of 12 modules", await page.TextContentAsync(orders + " > .print-what"));
    // Its diagram is a copy that needs nothing of the page's: no ids of the page's, its own viewBox, the boxes it shows.
    Assert.Equal(2, await page.Locator(orders + " .print-diagram svg.print-svg g.class-box").CountAsync());
    Assert.NotNull(await page.GetAttributeAsync(orders + " .print-diagram svg.print-svg", "viewBox"));
    Assert.Equal(1, await page.Locator("#classes-svg").CountAsync());
    Assert.Equal(["Order Class", "OrderService Class"], await page.Locator(orders + " .print-item > h4").EvaluateAllAsync<string[]>("hs => hs.map(h => h.textContent)"));
    Assert.Equal("Uses: Utils", await page.TextContentAsync(Item("Order") + " .print-links >> nth=0"));
    Assert.Contains("Used by: CsvOrderRepository, IOrderRepository, Main", await page.TextContentAsync(Item("Order") + " .print-links >> nth=1"));
    Assert.Equal(0, await page.Locator("#print-document .print-code").CountAsync()); // source only when asked for

    // The document is in the light theme whatever the page was in, and print shows nothing else.
    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
    Assert.True(await OnlyTheDocumentPrints(page));
    var pdf = await page.PdfAsync(new() { Format = "A4" });
    // Both views are short enough to share a page, so two pages means the second started a new one.
    Assert.Equal(2, Pages(pdf));

    // Afterwards the page is as it was: its selection, what it shows, its tab and its theme.
    await page.EvaluateAsync("() => window.vbadoc.finishPrint()");
    Assert.Equal("dark", await page.GetAttributeAsync("html", "data-theme"));
    Assert.Equal("", await page.InnerHTMLAsync("#print-document"));
    Assert.Equal("true", await page.GetAttributeAsync("#tab-graph", "aria-selected"));
    Assert.Equal("Utils", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.False(await page.EvaluateAsync<bool>("() => window.vbadoc.filterActive()"));
    await page.WaitForFunctionAsync("() => window.vbadocGraph.nodeCount() > 0");
    Assert.Equal(shown, await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task This_view_prints_what_the_page_shows_with_descriptions_and_the_source_when_asked()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ConditionsEmbedded, colourScheme: ColorScheme.Light);
    var page = opened.Page;
    await page.ClickAsync("#explorer-tree li.item:not(.favourites li):not(.widely-used-items li)[data-id='Described'] > .tree-row > .tree-check");
    await page.ClickAsync("#view-graph [data-level='members']");

    await Prepare(page, "current", source: true);

    Assert.Equal(["The page as it is now"], await Sections(page));
    Assert.Equal("Dependency graph · filtered: 1 of 14 modules", await page.TextContentAsync("#print-document .print-what"));
    Assert.True(await page.Locator("#print-document .print-diagram svg.print-svg g.node").CountAsync() >= 2);
    var withTax = Item("Described.WithTax");
    Assert.Equal("Function", await page.TextContentAsync(withTax + " .print-kind"));
    Assert.Contains("Public Function WithTax(net As Double) As Double", await page.TextContentAsync(withTax + " .print-signature"));
    Assert.Equal("The price with tax, to two places.", await page.TextContentAsync(withTax + " .print-description"));
    // Its own lines, numbered as the file has them, and highlighted.
    Assert.Equal(["7", "8", "9", "10", "11"], await page.Locator(withTax + " .print-code .ln").AllTextContentsAsync());
    Assert.True(await page.Locator(withTax + " .print-code .tok-keyword").CountAsync() > 0);

    var pdf = await page.PdfAsync(new() { Format = "A4" });
    Assert.True(Pages(pdf) >= 1);
    Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(pdf, 0, 5));
    await page.EvaluateAsync("() => window.vbadoc.finishPrint()");
    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_Calls_tab_prints_what_is_open_in_it_and_the_selection_when_nothing_else_says_what_to_list()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#tab-calls");
    await page.ClickAsync("#calls-tree li.group > ul > li[data-id='Main.ProcessOrders'] > .tree-row .tree-twisty");
    await page.ClickAsync("#calls-tree li[data-id='OrderService.Init'] .calls-name");

    await Prepare(page, "current");

    Assert.Contains("OrderService.Init", await page.TextContentAsync("#print-document .print-diagram"));
    Assert.Equal(0, await page.Locator("#print-document [id='calls-tree']").CountAsync()); // a copy, without the page's ids
    Assert.Equal(["OrderService.Init Sub"], await page.Locator("#print-document .print-item > h4").EvaluateAllAsync<string[]>("hs => hs.map(h => h.textContent)"));

    await page.EvaluateAsync("() => window.vbadoc.finishPrint()");
    await page.ClickAsync("[data-calls-show='sequence']");
    await Prepare(page, "current");
    Assert.True(await page.Locator("#print-document .print-diagram g.seq-message").CountAsync() > 5);
    // The arrowheads are the copy's own, so they print with the page's hidden.
    Assert.True(await page.EvaluateAsync<bool>(@"() => [...document.querySelectorAll('#print-document .print-diagram g.seq-message line')].every(l => {
      const id = (l.getAttribute('marker-end') || '').match(/#([^"")]+)/);
      return id && document.getElementById(id[1]) && document.getElementById(id[1]).closest('#print-document');
    })"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_Print_menu_offers_this_view_and_every_saved_view_once_there_are_some_and_remembers_the_source_choice()
  {
    await using (var none = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded))
    {
      await none.Page.ClickAsync("#print > summary");
      Assert.False(await none.Page.IsDisabledAsync("#print-current"));
      Assert.True(await none.Page.IsDisabledAsync("#print-all"));
      Assert.Contains("no saved views", await none.Page.GetAttributeAsync("#print-all", "title"));
    }

    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;
    await page.ClickAsync("#print > summary");
    Assert.Equal("Every saved view (2)", await page.InnerTextAsync("#print-all"));
    Assert.False(await page.IsDisabledAsync("#print-all"));

    await page.CheckAsync("#print-source");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc");
    Assert.True(await page.IsCheckedAsync("#print-source"));

    // The button makes the document and hands it to the browser's Print.
    await page.EvaluateAsync("() => { window.printed = 0; window.print = () => { window.printed++; window.dispatchEvent(new Event('afterprint')); }; }");
    await page.ClickAsync("#print > summary");
    await page.ClickAsync("#print-current");
    await page.WaitForFunctionAsync("() => window.printed === 1");
    Assert.Equal("", await page.InnerHTMLAsync("#print-document"));   // put back once printing is over
    Assert.Equal("dark", await page.GetAttributeAsync("html", "data-theme"));
    Assert.Empty(opened.Errors);
  }
}
