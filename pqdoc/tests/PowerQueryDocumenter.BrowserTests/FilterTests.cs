using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The explorer's filter and Expand / Collapse all (FR-HTML-15, step D3a of the feedback plan): ticking queries and
/// groups shows only those in the graph, apart from selecting, said in the explorer and the toolbar with Clear, and
/// remembered per workbook.
/// </summary>
[Collection("Browser")]
public sealed class FilterTests(PageFixture fixture)
{
  private static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  private static ILocator GroupBox(IPage page, string label) => page.Locator(Group(label) + " > .tree-row > .tree-check");

  private static async Task<ILocator> ItemBox(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li.item:not(.favourites li)[data-id='{escaped}'] > .tree-row > .tree-check");
  }

  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static Task<int> QueryCount(IPage page) => page.EvaluateAsync<int>("() => window.pqdoc.data.queries.length");

  [Fact]
  public async Task FR_HTML_15_ticking_a_group_shows_only_its_queries_and_ticking_a_query_adds_it_without_selecting()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    var total = await QueryCount(page);

    await GroupBox(page, "Staging").ClickAsync();

    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], await Shown(opened));
    Assert.Equal($"Filtered: 4 of {total} queries", await page.TextContentAsync("#explorer-filter-text"));
    Assert.True(await page.IsVisibleAsync("#view-graph .view-filter-note"));
    Assert.Equal($"Filtered: 4 of {total} queries", await page.Locator("#view-graph .view-filter-note > span").TextContentAsync());
    Assert.True(await (await ItemBox(opened, "Chain_B")).IsCheckedAsync());
    Assert.Null(await opened.SelectionAsync());

    await (await ItemBox(opened, "Sales Data")).ClickAsync();

    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D", "Sales Data"], await Shown(opened));
    Assert.Equal($"Filtered: 5 of {total} queries", await page.TextContentAsync("#explorer-filter-text"));
    // A group partly ticked shows it.
    Assert.True(await GroupBox(page, "Root").EvaluateAsync<bool>("b => b.indeterminate"));
    Assert.Null(await opened.SelectionAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_15_selecting_doesnt_filter_and_unticking_or_Clear_takes_queries_out()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    var total = await QueryCount(page);

    await page.ClickAsync("#explorer-tree li[data-id='Chain_C'] > .tree-row .tree-label");
    Assert.Equal("Chain_C", await opened.SelectionAsync());
    Assert.True(await page.IsHiddenAsync("#explorer-filter-bar"));
    Assert.Equal(total, (await Shown(opened)).Length);

    await GroupBox(page, "Staging").ClickAsync();
    await (await ItemBox(opened, "Chain_B")).ClickAsync();
    Assert.Equal(["Chain_A", "Chain_C", "Chain_D"], await Shown(opened));
    Assert.True(await GroupBox(page, "Staging").EvaluateAsync<bool>("b => b.indeterminate"));

    // Esc clears the selection, not the filter.
    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(["Chain_A", "Chain_C", "Chain_D"], await Shown(opened));

    await page.Locator("#view-graph .view-filter-note [data-filter-clear]").ClickAsync();
    Assert.Equal(total, (await Shown(opened)).Length);
    Assert.True(await page.IsHiddenAsync("#explorer-filter-bar"));
    Assert.True(await page.IsHiddenAsync("#view-graph .view-filter-note"));
    Assert.False(await GroupBox(page, "Staging").IsCheckedAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_15_the_filter_is_remembered_and_Space_ticks_the_row_with_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    await page.FocusAsync("#explorer-filter");
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("Staging", await page.EvaluateAsync<string>("() => document.activeElement.closest('li').querySelector('.tree-label').textContent"));
    await page.Keyboard.PressAsync("Space");

    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], await Shown(opened));
    Assert.Null(await opened.SelectionAsync());

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], await Shown(opened));
    Assert.True(await page.IsVisibleAsync("#explorer-filter-bar"));
    Assert.True(await GroupBox(page, "Staging").IsCheckedAsync());

    // Focused on a query, the graph shows its chains of what's ticked.
    await opened.SelectAsync("Chain_D");
    Assert.Contains("Chain_D", await Shown(opened));
    Assert.DoesNotContain("Sales Data", await Shown(opened));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Expand_all_opens_every_group_and_Collapse_all_closes_them()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    await page.ClickAsync("#explorer-collapse-all");
    Assert.Equal(0, await page.Locator("#explorer-tree li[aria-expanded='true']").CountAsync());
    Assert.False(await page.IsVisibleAsync("#explorer-tree li[data-id='Chain_A']"));

    await page.ClickAsync("#explorer-expand-all");
    Assert.Equal(0, await page.Locator("#explorer-tree li.group[aria-expanded='false']").CountAsync());
    Assert.True(await page.IsVisibleAsync("#explorer-tree li[data-id='Sales Data']"));
  }
}
