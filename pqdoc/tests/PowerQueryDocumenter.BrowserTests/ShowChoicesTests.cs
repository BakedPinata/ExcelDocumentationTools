using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The Show… menu in the explorer (FR-HTML-17, step D3c of the feedback plan): queries, functions and queries with no
/// connections, chosen once for the tree and the graph, said in the graph's toolbar, and remembered.
/// </summary>
[Collection("Browser")]
public sealed class ShowChoicesTests(PageFixture fixture)
{
  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  [Fact]
  public async Task FR_HTML_17_turning_functions_off_takes_them_out_of_the_tree_and_the_graph_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    Assert.Contains("fnClean", await Shown(opened));

    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='kind:function']");
    await page.Keyboard.PressAsync("Escape");

    Assert.DoesNotContain("fnClean", await Shown(opened));
    Assert.False(await page.IsVisibleAsync("#explorer-tree li[data-id='fnClean'] > .tree-row"));
    Assert.True(await page.IsVisibleAsync("#explorer-tree li[data-id='Chain_A'] > .tree-row"));
    Assert.Equal("Show… (1)", await page.Locator("#view-graph .view-show-note [data-show-open]").TextContentAsync());

    await page.ClickAsync("#explorer-layouts [data-layout='types']");
    Assert.False(await page.IsVisibleAsync(Group("Functions")));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.DoesNotContain("fnClean", await Shown(opened));
    await page.ClickAsync("#view-graph .view-show-note [data-show-reset]");
    Assert.Contains("fnClean", await Shown(opened));
    Assert.True(await page.IsVisibleAsync(Group("Functions")));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_17_queries_with_no_connections_can_be_left_out_unless_ticked()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    var isolated = await page.EvaluateAsync<string[]>("() => window.pqdoc.data.queries.filter(q => q.role === 'isolated').map(q => q.id)");
    Assert.NotEmpty(isolated);

    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='unconnected']");
    await page.Keyboard.PressAsync("Escape");
    var shown = await Shown(opened);
    Assert.All(isolated, id => Assert.DoesNotContain(id, shown));
    // They stay in the tree: having no connections is about the graph.
    Assert.True(await page.IsVisibleAsync($"#explorer-tree li[data-id='{isolated[0]}'] > .tree-row"));

    await page.ClickAsync($"#explorer-tree li.item[data-id='{isolated[0]}'] > .tree-row > .tree-check");
    Assert.Equal([isolated[0]], await Shown(opened));
    Assert.Empty(opened.Errors);
  }
}
