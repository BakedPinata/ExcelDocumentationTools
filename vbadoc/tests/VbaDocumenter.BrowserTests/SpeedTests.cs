using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Speed on a project the size of the user's large one (step D1d of the feedback plan): what made hovering and clearing
/// a focus slow stays fixed. Timings vary between machines, so the budget is generous, and the other tests check what
/// can't vary: the tooltip is hidden without leaving the layout, and the whole graph's layout comes back unchanged.
/// </summary>
[Collection("Browser")]
public class SpeedTests(PageFixture fixture)
{
  static Task WaitUntilStill(IPage page) =>
      page.WaitForFunctionAsync("() => window.vbadocGraph.nodeCount() > 0 && !window.vbadocGraph.moving()", null, new() { Timeout = 60000 });

  static async Task ShowMembers(IPage page)
  {
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'", null, new() { Timeout = 60000 });
    await WaitUntilStill(page);
  }

  /// <summary>Every drawn node's position, by id, rounded to a tenth of a unit.</summary>
  static Task<Dictionary<string, double[]>> Positions(IPage page) => page.EvaluateAsync<Dictionary<string, double[]>>(@"() => {
      const all = {};
      document.querySelectorAll('#graph-svg g.node').forEach(n => {
        const p = window.vbadocGraph.position(n.dataset.id);
        all[n.dataset.id] = [Math.round(p.x * 10) / 10, Math.round(p.y * 10) / 10];
      });
      return all;
    }");

  [Fact]
  public async Task Hovering_member_nodes_on_a_large_project_stays_quick()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.UserSizedEmbedded, width: 1600, height: 900);
    var page = opened.Page;
    await ShowMembers(page);
    await page.ClickAsync("#graph-fit");
    await page.WaitForTimeoutAsync(400);
    var points = await page.EvaluateAsync<double[][]>(@"() => Array.from(document.querySelectorAll('#graph-svg g.node'))
        .map(n => n.getBoundingClientRect())
        .filter(r => r.width > 0 && r.top > 140 && r.left > 320 && r.right < innerWidth - 20 && r.bottom < innerHeight - 20)
        .slice(0, 20).map(r => [r.left + r.width / 2, r.top + r.height / 2])");
    Assert.True(points.Length >= 10, $"only {points.Length} nodes to hover");
    await page.EvaluateAsync(@"() => {
        window.longTasks = 0;
        new PerformanceObserver(list => list.getEntries().forEach(e => { window.longTasks += e.duration; })).observe({ entryTypes: ['longtask'] });
      }");

    foreach (var point in points) await page.Mouse.MoveAsync((float)point[0], (float)point[1], new() { Steps = 3 });
    await page.WaitForTimeoutAsync(300);

    // Before D1d each hover laid out the whole graph again: about 100 ms each here, so 2 s for these. Now it's tens of ms.
    var longTasks = await page.EvaluateAsync<double>("() => window.longTasks");
    Assert.True(longTasks < 800, $"hovering {points.Length} nodes blocked the page for {longTasks:0} ms");
    Assert.True(await page.IsVisibleAsync("#graph-tooltip"));
  }

  [Fact]
  public async Task The_hidden_tooltip_stays_in_the_layout_so_showing_it_does_not_lay_out_the_graph()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    Assert.True(await page.IsHiddenAsync("#graph-tooltip"));
    Assert.Equal("block", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('graph-tooltip')).display"));
    Assert.Equal("hidden", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('graph-tooltip')).visibility"));
  }

  [Fact]
  public async Task Clearing_a_focus_puts_the_whole_graph_back_as_it_was_with_dragged_nodes_still_pinned()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.UserSizedEmbedded, width: 1600, height: 900,
        initScript: "localStorage.setItem('vbadoc-graph-settings', JSON.stringify({ pinDragged: true }));");
    var page = opened.Page;
    await ShowMembers(page);
    await page.ClickAsync("#graph-fit");
    await page.WaitForTimeoutAsync(400);
    var node = "#graph-svg g.node[data-id='M05.Procedure03'] path.shape";
    var box = (await page.Locator(node).BoundingBoxAsync())!;
    await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 10; i++) await page.Mouse.MoveAsync(box.X + box.Width / 2 + i * 6, box.Y + box.Height / 2 + i * 4);
    await page.Mouse.UpAsync();
    await WaitUntilStill(page);
    var before = await Positions(page);
    Assert.Equal(1, await page.Locator("#graph-svg g.node.pinned").CountAsync());

    await opened.SelectAsync("M10.Procedure05"); // focus on click is on: only its neighbourhood is drawn
    await WaitUntilStill(page);
    Assert.True(await page.Locator("#graph-svg g.node").CountAsync() < 20);
    await page.Keyboard.PressAsync("Escape");
    await WaitUntilStill(page);

    Assert.Equal(before, await Positions(page));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.pinned[data-id='M05.Procedure03']").CountAsync());
  }

  [Fact]
  public async Task Switching_level_and_back_keeps_the_layout_and_Re_run_layout_starts_afresh()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await WaitUntilStill(page);
    var starting = await Positions(page);
    var utils = (await page.Locator("#graph-svg g.node[data-id='Utils'] path.shape").BoundingBoxAsync())!;
    await page.Mouse.MoveAsync(utils.X + utils.Width / 2, utils.Y + utils.Height / 2);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 10; i++) await page.Mouse.MoveAsync(utils.X + utils.Width / 2 + i * 8, utils.Y + utils.Height / 2 + i * 6);
    await page.Mouse.UpAsync();
    await WaitUntilStill(page);
    var moved = await Positions(page);
    Assert.NotEqual(starting, moved);

    await ShowMembers(page);
    await page.ClickAsync("[data-level='modules']");
    await WaitUntilStill(page);
    Assert.Equal(moved, await Positions(page));

    // Re-run layout lays the graph out afresh, which gives the starting layout again.
    await page.ClickAsync("#graph-settings summary");
    await page.ClickAsync("#graph-relayout");
    await WaitUntilStill(page);
    Assert.Equal(starting, await Positions(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Dragging_a_module_outline_doesnt_pull_the_rest_of_a_large_graph_in()
  {
    // Before the drag was made gentler, the rest of this graph pulled in to about 93% of its spread while a module was
    // dragged, and sprang back once it was let go.
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.UserSizedEmbedded, width: 1600, height: 900,
        initScript: OpenedPage.WithoutExplorer);
    var page = opened.Page;
    await WaitUntilStill(page);
    await ShowMembers(page);
    const string spread = @"() => {
      const ps = [...document.querySelectorAll('#graph-svg g.node')].map(n => window.vbadocGraph.position(n.dataset.id));
      const cx = ps.reduce((s, p) => s + p.x, 0) / ps.length, cy = ps.reduce((s, p) => s + p.y, 0) / ps.length;
      return ps.reduce((s, p) => s + Math.hypot(p.x - cx, p.y - cy), 0) / ps.length;
    }";
    var before = await page.EvaluateAsync<double>(spread);
    var outline = (await page.Locator("#graph-svg g.hull-group path.hull").Nth(2).BoundingBoxAsync())!;
    float x = outline.X + 6, y = outline.Y + outline.Height / 2;

    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 20; i++) { await page.Mouse.MoveAsync(x + i * 10, y + i * 4); await page.WaitForTimeoutAsync(40); }
    await page.WaitForTimeoutAsync(300);
    var during = await page.EvaluateAsync<double>(spread);
    await page.Mouse.UpAsync();

    Assert.True(during / before > 0.97, $"the graph pulled in to {during / before:P0} of its spread while dragging");
    Assert.Empty(opened.Errors);
  }
}
