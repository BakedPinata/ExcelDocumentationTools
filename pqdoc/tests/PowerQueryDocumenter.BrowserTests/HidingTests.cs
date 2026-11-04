using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Hiding and Focus on favourites (FR-HTML-16, step D3b of the feedback plan): a hidden query and its links leave the
/// graph, never silently, and come back from the Hidden group; Focus on favourites shows only them and what they use.
/// </summary>
[Collection("Browser")]
public sealed class HidingTests(PageFixture fixture)
{
  private static async Task<ILocator> Row(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li.item:not(.favourites li):not(.hidden-items li)[data-id='{escaped}'] > .tree-row");
  }

  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static Task<string[]> Hidden(IPage page) =>
    page.Locator("#explorer-tree > li.hidden-items > ul > li > .tree-row .tree-label").EvaluateAllAsync<string[]>("ls => ls.map(l => l.textContent)");

  [Fact]
  public async Task FR_HTML_16_hiding_a_query_takes_it_and_its_links_out_of_the_graph_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    var all = await Shown(opened);
    var row = await Row(opened, "Chain_B");

    await row.HoverAsync();
    await row.Locator(".hide-toggle").ClickAsync();

    Assert.Equal(all.Where(id => id != "Chain_B"), await Shown(opened));
    Assert.Equal(0, await page.Locator("#graph-svg path.edge:not(.out-of-focus)[data-from='Chain_B'], #graph-svg path.edge:not(.out-of-focus)[data-to='Chain_B']").CountAsync());
    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(["Chain_B"], await Hidden(page));
    Assert.Equal("1 hidden", await page.Locator("#view-graph .view-hidden-note button").TextContentAsync());

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.DoesNotContain("Chain_B", await Shown(opened));
    Assert.Equal(["Chain_B"], await Hidden(page));

    await page.ClickAsync("#view-graph .view-hidden-note button");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    Assert.Equal(all, await Shown(opened));
    Assert.Equal(0, await page.Locator("#explorer-tree > li.hidden-items").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_16_the_details_panel_says_when_the_query_is_hidden_or_uses_hidden_queries()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    await page.EvaluateAsync("() => window.pqdoc.toggleHidden('Chain_B')");

    await opened.SelectAsync("Chain_C");
    Assert.Contains("1 item it's linked to is hidden", await page.TextContentAsync("#side-panel-header .hidden-note"));
    Assert.Equal(1, await page.Locator("#details-uses li:has(a[data-select='Chain_B']) .hidden-tag").CountAsync());

    await opened.SelectAsync("Chain_B");
    Assert.Contains("Hidden from the views", await page.TextContentAsync("#side-panel-header .hidden-note"));
    await page.ClickAsync("#side-panel-header .hidden-note [data-hide]");
    Assert.Empty(await Hidden(page));
    Assert.Equal(0, await page.Locator("#side-panel-header .hidden-note").CountAsync());

    await page.ClickAsync("#side-panel-header .panel-hide");
    Assert.Equal(["Chain_B"], await Hidden(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_16_Focus_on_favourites_shows_only_them_and_what_they_use()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    var expected = (await page.EvaluateAsync<string[]>("() => [...window.pqdoc.upstream('Chain_B'), 'Chain_B']"))
      .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    await page.EvaluateAsync("() => window.pqdoc.toggleFavourite('Chain_B')");

    await page.ClickAsync("#explorer-tree > li.favourites [data-focus-favourites]");
    Assert.Equal(expected, await Shown(opened));
    Assert.True(await page.IsVisibleAsync("#view-graph .view-favourites-note"));

    // A hidden query stays out, favourite or not.
    await page.EvaluateAsync("() => window.pqdoc.toggleHidden('Chain_A')");
    Assert.Equal(expected.Where(id => id != "Chain_A"), await Shown(opened));

    await page.ClickAsync("#view-graph .view-favourites-note [data-focus-favourites-off]");
    Assert.True(await page.IsHiddenAsync("#view-graph .view-favourites-note"));
    Assert.Equal(await page.EvaluateAsync<int>("() => window.pqdoc.data.queries.length") - 1, (await Shown(opened)).Length);
    Assert.Empty(opened.Errors);
  }
}
