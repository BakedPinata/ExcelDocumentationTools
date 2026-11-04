using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Favourites (step D2 of the feedback plan, in place of Home and the trail), Path from the nearest favourite, and
/// Back/Forward, on SampleVba (see SampleVbaDependencyTests for its edges).
/// </summary>
[Collection("Browser")]
public class NavigationTests(PageFixture fixture)
{
  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static Task<string[]> DrawnIds(IPage page, string selector) =>
      page.Locator(selector).EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)");

  static Task<string[]> Favourites(IPage page) => page.EvaluateAsync<string[]>("() => window.vbadoc.favourites()");

  static Task<string[]> FavouritesListed(IPage page) =>
      page.Locator("#explorer-tree > li.favourites > ul > li > .tree-row .tree-label").EvaluateAllAsync<string[]>("ls => ls.map(l => l.textContent)");

  static Task Favourite(OpenedPage opened, string id) => opened.Page.EvaluateAsync("id => window.vbadoc.toggleFavourite(id)", id);

  static string Row(string id) => $"#explorer-tree li.item:not(.favourites li, .widely-used-items li)[data-id='{id}'] > .tree-row";

  [Fact]
  public async Task The_star_in_the_source_panel_makes_a_favourite_which_is_remembered_listed_and_starred_in_the_graph()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-header .favourite-toggle", "aria-pressed"));
    Assert.Equal(0, await page.Locator("#explorer-tree > li.favourites").CountAsync());

    await page.ClickAsync("#side-panel-header .favourite-toggle");

    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-header .favourite-toggle", "aria-pressed"));
    Assert.Equal(["Order.Describe"], await FavouritesListed(page));
    Assert.Equal("true", await page.GetAttributeAsync(Row("Order.Describe") + " .favourite-toggle", "aria-pressed"));
    Assert.Equal(["Order"], await DrawnIds(page, "#graph-svg g.node.favourite")); // a member's module at module level

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    Assert.Equal(["Order.Describe"], await Favourites(page));
    Assert.Equal(["Order.Describe"], await FavouritesListed(page));
    await page.ClickAsync("[data-level='members']");
    Assert.Equal(["Order.Describe"], await DrawnIds(page, "#graph-svg g.node.favourite"));

    // The star again takes it off.
    await opened.SelectAsync("Order.Describe");
    await page.ClickAsync("#side-panel-header .favourite-toggle");
    Assert.Empty(await Favourites(page));
    Assert.Equal(0, await page.Locator("#explorer-tree > li.favourites").CountAsync());
    Assert.Equal(0, await page.Locator("#graph-svg g.node.favourite").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_star_on_an_explorer_row_adds_a_favourite_without_selecting_it_and_favourites_keep_their_order()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.HoverAsync(Row("Utils"));
    await page.ClickAsync(Row("Utils") + " .favourite-toggle");
    await page.HoverAsync(Row("Logger"));
    await page.ClickAsync(Row("Logger") + " .favourite-toggle");

    Assert.Null(await Selection(page));
    Assert.Equal(["Utils", "Logger"], await FavouritesListed(page));
    Assert.Equal("2", await page.TextContentAsync("#explorer-tree > li.favourites > .tree-row .tree-detail"));

    // Choosing one in Favourites selects it, as in the layout, and all its rows are marked (Logger is widely used too).
    await page.ClickAsync("#explorer-tree > li.favourites li[data-id='Logger'] > .tree-row .tree-label");
    Assert.Equal("Logger", await Selection(page));
    Assert.Equal(3, await page.Locator("#explorer-tree li[data-id='Logger'][aria-selected='true']").CountAsync());

    // Its star in Favourites takes it off there and in the layout.
    await page.ClickAsync("#explorer-tree > li.favourites li[data-id='Utils'] .favourite-toggle");
    Assert.Equal(["Logger"], await FavouritesListed(page));
    Assert.Equal("false", await page.GetAttributeAsync(Row("Utils") + " .favourite-toggle", "aria-pressed"));
    Assert.Equal("Logger", await Selection(page));
  }

  [Fact]
  public async Task Favourites_are_listed_in_every_layout_and_a_favourite_no_longer_in_the_workbook_is_ignored()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded,
        initScript: "localStorage.setItem('vbadoc-favourites|Foldered.xlsm', JSON.stringify(['Gone.Away', 'Order']));");
    var page = opened.Page;

    Assert.Equal(["Order"], await Favourites(page));
    Assert.Equal(["Order"], await FavouritesListed(page));
    await page.ClickAsync("#explorer-layouts [data-layout='types']");
    Assert.Equal(["Order"], await FavouritesListed(page));
    Assert.Equal("Favourites", await page.TextContentAsync("#explorer-tree > li:first-child > .tree-row .tree-label"));
  }

  [Fact]
  public async Task A_favourite_module_at_member_level_stars_its_outline()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await Favourite(opened, "Utils");
    await page.ClickAsync("[data-level='members']");

    Assert.Equal("★ Utils", await page.TextContentAsync("#graph-svg g.hull-group.favourite text.hull-label"));
    Assert.Equal(0, await page.Locator("#graph-svg g.node.favourite:not(.module-node)").CountAsync());
  }

  [Fact]
  public async Task Path_from_the_nearest_favourite_highlights_the_shortest_chain_in_either_direction()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    Assert.True(await page.IsDisabledAsync("#graph-path")); // no favourites yet
    await Favourite(opened, "Main");
    await opened.SelectAsync("Main");
    Assert.True(await page.IsDisabledAsync("#graph-path")); // the only favourite is the selection

    // Main uses Order, which uses Utils.
    await opened.SelectAsync("Utils");
    await page.ClickAsync("#graph-path");
    Assert.Equal("true", await page.GetAttributeAsync("#graph-path", "aria-pressed"));
    Assert.Equal(["Main", "Order", "Utils"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());
    Assert.Equal(2, await page.Locator("#graph-svg path.edge.highlighted").CountAsync());
    Assert.Equal(1, await page.Locator("#graph-svg path.edge.highlighted[data-from='Main'][data-to='Order']").CountAsync());
    Assert.Equal("The favourite Main uses the selection through 1 other node.", await page.InnerTextAsync("#graph-path-note"));

    // The path follows the selection, and takes the shorter direction: Sheet1 uses Main directly, while
    // Main reaches Sheet1 only through another module.
    await opened.SelectAsync("Sheet1");
    Assert.Equal("The selection uses the favourite Main directly.", await page.InnerTextAsync("#graph-path-note"));
    Assert.Equal(["Main", "Sheet1"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());

    // With another favourite nearer, the path comes from that one.
    await Favourite(opened, "Order");
    await opened.SelectAsync("Utils");
    Assert.Equal("The favourite Order uses the selection directly.", await page.InnerTextAsync("#graph-path-note"));

    // Clicking the background turns it off along with the selection.
    await page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
    Assert.Equal("false", await page.GetAttributeAsync("#graph-path", "aria-pressed"));
    Assert.True(await page.IsHiddenAsync("#graph-path-note"));
    Assert.Equal(0, await page.Locator("#graph-svg .dim").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Path_from_the_nearest_favourite_says_when_nothing_links_them_and_works_at_member_level()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await Favourite(opened, "Main");
    await opened.SelectAsync("Utils");
    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='line:uses']");
    await page.Keyboard.PressAsync("Escape");

    await page.ClickAsync("#graph-path");
    Assert.Equal("No chain of dependencies links a favourite and the selection with the current Show… choices.",
        await page.InnerTextAsync("#graph-path-note"));
    Assert.Equal(["Main", "Utils"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());

    await page.ClickAsync("#explorer-show summary");
    await page.ClickAsync("#explorer-show [data-show-reset]");
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
    await opened.SelectAsync("Utils.FormatMoney");
    if (await page.GetAttributeAsync("#graph-path", "aria-pressed") != "true") await page.ClickAsync("#graph-path");

    // At member level the chain runs from one of Main's members to FormatMoney, through Order.Describe.
    var lit = await DrawnIds(page, "#graph-svg g.node:not(.dim)");
    Assert.Contains("Utils.FormatMoney", lit);
    Assert.Contains("Order.Describe", lit);
    Assert.StartsWith("The favourite Main.", await page.InnerTextAsync("#graph-path-note"));
  }

  [Fact]
  public async Task Back_and_Forward_step_through_selections()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='Order']");
    await opened.SelectAsync("Order.Status[Let]");
    Assert.EndsWith("#Order.Status%5BLet%5D", page.Url);

    await page.GoBackAsync();
    Assert.Equal("Order", await Selection(page));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='Order']").CountAsync());

    await page.GoBackAsync();
    Assert.Null(await Selection(page));

    await page.GoForwardAsync();
    await page.GoForwardAsync();
    Assert.Equal("Order.Status[Let]", await Selection(page));
  }

  [Fact]
  public async Task An_address_with_a_fragment_opens_with_that_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.GotoAsync(PageFixture.PageUrl(fixture.SampleEmbedded) + "#Order.Describe");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");

    Assert.Equal("Order.Describe", await Selection(page));
    Assert.Contains("Describe", await page.InnerTextAsync("#side-panel-header h2"));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='Order']").CountAsync());

    // An unknown name is ignored.
    await page.GotoAsync(PageFixture.PageUrl(fixture.SampleEmbedded) + "#NoSuchThing");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    Assert.Null(await Selection(page));
    Assert.Empty(opened.Errors);
  }
}
