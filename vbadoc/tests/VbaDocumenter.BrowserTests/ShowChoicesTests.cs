using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The Show… menu in the explorer (step D3c of the feedback plan): one set of choices for the tree, the graph and the
/// class diagram, in place of the views' own Show… menus, said in every view's toolbar.
/// </summary>
[Collection("Browser")]
public class ShowChoicesTests(PageFixture fixture)
{
  static Task<string[]> GraphNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<string[]> ClassBoxes(IPage page) =>
      page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");

  static string Group(string label) => $"#explorer-tree li.group:has(> .tree-row .tree-label:text-is('{label}'))";

  [Fact]
  public async Task A_module_kind_turned_off_leaves_the_tree_the_graph_and_the_class_diagram()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.Equal(0, await page.Locator("#view-graph [id^='graph-filters'], #view-classes [id^='classes-filters']").CountAsync());

    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='module:document']");
    await page.Keyboard.PressAsync("Escape");

    Assert.DoesNotContain("Sheet1", await GraphNodes(page));
    Assert.DoesNotContain("ThisWorkbook", await GraphNodes(page));
    Assert.False(await page.IsVisibleAsync(Group("Document modules")));
    Assert.True(await page.IsVisibleAsync(Group("Classes")));
    Assert.Equal("Show… (1)", await page.Locator("#view-graph .view-show-note [data-show-open]").TextContentAsync());

    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    var boxes = await ClassBoxes(page);
    Assert.DoesNotContain("Sheet1", boxes);
    Assert.Contains("Order", boxes);
    Assert.True(await page.IsVisibleAsync("#view-classes .view-show-note"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_member_kind_turned_off_leaves_the_tree_and_the_class_diagrams_boxes()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#explorer-tree li[data-id='Order'] > .tree-row .tree-twisty");
    Assert.True(await page.IsVisibleAsync("#explorer-tree li[data-id='Order.m_Id'] > .tree-row"));

    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='member:data']");
    await page.Keyboard.PressAsync("Escape");

    Assert.False(await page.IsVisibleAsync("#explorer-tree li[data-id='Order.m_Id'] > .tree-row"));
    Assert.True(await page.IsVisibleAsync("#explorer-tree li[data-id='Order.Describe'] > .tree-row"));
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal(0, await page.Locator("#classes-svg g.class-line[data-member='Order.m_Id']").CountAsync());
    Assert.Equal(1, await page.Locator("#classes-svg g.class-line[data-member='Order.Describe']").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_toolbars_note_opens_the_menu_in_the_explorer_and_Show_everything_puts_everything_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutExplorer +
        "if (localStorage.getItem('vbadoc-show') === null) localStorage.setItem('vbadoc-show', JSON.stringify({ off: ['module:document', 'line:events'] }));");
    var page = opened.Page;
    Assert.Equal("Show… (2)", await page.Locator("#view-graph .view-show-note [data-show-open]").TextContentAsync());
    Assert.Equal(10, (await GraphNodes(page)).Length);

    await page.ClickAsync("#view-graph .view-show-note [data-show-open]");
    Assert.True(await page.IsVisibleAsync("#explorer"));
    Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('explorer-show').open"));
    Assert.False(await page.IsCheckedAsync("#explorer-show [data-choice='module:document']"));

    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#view-graph .view-show-note [data-show-reset]");
    Assert.Equal(12, (await GraphNodes(page)).Length);
    Assert.True(await page.IsHiddenAsync("#view-graph .view-show-note"));
    Assert.Equal("Show…", await page.InnerTextAsync("#explorer-show summary"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_sections_None_and_All_and_Show_nothing_make_picking_a_few_things_quick()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#explorer-show summary");

    await page.ClickAsync("#explorer-show section:has(h3 span:text-is('Modules')) [data-show-section-none]");
    Assert.Equal(0, await page.Locator("#explorer-show [data-choice^='module:']:checked").CountAsync());
    await page.CheckAsync("#explorer-show [data-choice='module:class']");
    Assert.Equal(["CsvOrderRepository", "Order", "OrderService", "ProgressReporter", "SheetOrderRepository"], await GraphNodes(page));

    await page.ClickAsync("#explorer-show section:has(h3 span:text-is('Modules')) [data-show-section-all]");
    Assert.Equal(5, await page.Locator("#explorer-show [data-choice^='module:']:checked").CountAsync());

    await page.ClickAsync("#explorer-show [data-show-nothing]");
    Assert.Equal(0, await page.Locator("#explorer-show [data-choice]:checked").CountAsync());
    Assert.Empty(await GraphNodes(page));
    Assert.Equal("all members", (await page.Locator("#explorer-show [data-radio='references']:checked").EvaluateAsync<string>("r => r.parentElement.textContent")).Trim().ToLowerInvariant());

    await page.ClickAsync("#explorer-show [data-show-reset]");
    Assert.Equal(12, (await GraphNodes(page)).Length);
    Assert.Empty(opened.Errors);
  }
}
