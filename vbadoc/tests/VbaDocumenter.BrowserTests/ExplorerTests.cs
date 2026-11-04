using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The explorer: modules grouped by '@Folder or by kind, chosen with a switch, members under them, the filter,
/// the keyboard, and keeping in step with the views.</summary>
[Collection("Browser")]
public class ExplorerTests(PageFixture fixture)
{
  // The layout's own groups and items, apart from the shell's groups (Favourites, Widely used, Hidden), which have tests of
  // their own.
  const string TopLevel = "#explorer-tree > li:not(.favourites, .widely-used-items, .hidden-items) > .tree-row .tree-label";

  static string Item(string id) => $"#explorer-tree li[data-id='{id}']:not(.widely-used-items li)";

  static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  static string ChildLabels(string parent) => parent + " > ul > li > .tree-row .tree-label";

  static async Task<string[]> Texts(IPage page, string selector) => [.. await page.Locator(selector).AllTextContentsAsync()];

  [Fact]
  public async Task Without_folders_modules_are_grouped_by_kind()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    Assert.Equal(["Standard modules", "Classes", "Interfaces", "Document modules"], await Texts(page, TopLevel));
    Assert.Equal(["CsvOrderRepository", "Order", "OrderService", "ProgressReporter", "SheetOrderRepository"],
        await Texts(page, ChildLabels(Group("Classes"))));
    Assert.Equal(["IOrderRepository"], await Texts(page, ChildLabels(Group("Interfaces"))));
    Assert.Equal("5", await page.Locator(Group("Classes") + " > .tree-row .tree-detail").TextContentAsync());
    Assert.False(await page.IsVisibleAsync("#explorer-layouts"));
    Assert.Equal(0, await page.Locator("#explorer-tree svg.tree-icon").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Folder_comments_group_the_modules_and_nest_at_the_dots()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;

    Assert.Equal(["Orders", "Startup", "No folder"], await Texts(page, TopLevel));
    Assert.Equal(["Model", "Storage"], await Texts(page, ChildLabels(Group("Orders"))));
    Assert.Equal(["IOrderRepository", "Order", "OrderService"], await Texts(page, ChildLabels(Group("Model"))));
    Assert.Equal("Interface", await page.Locator(Item("IOrderRepository") + " > .tree-row .tree-detail").TextContentAsync());
    Assert.Equal(["Logger", "ProgressReporter", "Sheet1", "ThisWorkbook", "Utils"], await Texts(page, ChildLabels(Group("No folder"))));
    Assert.Equal("5", await page.Locator(Group("Orders") + " > .tree-row .tree-detail").TextContentAsync());
  }

  [Fact]
  public async Task Folders_are_drawn_as_folders_and_No_folder_is_not_one()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;

    Assert.Equal(1, await page.Locator(Group("Orders") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(1, await page.Locator(Group("Model") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(0, await page.Locator(Group("No folder") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(0, await page.Locator("#explorer-tree li.item svg.tree-icon").CountAsync());
  }

  [Fact]
  public async Task The_switch_shows_modules_by_kind_instead_keeping_the_selection_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    Assert.True(await page.IsVisibleAsync("#explorer-layouts"));
    Assert.Equal(["Folders", "Types"], await Texts(page, "#explorer-layouts button"));
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-layouts [data-layout='folders']", "aria-checked"));
    await opened.SelectAsync("Order.Describe");

    await page.ClickAsync("#explorer-layouts [data-layout='types']");

    Assert.Equal(["Standard modules", "Classes", "Interfaces", "Document modules"], await Texts(page, TopLevel));
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-layouts [data-layout='types']", "aria-checked"));
    Assert.Equal("true", await page.GetAttributeAsync(Item("Order.Describe"), "aria-selected"));
    Assert.True(await page.IsVisibleAsync(Item("Order.Describe") + " > .tree-row"));
    Assert.Equal("Order.Describe", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));

    await page.ReloadAsync();
    Assert.Equal(["Standard modules", "Classes", "Interfaces", "Document modules"], await Texts(page, TopLevel));

    await page.ClickAsync("#explorer-layouts [data-layout='folders']");
    Assert.Equal(["Orders", "Startup", "No folder"], await Texts(page, TopLevel));
  }

  [Fact]
  public async Task A_module_lists_its_members_in_source_order_with_an_Enums_values_under_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    Assert.Equal("false", await page.GetAttributeAsync(Item("Settings"), "aria-expanded"));

    await page.ClickAsync(Item("Settings") + " > .tree-row .tree-twisty");

    Assert.Equal("true", await page.GetAttributeAsync(Item("Settings"), "aria-expanded"));
    Assert.Equal(["Level", "Current"], await Texts(page, ChildLabels(Item("Settings"))));
    Assert.Equal(["Low", "High"], await Texts(page, ChildLabels(Item("Settings.Level"))));
    Assert.Equal("Function", await page.Locator(Item("Settings.Current") + " > .tree-row .tree-detail").TextContentAsync());

    var order = await page.EvaluateAsync<string[]>(
        "() => window.vbadoc.data.members.filter(m => m.module === 'Order').sort((a, b) => a.start - b.start || a.declaration - b.declaration).map(m => m.name)");
    Assert.Equal(order, await Texts(page, ChildLabels(Item("Order"))));
  }

  [Fact]
  public async Task Choosing_an_item_selects_it_in_every_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync(Item("OrderService") + " > .tree-row .tree-label");

    Assert.Equal("OrderService", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='OrderService']").CountAsync());
    Assert.StartsWith("OrderService", await page.InnerTextAsync("#side-panel-header h2"));
    await page.ClickAsync("#tab-classes");
    await page.WaitForSelectorAsync("#classes-svg g.class-box[data-module='OrderService'].selected");
  }

  [Fact]
  public async Task The_explorer_follows_selections_made_elsewhere()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await opened.SelectAsync("Order.Describe");

    Assert.Equal("true", await page.GetAttributeAsync(Item("Order.Describe"), "aria-selected"));
    Assert.Equal("true", await page.GetAttributeAsync(Item("Order"), "aria-expanded"));
    Assert.True(await page.IsVisibleAsync(Item("Order.Describe") + " > .tree-row"));

    await opened.SelectAsync("Logger");
    Assert.Equal("false", await page.GetAttributeAsync(Item("Order.Describe"), "aria-selected"));
    Assert.Equal("true", await page.GetAttributeAsync(Item("Logger"), "aria-selected"));
    Assert.Equal(1, await page.Locator("#explorer-tree li[aria-selected='true']:not(.widely-used-items li)").CountAsync());
  }

  [Fact]
  public async Task The_filter_keeps_what_matches_with_what_holds_it_and_clearing_it_puts_things_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.FillAsync("#explorer-filter", "describe");

    Assert.True(await page.IsVisibleAsync(Item("Order.Describe") + " > .tree-row"));
    Assert.True(await page.IsVisibleAsync(Item("Order") + " > .tree-row"));
    Assert.False(await page.IsVisibleAsync(Item("Logger") + " > .tree-row"));
    Assert.Equal(["Classes"], await Texts(page, "#explorer-tree > li:not([hidden]) > .tree-row .tree-label"));
    Assert.True(await page.IsHiddenAsync("#explorer-empty"));

    await page.FillAsync("#explorer-filter", "nothing like this");
    Assert.True(await page.IsVisibleAsync("#explorer-empty"));

    await page.FocusAsync("#explorer-filter");
    await page.Keyboard.PressAsync("Escape");
    Assert.Equal("", await page.InputValueAsync("#explorer-filter"));
    Assert.True(await page.IsVisibleAsync(Item("Logger") + " > .tree-row"));
    Assert.False(await page.IsVisibleAsync(Item("Order.Describe") + " > .tree-row")); // Order is closed again
  }

  [Fact]
  public async Task The_keyboard_moves_through_the_tree_opens_and_closes_and_chooses()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    const string focused = "() => document.activeElement.closest('li') && document.activeElement.closest('li').querySelector('.tree-label').textContent";

    await page.FocusAsync("#explorer-filter");
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("Standard modules", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("AppInit", await page.EvaluateAsync<string>(focused));

    await page.Keyboard.PressAsync("ArrowRight");
    Assert.Equal("true", await page.GetAttributeAsync(Item("AppInit"), "aria-expanded"));
    await page.Keyboard.PressAsync("ArrowLeft");
    Assert.Equal("false", await page.GetAttributeAsync(Item("AppInit"), "aria-expanded"));
    await page.Keyboard.PressAsync("ArrowLeft");
    Assert.Equal("Standard modules", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("ArrowDown");

    await page.Keyboard.PressAsync("Enter");
    Assert.Equal("AppInit", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator("#explorer-tree li[tabindex='0']").CountAsync());
  }

  [Fact]
  public async Task The_explorer_can_be_resized_and_hidden_and_both_are_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    const string width = "() => document.getElementById('explorer').getBoundingClientRect().width";
    var before = await page.EvaluateAsync<double>(width);
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-toggle", "aria-pressed"));

    await page.FocusAsync("#explorer-splitter");
    for (int i = 0; i < 3; i++) await page.Keyboard.PressAsync("ArrowRight");
    var wider = await page.EvaluateAsync<double>(width);
    Assert.True(wider > before + 50, $"{before} -> {wider}");

    await page.ReloadAsync();
    Assert.Equal(wider, await page.EvaluateAsync<double>(width), 1.0);

    await page.ClickAsync("#explorer-close");
    Assert.True(await page.IsHiddenAsync("#explorer"));
    Assert.True(await page.IsHiddenAsync("#explorer-splitter"));
    Assert.Equal("false", await page.GetAttributeAsync("#explorer-toggle", "aria-pressed"));
    Assert.Equal(0, await page.EvaluateAsync<double>("() => document.getElementById('view-graph').getBoundingClientRect().left"), 1.0);

    await page.ReloadAsync();
    Assert.True(await page.IsHiddenAsync("#explorer"));
    await page.ClickAsync("#explorer-toggle");
    Assert.True(await page.IsVisibleAsync("#explorer"));
  }

  [Fact]
  public async Task On_a_narrow_screen_it_starts_closed_and_opens_above_the_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, width: 760, height: 1000);
    var page = opened.Page;
    Assert.True(await page.IsHiddenAsync("#explorer"));

    await page.ClickAsync("#explorer-toggle");

    var explorer = (await page.Locator("#explorer").BoundingBoxAsync())!;
    var view = (await page.Locator("#view-graph").BoundingBoxAsync())!;
    Assert.True(explorer.Y + explorer.Height <= view.Y + 1, $"explorer ends at {explorer.Y + explorer.Height}, view starts at {view.Y}");
    Assert.Equal(view.Width, explorer.Width, 1.0);
  }
}
