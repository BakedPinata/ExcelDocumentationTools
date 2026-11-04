using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The explorer's filter and Expand / Collapse all (step D3a of the feedback plan): ticking folders, modules and members
/// shows only those in the graph and the class diagram, apart from selecting, said in the explorer and every view's
/// toolbar with Clear, and remembered per workbook.
/// </summary>
[Collection("Browser")]
public class FilterTests(PageFixture fixture)
{
  static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  static string Box(string id) => $"#explorer-tree li.item:not(.favourites li)[data-id='{id}'] > .tree-row > .tree-check";

  static string GroupBox(string label) => Group(label) + " > .tree-row > .tree-check";

  static Task<string[]> GraphNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<string[]> ClassBoxes(IPage page) =>
      page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");

  static Task<int> ModuleCount(IPage page) => page.EvaluateAsync<int>("() => window.vbadoc.data.modules.length");

  static async Task ShowClasses(IPage page, int boxes)
  {
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("n => document.querySelectorAll('#classes-svg g.class-box').length === n", boxes);
  }

  [Fact]
  public async Task Ticking_a_folder_shows_only_its_modules_in_every_view_and_ticking_a_member_adds_its_module()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    var total = await ModuleCount(page);

    await page.ClickAsync(GroupBox("Model"));

    Assert.Equal(["IOrderRepository", "Order", "OrderService"], await GraphNodes(page));
    Assert.Equal($"Filtered: 3 of {total} modules", await page.TextContentAsync("#explorer-filter-text"));
    Assert.Equal($"Filtered: 3 of {total} modules", await page.Locator("#view-graph .view-filter-note > span").TextContentAsync());
    Assert.True(await page.Locator(GroupBox("Orders")).EvaluateAsync<bool>("b => b.indeterminate"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));

    // A member brings its module, at module level.
    await page.ClickAsync(Group("No folder") + " li[data-id='Utils'] > .tree-row .tree-twisty");
    await page.ClickAsync(Box("Utils.FormatMoney"));
    Assert.Equal(["IOrderRepository", "Order", "OrderService", "Utils"], await GraphNodes(page));
    Assert.Equal($"Filtered: 4 of {total} modules", await page.TextContentAsync("#explorer-filter-text"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));

    // At member level, only that member of Utils, and every member of the ticked modules.
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
    var members = await GraphNodes(page);
    Assert.Contains("Utils.FormatMoney", members);
    Assert.DoesNotContain("Utils.UnusedHelper", members);
    Assert.Contains("Order.Describe", members);
    Assert.All(members, id => Assert.Matches(@"^(IOrderRepository|Order|OrderService|Utils)(\.|$)", id));

    await ShowClasses(page, 4);
    Assert.Equal(["IOrderRepository", "Order", "OrderService", "Utils"], await ClassBoxes(page));
    Assert.True(await page.IsVisibleAsync("#view-classes .view-filter-note"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_ticked_module_ticks_its_members_and_unticking_or_Clear_takes_items_out()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;

    await page.ClickAsync(Group("Model") + " li[data-id='Order'] > .tree-row .tree-twisty");
    await page.ClickAsync(Box("Order"));
    Assert.True(await page.IsCheckedAsync(Box("Order.Describe")));
    Assert.True(await page.IsDisabledAsync(Box("Order.Describe")));
    Assert.Equal("Order", await page.EvaluateAsync<string>("() => window.vbadoc.filterKey()")); // not its members too

    await page.ClickAsync(Box("Main"));
    Assert.Equal(["Main", "Order"], await GraphNodes(page));
    await page.ClickAsync(Box("Order"));
    Assert.Equal(["Main"], await GraphNodes(page));
    Assert.False(await page.IsDisabledAsync(Box("Order.Describe")));

    await page.ClickAsync("#explorer-filter-bar [data-filter-clear]");
    Assert.True(await page.IsHiddenAsync("#explorer-filter-bar"));
    Assert.True(await page.IsHiddenAsync("#view-graph .view-filter-note"));
    Assert.True((await GraphNodes(page)).Length > 2);
    Assert.False(await page.IsCheckedAsync(Box("Main")));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Selecting_doesnt_filter_and_the_filter_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;

    await page.ClickAsync("#explorer-tree li[data-id='Utils'] > .tree-row .tree-label");
    Assert.Equal("Utils", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.True(await page.IsHiddenAsync("#explorer-filter-bar"));

    await page.ClickAsync(GroupBox("Startup"));
    Assert.Equal(["AppInit", "Main", "Settings"], await GraphNodes(page));
    // Esc clears the selection, not the filter.
    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(["AppInit", "Main", "Settings"], await GraphNodes(page));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    Assert.Equal(["AppInit", "Main", "Settings"], await GraphNodes(page));
    Assert.True(await page.IsVisibleAsync("#explorer-filter-bar"));
    Assert.True(await page.IsCheckedAsync(GroupBox("Startup")));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Space_ticks_the_focused_row_and_Enter_selects_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    const string focused = "() => document.activeElement.closest('li').querySelector('.tree-label').textContent";

    await page.FocusAsync("#explorer-filter");
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("Orders", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("Space");
    Assert.Equal(["CsvOrderRepository", "IOrderRepository", "Order", "OrderService", "SheetOrderRepository"], await GraphNodes(page));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal("true", await page.GetAttributeAsync(Group("Orders"), "aria-expanded")); // Space didn't close it

    await page.Keyboard.PressAsync("ArrowDown");
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("IOrderRepository", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("Enter");
    Assert.Equal("IOrderRepository", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
  }

  [Fact]
  public async Task Expand_all_opens_every_folder_but_not_the_modules_and_Collapse_all_closes_everything()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#explorer-tree li[data-id='Utils'] > .tree-row .tree-twisty");

    await page.ClickAsync("#explorer-collapse-all");
    Assert.Equal(0, await page.Locator("#explorer-tree li[aria-expanded='true']").CountAsync());
    Assert.False(await page.IsVisibleAsync("#explorer-tree li[data-id='Order']"));

    await page.ClickAsync("#explorer-expand-all");
    Assert.Equal(0, await page.Locator("#explorer-tree li.group[aria-expanded='false']").CountAsync());
    Assert.True(await page.IsVisibleAsync("#explorer-tree li[data-id='CsvOrderRepository']"));
    Assert.Equal("false", await page.GetAttributeAsync("#explorer-tree li[data-id='Utils']", "aria-expanded"));
  }
}
