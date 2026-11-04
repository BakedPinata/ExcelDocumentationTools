using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Hiding and Focus on favourites (step D3b of the feedback plan): a hidden item and its edges leave every view, never
/// silently, and come back from the Hidden group; Focus on favourites shows only them and what they use.
/// </summary>
[Collection("Browser")]
public class HidingTests(PageFixture fixture)
{
  static string Row(string id) => $"#explorer-tree li.item:not(.favourites li):not(.hidden-items li)[data-id='{id}'] > .tree-row";

  static Task<string[]> GraphNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<int> EdgesTouching(IPage page, string id) =>
      page.Locator($"#graph-svg path.edge[data-from='{id}'], #graph-svg path.edge[data-to='{id}']").CountAsync();

  static Task<string[]> Hidden(IPage page) =>
      page.Locator("#explorer-tree > li.hidden-items > ul > li > .tree-row .tree-label").EvaluateAllAsync<string[]>("ls => ls.map(l => l.textContent)");

  static async Task HideFromRow(IPage page, string id)
  {
    await page.HoverAsync(Row(id));
    await page.ClickAsync(Row(id) + " .hide-toggle");
  }

  [Fact]
  public async Task Hiding_a_module_takes_it_and_its_edges_out_of_every_view_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.True(await EdgesTouching(page, "Logger") > 0);

    await HideFromRow(page, "Logger");

    Assert.DoesNotContain("Logger", await GraphNodes(page));
    Assert.Equal(0, await EdgesTouching(page, "Logger"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()")); // hiding doesn't select
    Assert.Equal(["Logger"], await Hidden(page));
    Assert.Contains("hidden-from-views", await page.GetAttributeAsync("#explorer-tree li.item:not(.hidden-items li)[data-id='Logger']", "class"));
    Assert.Equal("1 hidden", await page.Locator("#view-graph .view-hidden-note button").TextContentAsync());

    // Its members go with it at member level, and its box from the class diagram.
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
    Assert.DoesNotContain(await GraphNodes(page), id => id.StartsWith("Logger.", StringComparison.Ordinal));
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal(0, await page.Locator("#classes-svg g.class-box[data-module='Logger']").CountAsync());
    Assert.True(await page.IsVisibleAsync("#view-classes .view-hidden-note"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => window.vbadocClasses && window.vbadocClasses.drawn()");
    Assert.Equal(0, await page.Locator("#classes-svg g.class-box[data-module='Logger']").CountAsync());
    Assert.Equal(["Logger"], await Hidden(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_hidden_item_comes_back_from_the_Hidden_group_one_at_a_time_or_all_at_once()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var all = await GraphNodes(page);
    await HideFromRow(page, "Logger");
    await HideFromRow(page, "Utils");
    Assert.Equal(["Logger", "Utils"], await Hidden(page));
    Assert.Equal(all.Length - 2, (await GraphNodes(page)).Length);

    // The toolbar's count opens the Hidden group.
    await page.ClickAsync("#view-graph .view-hidden-note button");
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-tree > li.hidden-items", "aria-expanded"));

    await page.ClickAsync("#explorer-tree > li.hidden-items li[data-id='Logger'] .hide-toggle");
    Assert.Equal(["Utils"], await Hidden(page));
    Assert.Contains("Logger", await GraphNodes(page));

    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    Assert.Equal(0, await page.Locator("#explorer-tree > li.hidden-items").CountAsync());
    Assert.Equal(all, await GraphNodes(page));
    Assert.True(await page.IsHiddenAsync("#view-graph .view-hidden-note"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_hidden_member_leaves_its_module_and_the_class_diagram_box_shows_the_rest()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.EvaluateAsync("() => window.vbadoc.toggleHidden('Order.Describe')");

    Assert.Contains("Order", await GraphNodes(page)); // at module level, the module stays
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
    var members = await GraphNodes(page);
    Assert.DoesNotContain("Order.Describe", members);
    Assert.Contains(members, id => id.StartsWith("Order.", StringComparison.Ordinal));

    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    var box = await page.Locator("#classes-svg g.class-box[data-module='Order']").TextContentAsync();
    Assert.DoesNotContain("Describe", box);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_source_panel_says_when_the_selection_is_hidden_or_linked_to_hidden_items()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.EvaluateAsync("() => window.vbadoc.toggleHidden('Logger')");

    await opened.SelectAsync("Main");
    Assert.Contains("1 item it's linked to is hidden", await page.TextContentAsync("#side-panel-header .hidden-note"));
    await page.ClickAsync("#source-dependencies summary");
    Assert.Equal(1, await page.Locator("#side-panel-header .dependency-list li:has(a[data-select='Logger']) .hidden-tag").CountAsync());

    // Selected through search or the explorer, a hidden item says so, and Show it brings it back.
    await opened.SelectAsync("Logger");
    Assert.Contains("Hidden from the views", await page.TextContentAsync("#side-panel-header .hidden-note"));
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-header .panel-hide", "aria-pressed"));
    await page.ClickAsync("#side-panel-header .hidden-note [data-hide]");
    Assert.Empty(await Hidden(page));
    Assert.Contains("Logger", await GraphNodes(page));
    Assert.Equal(0, await page.Locator("#side-panel-header .hidden-note").CountAsync());

    // The panel's own button hides it again.
    await page.ClickAsync("#side-panel-header .panel-hide");
    Assert.Equal(["Logger"], await Hidden(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Focus_on_favourites_shows_only_them_and_what_they_use_in_every_view_until_Show_all()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var expected = (await page.Locator("#graph-svg path.edge[data-from='Main']").EvaluateAllAsync<string[]>("es => es.map(e => e.dataset.to)"))
        .Append("Main").Distinct().Order(StringComparer.Ordinal).ToArray();
    await page.EvaluateAsync("() => window.vbadoc.toggleFavourite('Main')");

    await page.ClickAsync("#explorer-tree > li.favourites [data-focus-favourites]");

    Assert.Equal(expected, await GraphNodes(page));
    Assert.True(await page.IsVisibleAsync("#view-graph .view-favourites-note"));
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Contains("Main", await page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module)"));
    Assert.True(await page.Locator("#classes-svg g.class-box").CountAsync() < 12);

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => window.vbadocClasses && window.vbadocClasses.drawn()");
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-tree > li.favourites [data-focus-favourites]", "aria-pressed"));
    await page.ClickAsync("#view-classes .view-favourites-note [data-focus-favourites-off]");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal(12, await page.Locator("#classes-svg g.class-box").CountAsync());
    Assert.Equal("false", await page.GetAttributeAsync("#explorer-tree > li.favourites [data-focus-favourites]", "aria-pressed"));
    Assert.Empty(opened.Errors);
  }
}
