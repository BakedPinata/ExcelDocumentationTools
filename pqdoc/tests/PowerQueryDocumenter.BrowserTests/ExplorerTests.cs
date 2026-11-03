using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>The explorer (FR-HTML-11): queries in their Excel groups, or as queries and functions, in step with the graph.</summary>
[Collection("Browser")]
public sealed class ExplorerTests(PageFixture fixture)
{
  private const string TopLevel = "#explorer-tree > li > .tree-row .tree-label";

  private static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  private static string ChildLabels(string parent) => parent + " > ul > li > .tree-row .tree-label";

  private static async Task<string[]> Texts(IPage page, string selector) => [.. await page.Locator(selector).AllTextContentsAsync()];

  private static async Task<ILocator> Item(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li[data-id='{escaped}']");
  }

  [Fact]
  public async Task A_workbooks_queries_are_in_its_groups_in_Excels_order_with_the_rest_after()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    Assert.Equal(["Staging", "Reference", "Other queries"], await Texts(page, TopLevel));
    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], await Texts(page, ChildLabels(Group("Staging"))));
    Assert.Equal(["Companies", "Discount%", "Manual Targets"], await Texts(page, ChildLabels(Group("Reference"))));
    Assert.Equal("Function", await (await Item(opened, "fnClean")).Locator(".tree-detail").TextContentAsync());
    Assert.Equal("7", await page.Locator(Group("Other queries") + " > .tree-row .tree-detail").TextContentAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Without_groups_queries_and_functions_are_apart()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;

    Assert.Equal(["Queries", "Functions"], await Texts(page, TopLevel));
    Assert.Equal(["fnCleanText"], await Texts(page, ChildLabels(Group("Functions"))));
    Assert.Contains("Orders", await Texts(page, ChildLabels(Group("Queries"))));
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
