using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Lock view (step L1 of the generalisation plan): a button at the start of every view's toolbar that keeps what the
/// views show where it is, so a click on an item or on empty space changes only the selection and the source panel.
/// Locking ticks what's shown in the explorer, unlocking puts the ticks back, and a saved view keeps the lock.
/// </summary>
[Collection("Browser")]
public class LockViewTests(PageFixture fixture)
{
  const string GraphLock = "#view-graph [data-lock-view]";
  const string ClassesLock = "#view-classes [data-lock-view]";

  static string Node(string id) => $"#graph-svg g.node[data-id='{id}']";

  static string ClassBox(string module) => $"#classes-svg g.class-box[data-module='{module}']";

  static string TreeBox(string id) => $"#explorer-tree li.item:not(.favourites li)[data-id='{id}'] > .tree-row > .tree-check";

  static Task<string[]> ShownNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  /// <summary>Every node's place, every line drawn, and the zoom: what locking keeps.</summary>
  static Task<string> GraphPicture(IPage page) => page.EvaluateAsync<string>("""
      () => [...document.querySelectorAll('#graph-svg g.node')].map(n => n.dataset.id + n.getAttribute('transform')).sort().join(';') +
        ' | ' + document.querySelectorAll('#graph-svg path.edge').length +
        ' | ' + document.querySelector('#graph-svg g.viewport').getAttribute('transform')
      """);

  static Task<string> ClassesPicture(IPage page) => page.EvaluateAsync<string>("""
      () => [...document.querySelectorAll('#classes-svg g.class-box')].map(n => n.dataset.module + n.getAttribute('transform')).sort().join(';') +
        ' | ' + document.querySelector('#classes-svg g.viewport').getAttribute('transform')
      """);

  static Task WaitUntilStill(IPage page) =>
      page.WaitForFunctionAsync("() => window.vbadocGraph.nodeCount() > 0 && !window.vbadocGraph.moving() && !window.vbadocGraph.sliding()", null, new() { Timeout = 30000 });

  static Task WaitForClasses(IPage page) =>
      page.WaitForFunctionAsync("() => window.vbadocClasses.drawn() && !window.vbadocClasses.moving()", null, new() { Timeout = 30000 });

  /// <summary>Clicks the graph's background, in its bottom right corner, away from the toolbar, the legend and the nodes.</summary>
  static async Task ClickBackground(IPage page, string svg)
  {
    var box = await page.Locator(svg).BoundingBoxAsync();
    await page.Mouse.ClickAsync(box!.X + box.Width - 6, box.Y + box.Height - 6);
  }

  /// <summary>The legends float over the views' corners, and a view that's locked doesn't move to clear them.</summary>
  static Task CloseLegends(IPage page) =>
      page.EvaluateAsync("() => document.querySelectorAll('details.view-legend').forEach(d => { d.open = false; })");

  [Fact]
  public async Task Locked_a_click_on_an_item_or_on_empty_space_changes_only_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync(Node("Utils"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
    await WaitUntilStill(page);
    var nodes = await page.EvaluateAsync<string>("() => [...document.querySelectorAll('#graph-svg g.node')].map(n => n.dataset.id + n.getAttribute('transform')).sort().join(';')");
    var zoom = await page.EvaluateAsync<string>("() => document.querySelector('#graph-svg g.viewport').getAttribute('transform')");

    await page.ClickAsync(GraphLock);
    Assert.Equal("true", await page.GetAttributeAsync(GraphLock, "aria-pressed"));
    var locked = await GraphPicture(page);
    Assert.StartsWith(nodes, locked);   // locking moved nothing
    Assert.EndsWith(zoom, locked);      // and left the zoom

    await page.ClickAsync(Node("Order"));
    Assert.Equal("Order", await Selection(page));
    Assert.Contains("Order", await page.InnerTextAsync("#side-panel-header"));
    Assert.Equal(locked, await GraphPicture(page));
    Assert.Equal(["Order"], await page.Locator("#graph-svg g.node.selected").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)"));
    Assert.Equal(0, await page.Locator("#graph-svg g.node.dim, #graph-svg g.node.related, #graph-svg path.edge.dim").CountAsync());

    await page.ClickAsync(Node("Logger"), new() { Modifiers = [KeyboardModifier.Control] });
    Assert.Equal("Logger", await Selection(page));
    Assert.Equal(locked, await GraphPicture(page));

    await ClickBackground(page, "#graph-svg");
    Assert.Null(await Selection(page));
    Assert.Equal(locked, await GraphPicture(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Locked_the_views_keep_still_when_the_source_panel_opens_and_closes_with_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync(Node("Utils"));
    await WaitUntilStill(page);
    await page.ClickAsync(GraphLock);
    var locked = await GraphPicture(page);

    await ClickBackground(page, "#graph-svg");   // the panel closes
    await page.WaitForFunctionAsync("() => document.getElementById('layout').classList.contains('side-panel-hidden')");
    await page.ClickAsync(Node("Order"));          // and opens again
    await page.WaitForFunctionAsync("() => !document.getElementById('layout').classList.contains('side-panel-hidden')");
    await page.WaitForTimeoutAsync(400);
    Assert.Equal(locked, await GraphPicture(page));
  }

  [Fact]
  public async Task Locking_ticks_what_is_shown_and_unlocking_puts_the_ticks_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync(TreeBox("Logger"));     // a filter of the user's own, before locking
    await page.ClickAsync(TreeBox("Utils"));
    await page.ClickAsync(TreeBox("Order"));
    await page.ClickAsync(Node("Utils"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
    await page.ClickAsync(TreeBox("Order"));
    Assert.Equal(["Logger", "Utils"], await ShownNodes(page));

    await page.ClickAsync(GraphLock);
    Assert.True(await page.IsCheckedAsync(TreeBox("Logger")));
    Assert.True(await page.IsCheckedAsync(TreeBox("Utils")));
    Assert.False(await page.IsCheckedAsync(TreeBox("Order")));
    Assert.StartsWith("Locked: 2 of ", await page.InnerTextAsync("#view-graph .view-filter-note span"));
    Assert.Equal("Unlock", await page.InnerTextAsync("#view-graph .view-filter-note [data-filter-clear]"));
    Assert.True(await page.IsDisabledAsync("#graph-focus"));

    // A tick still adds an item, and the ones there stay where they are.
    await WaitUntilStill(page);
    var utils = await page.GetAttributeAsync(Node("Utils"), "transform");
    await page.ClickAsync(TreeBox("Order"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
    Assert.Equal(utils, await page.GetAttributeAsync(Node("Utils"), "transform"));
    await page.ClickAsync(TreeBox("Logger"));
    Assert.Equal(["Order", "Utils"], await ShownNodes(page));
    Assert.Equal(utils, await page.GetAttributeAsync(Node("Utils"), "transform"));

    await page.ClickAsync(GraphLock);
    Assert.Equal("false", await page.GetAttributeAsync(GraphLock, "aria-pressed"));
    Assert.True(await page.IsCheckedAsync(TreeBox("Logger")));
    Assert.True(await page.IsCheckedAsync(TreeBox("Utils")));
    Assert.False(await page.IsCheckedAsync(TreeBox("Order")));
    Assert.False(await page.IsDisabledAsync("#graph-focus"));
    Assert.StartsWith("Filtered: 2 of ", await page.InnerTextAsync("#view-graph .view-filter-note span"));
    Assert.Equal("Clear", await page.InnerTextAsync("#view-graph .view-filter-note [data-filter-clear]"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task At_member_level_locking_keeps_a_module_node_without_bringing_its_members()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync("#view-graph [data-level='members']");
    // A member that refers to another module itself (As Order, say), so its focus shows that module's own node.
    var member = await page.EvaluateAsync<string>("""
        () => {
          const vb = window.vbadoc;
          const edge = vb.data.memberEdges.find(e => vb.membersById.has(e.from) && vb.modulesById.has(e.to) && vb.membersById.get(e.from).module !== e.to);
          return edge.from;
        }
        """);
    await opened.SelectAsync(member);
    await WaitUntilStill(page);
    var shown = await ShownNodes(page);
    Assert.Contains(shown, id => !id.Contains('.'));   // a module's own node is among them
    var before = await GraphPicture(page);

    await page.ClickAsync(GraphLock);
    Assert.Equal(shown, await ShownNodes(page));
    Assert.StartsWith(before[..before.IndexOf(" | ", StringComparison.Ordinal)], await GraphPicture(page));

    var other = shown.First(id => id.Contains('.') && id != member);
    await page.ClickAsync(Node(other));
    Assert.Equal(other, await Selection(page));
    Assert.Equal(shown, await ShownNodes(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_lock_is_one_for_the_page_and_the_class_diagram_keeps_still_too()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync("#tab-classes");
    await WaitForClasses(page);
    await page.ClickAsync(ClassBox("Order") + " .box-name");
    await WaitForClasses(page);
    var boxes = await page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");
    Assert.True(boxes.Length > 1);
    var before = await ClassesPicture(page);

    await page.ClickAsync(ClassesLock);
    await WaitForClasses(page);
    Assert.Equal(before, await ClassesPicture(page));
    Assert.Equal("true", await page.GetAttributeAsync(GraphLock, "aria-pressed"));

    var other = boxes.First(id => id != "Order");
    await page.ClickAsync(ClassBox(other) + " .box-name");
    Assert.Equal(other, await Selection(page));
    await WaitForClasses(page);
    Assert.Equal(before, await ClassesPicture(page));

    await ClickBackground(page, "#classes-svg");
    Assert.Null(await Selection(page));
    Assert.Equal(before, await ClassesPicture(page));

    // The graph shows the same modules, and holds still as well.
    await page.ClickAsync("#tab-graph");
    Assert.Equal(boxes, await ShownNodes(page));
    await WaitUntilStill(page);
    var graph = await GraphPicture(page);
    await page.ClickAsync(Node(other));
    Assert.Equal(graph, await GraphPicture(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_lock_is_remembered_and_a_saved_view_keeps_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await CloseLegends(page);
    await page.ClickAsync(Node("Utils"));
    await page.ClickAsync(GraphLock);
    await page.ClickAsync("#views > summary");
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", "locked");
    await page.ClickAsync("#views-form-save");
    await page.Keyboard.PressAsync("Escape");

    await page.ClickAsync(GraphLock);   // unlocked: everything, or the selection's focus, shows again
    Assert.Equal("false", await page.GetAttributeAsync(GraphLock, "aria-pressed"));

    await page.ClickAsync("#views > summary");
    await page.ClickAsync("#views-list li:has(.views-name:text-is('locked')) .views-open");
    Assert.Equal("true", await page.GetAttributeAsync(GraphLock, "aria-pressed"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync(Node("Order"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc && window.vbadocGraph.nodeCount() > 0");
    Assert.Equal("true", await page.GetAttributeAsync(GraphLock, "aria-pressed"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));

    await page.ClickAsync(GraphLock);
    Assert.False(await page.EvaluateAsync<bool>("() => window.vbadoc.filterActive()"));
    Assert.Empty(opened.Errors);
  }
}
