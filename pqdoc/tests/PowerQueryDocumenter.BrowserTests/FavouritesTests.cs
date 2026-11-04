using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Favourites (FR-HTML-14, step D2 of the feedback plan): starred from the details panel or the explorer, listed at the
/// top of the explorer, starred in the graph, and remembered per workbook.
/// </summary>
[Collection("Browser")]
public sealed class FavouritesTests(PageFixture fixture)
{
  private static Task<string[]> Listed(IPage page) =>
    page.Locator("#explorer-tree > li.favourites > ul > li > .tree-row .tree-label").EvaluateAllAsync<string[]>("ls => ls.map(l => l.textContent)");

  private static async Task<ILocator> Row(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li.item:not(.favourites li)[data-id='{escaped}'] > .tree-row");
  }

  [Fact]
  public async Task FR_HTML_14_the_star_in_the_details_panel_makes_a_favourite_listed_starred_in_the_graph_and_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Sales Data");

    await page.ClickAsync("#side-panel-header .favourite-toggle");

    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-header .favourite-toggle", "aria-pressed"));
    Assert.Equal(["Sales Data"], await Listed(page));
    Assert.Equal(["Sales Data"], await opened.DrawnIdsAsync("#graph-svg g.node.favourite"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal(["Sales Data"], await Listed(page));
    Assert.Equal(["Sales Data"], await opened.DrawnIdsAsync("#graph-svg g.node.favourite"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_14_the_star_on_an_explorer_row_adds_a_favourite_without_selecting_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    var row = await Row(opened, "Chain_C");

    await row.HoverAsync();
    await row.Locator(".favourite-toggle").ClickAsync();

    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(["Chain_C"], await Listed(page));
    Assert.Equal("Favourites", await page.TextContentAsync("#explorer-tree > li:first-child > .tree-row .tree-label"));

    await page.ClickAsync("#explorer-tree > li.favourites li[data-id='Chain_C'] .favourite-toggle");
    Assert.Equal(0, await page.Locator("#explorer-tree > li.favourites").CountAsync());
    Assert.Equal(0, await page.Locator("#graph-svg g.node.favourite").CountAsync());
  }
}
