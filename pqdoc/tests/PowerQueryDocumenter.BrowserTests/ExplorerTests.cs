using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The explorer (FR-HTML-11): queries in their Excel groups, nested as in Excel, or as queries and functions, chosen with
/// a switch, and in step with the graph.
/// </summary>
[Collection("Browser")]
public sealed class ExplorerTests(PageFixture fixture)
{
  // The layout's own groups and items, apart from the shell's groups (Favourites, Widely used, Hidden), which have tests of
  // their own.
  private const string TopLevel = "#explorer-tree > li:not(.favourites, .widely-used-items, .hidden-items) > .tree-row .tree-label";

  private static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  private static string ChildLabels(string parent) => parent + " > ul > li > .tree-row .tree-label";

  private static async Task<string[]> Texts(IPage page, string selector) => [.. await page.Locator(selector).AllTextContentsAsync()];

  private static async Task<ILocator> Item(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li[data-id='{escaped}']:not(.widely-used-items li)");
  }

  [Fact]
  public async Task A_workbooks_queries_are_in_its_groups_in_Excels_order_with_the_rest_after()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    // Empty Folder holds no queries, so it isn't shown.
    Assert.Equal(["Staging", "Reference", "Root", "Other queries"], await Texts(page, TopLevel));
    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], await Texts(page, ChildLabels(Group("Staging"))));
    Assert.Equal(["Companies", "Discount%", "Manual Targets"], await Texts(page, ChildLabels(Group("Reference"))));
    Assert.Equal("Function", await (await Item(opened, "fnClean")).Locator(".tree-detail").TextContentAsync());
    Assert.Equal("5", await page.Locator(Group("Other queries") + " > .tree-row .tree-detail").TextContentAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Nested_groups_hold_their_groups_then_their_queries_and_count_everything_under_them()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    Assert.Equal(["Has Description"], await Texts(page, ChildLabels(Group("Root"))));
    Assert.Equal(["Deep", "Sales/Returns"], await Texts(page, ChildLabels(Group("Has Description"))));
    Assert.Equal(["Sales Data"], await Texts(page, ChildLabels(Group("Deep"))));
    Assert.Equal("2", await page.Locator(Group("Root") + " > .tree-row .tree-detail").TextContentAsync());
    Assert.Equal("This is a nested group with a description", await page.Locator(Group("Has Description") + " > .tree-row").GetAttributeAsync("title"));
    Assert.Equal("3", await page.Locator(Group("Deep")).GetAttributeAsync("aria-level"));
  }

  [Fact]
  public async Task Groups_are_drawn_as_folders_and_Other_queries_is_not_one()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    Assert.Equal(1, await page.Locator(Group("Staging") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(1, await page.Locator(Group("Deep") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(0, await page.Locator(Group("Other queries") + " > .tree-row svg.tree-icon").CountAsync());
    Assert.Equal(0, await page.Locator("#explorer-tree li.item svg.tree-icon").CountAsync());
  }

  [Fact]
  public async Task The_switch_shows_queries_and_functions_instead_keeping_the_selection_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    Assert.True(await page.IsVisibleAsync("#explorer-layouts"));
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-layouts [data-layout='groups']", "aria-checked"));
    await opened.SelectAsync("Sales Data");

    await page.ClickAsync("#explorer-layouts [data-layout='types']");

    Assert.Equal(["Queries", "Functions"], await Texts(page, TopLevel));
    Assert.Equal(0, await page.Locator("#explorer-tree svg.tree-icon").CountAsync());
    Assert.Equal("true", await (await Item(opened, "Sales Data")).GetAttributeAsync("aria-selected"));
    Assert.Equal("Sales Data", await opened.SelectionAsync());

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal(["Queries", "Functions"], await Texts(page, TopLevel));
    Assert.Equal("true", await page.GetAttributeAsync("#explorer-layouts [data-layout='types']", "aria-checked"));

    await page.ClickAsync("#explorer-layouts [data-layout='groups']");
    Assert.Equal(["Staging", "Reference", "Root", "Other queries"], await Texts(page, TopLevel));
  }

  [Fact]
  public async Task Without_groups_queries_and_functions_are_apart()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;

    Assert.Equal(["Queries", "Functions"], await Texts(page, TopLevel));
    Assert.Equal(["fnCleanText"], await Texts(page, ChildLabels(Group("Functions"))));
    Assert.Contains("Orders", await Texts(page, ChildLabels(Group("Queries"))));
    Assert.False(await page.IsVisibleAsync("#explorer-layouts"));
  }

  [Fact]
  public async Task Choosing_a_query_selects_it_and_the_explorer_follows_the_graph()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await (await Item(opened, "Orders")).Locator("> .tree-row").ClickAsync();

    Assert.Equal("Orders", await opened.SelectionAsync());
    Assert.Equal(["Orders"], await opened.DrawnIdsAsync("#graph-svg g.node.selected"));
    Assert.Equal("Orders", await page.InnerTextAsync("#side-panel-header h2"));

    await opened.SelectAsync("Sales Report");
    Assert.Equal("true", await (await Item(opened, "Sales Report")).GetAttributeAsync("aria-selected"));
    Assert.Equal("false", await (await Item(opened, "Orders")).GetAttributeAsync("aria-selected"));
  }

  [Fact]
  public async Task The_filter_keeps_matching_queries_and_their_groups()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    await page.FillAsync("#explorer-filter", "chain");

    Assert.Equal(["Staging", "Other queries"], await Texts(page, "#explorer-tree > li:not([hidden]) > .tree-row .tree-label"));
    Assert.Equal(["Chain_E"], await Texts(page, Group("Other queries") + " > ul > li:not([hidden]) > .tree-row .tree-label"));

    // The filter stays when the layout changes.
    await page.ClickAsync("#explorer-layouts [data-layout='types']");
    Assert.Equal(["Queries"], await Texts(page, "#explorer-tree > li:not([hidden]) > .tree-row .tree-label"));
    Assert.Equal(5, await page.Locator("#explorer-tree li.item:not([hidden])").CountAsync());
  }

  [Fact]
  public async Task The_filter_finds_a_query_deep_in_nested_groups()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    await page.ClickAsync(Group("Root") + " > .tree-row .tree-twisty");
    await page.FillAsync("#explorer-filter", "sales data");

    Assert.Equal(["Root"], await Texts(page, "#explorer-tree > li:not([hidden]) > .tree-row .tree-label"));
    Assert.True(await (await Item(opened, "Sales Data")).IsVisibleAsync());
  }

  [Fact]
  public async Task Names_are_only_ever_shown_as_text()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.AwkwardPage);
    var page = opened.Page;

    Assert.Contains(PageFixture.HostileQuery, await Texts(page, "#explorer-tree li.item > .tree-row .tree-label"));
    Assert.Equal(0, await page.Locator("#explorer-tree img").CountAsync());

    await (await Item(opened, "It's \"quoted\"")).Locator("> .tree-row").ClickAsync();
    Assert.Equal("It's \"quoted\"", await opened.SelectionAsync());
    Assert.Empty(opened.Errors);
  }
}
