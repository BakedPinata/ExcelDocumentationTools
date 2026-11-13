using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Lock view (FR-HTML-21, step L1 of the generalisation plan): the shell's button at the start of the graph's toolbar
/// keeps what the graph shows where it is, so a click on a query or on empty space changes only the selection and the
/// details panel. Locking ticks what's shown in the explorer, and unlocking puts the ticks back.
/// </summary>
[Collection("Browser")]
public sealed class LockViewTests(PageFixture fixture)
{
  private const string Lock = "#view-graph [data-lock-view]";

  private static readonly string[] OrdersChains =
    ["fnCleanText", "Orders", "Q1.Summary", "Revenue & Margin (2024)", "Sales Region Map", "Sales Report", "Source"];

  private static Task<string[]> ShownIds(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static async Task ClickQueryAsync(OpenedPage opened, string id) =>
    await (await opened.NodeAsync(id)).Locator("circle, rect, path").First.ClickAsync();

  private static async Task<ILocator> TreeBoxAsync(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li.item:not(.favourites li):not(.widely-used-items li)[data-id='{escaped}'] > .tree-row > .tree-check");
  }

  /// <summary>Every query's place, whether it's shown, and the zoom: what locking keeps.</summary>
  private static Task<string> Picture(IPage page) => page.EvaluateAsync<string>("""
      () => [...document.querySelectorAll('#graph-svg g.node')]
        .map(n => n.dataset.id + n.getAttribute('transform') + n.classList.contains('out-of-focus')).sort().join(';') +
        ' | ' + document.querySelector('#graph-svg g.viewport').getAttribute('transform')
      """);

  private static Task WaitUntilStill(IPage page) =>
    page.WaitForFunctionAsync("() => !window.pqdocGraph.moving()", null, new() { Timeout = 30000 });

  /// <summary>The legend floats over a corner of the graph, and a locked graph doesn't move to clear it.</summary>
  private static Task CloseLegends(IPage page) =>
    page.EvaluateAsync("() => document.querySelectorAll('details.view-legend').forEach(d => { d.open = false; })");

  [Fact]
  public async Task FR_HTML_21_locked_a_click_changes_only_the_selection_and_the_details()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    await CloseLegends(page);
    await ClickQueryAsync(opened, "Orders");
    Assert.Equal(OrdersChains, await ShownIds(opened));
    await WaitUntilStill(page);
    await page.WaitForTimeoutAsync(400);   // the fit that follows a focus
    var before = await Picture(page);

    await page.ClickAsync(Lock);
    Assert.Equal("true", await page.GetAttributeAsync(Lock, "aria-pressed"));
    Assert.True(await page.IsDisabledAsync("#graph-focus"));
    Assert.Equal(before, await Picture(page));   // locking moved nothing

    await ClickQueryAsync(opened, "Source");
    Assert.Equal("Source", await opened.SelectionAsync());
    Assert.Contains("Source", await page.InnerTextAsync("#side-panel-header"));
    Assert.Equal(OrdersChains, await ShownIds(opened));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.selected").CountAsync());
    Assert.Equal(0, await page.Locator("#graph-svg g.node.dim, #graph-svg g.node.related, #graph-svg path.edge.dim, #graph-svg path.edge.highlighted").CountAsync());
    await page.WaitForTimeoutAsync(400);
    Assert.Equal(before, await Picture(page));

    // Empty space: the bottom right corner, away from the toolbar and the queries.
    var box = await page.Locator("#graph-svg").BoundingBoxAsync();
    await page.Mouse.ClickAsync(box!.X + box.Width - 6, box.Y + box.Height - 6);
    Assert.Null(await opened.SelectionAsync());
    await page.WaitForTimeoutAsync(400);
    Assert.Equal(before, await Picture(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_21_locking_ticks_what_is_shown_and_unlocking_puts_the_ticks_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await CloseLegends(page);
    var source = await TreeBoxAsync(opened, "Source");
    var orders = await TreeBoxAsync(opened, "Orders");
    await ClickQueryAsync(opened, "Orders");

    await page.ClickAsync(Lock);
    Assert.True(await source.IsCheckedAsync());
    Assert.True(await orders.IsCheckedAsync());
    Assert.StartsWith("Locked: 7 of ", await page.InnerTextAsync("#view-graph .view-filter-note span"));

    // A tick still drops a query or brings it back.
    await source.ClickAsync();
    Assert.DoesNotContain("Source", await ShownIds(opened));
    await source.ClickAsync();
    Assert.Equal(OrdersChains, await ShownIds(opened));

    // The note's way back is to unlock.
    Assert.Equal("Unlock", await page.InnerTextAsync("#view-graph .view-filter-note [data-filter-clear]"));
    await page.ClickAsync("#view-graph .view-filter-note [data-filter-clear]");
    Assert.Equal("false", await page.GetAttributeAsync(Lock, "aria-pressed"));
    Assert.False(await source.IsCheckedAsync());
    Assert.False(await orders.IsCheckedAsync());
    Assert.False(await page.IsDisabledAsync("#graph-focus"));
    Assert.True(await page.IsHiddenAsync("#view-graph .view-filter-note"));
    Assert.Equal(OrdersChains, await ShownIds(opened));   // focus on click works again, on the selection
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_21_the_lock_is_remembered_with_what_it_keeps()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await CloseLegends(page);
    await ClickQueryAsync(opened, "Orders");
    await page.ClickAsync(Lock);

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal("true", await page.GetAttributeAsync(Lock, "aria-pressed"));
    Assert.Equal(OrdersChains, await ShownIds(opened));
    Assert.Empty(opened.Errors);
  }
}
