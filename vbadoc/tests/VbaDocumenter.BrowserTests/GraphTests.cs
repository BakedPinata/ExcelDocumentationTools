using System.Diagnostics;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The dependency graph view, on SampleVba (see SampleVbaDependencyTests for its 29 module edges).</summary>
[Collection("Browser")]
public class GraphTests(PageFixture fixture)
{
  static Task<int> Count(IPage page, string selector) => page.Locator(selector).CountAsync();

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static async Task ShowMembers(IPage page)
  {
    await page.ClickAsync("[data-level='members']");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
  }

  [Fact]
  public async Task Module_level_draws_every_module_and_dependency()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    Assert.Equal(12, await Count(page, "#graph-svg g.node"));
    Assert.Equal(29, await Count(page, "#graph-svg path.edge"));
    Assert.Equal(2, await Count(page, "#graph-svg path.edge.implements"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.events"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.hollow")); // the interface
    Assert.Equal("IOrderRepository", await page.GetAttributeAsync("#graph-svg g.node.hollow", "data-id"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_legend_explains_kinds_and_all_three_edge_types()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var legend = await opened.Page.InnerTextAsync("#graph-legend");

    foreach (var text in new[] { "Standard module", "Class", "Interface", "UserForm", "Document module", "Uses", "Implements", "Events" })
      Assert.Contains(text, legend);
  }

  [Fact]
  public async Task Clicking_a_node_selects_it_and_highlights_its_direct_neighbours()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#graph-svg g.node[data-id='Main']");

    Assert.Equal("Main", await Selection(page));
    Assert.Contains("Main", await page.InnerTextAsync("#source-header h2"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected"));
    Assert.Equal(9, await Count(page, "#graph-svg g.node.related")); // 8 it uses + Sheet1, which uses it
    Assert.Equal(9, await Count(page, "#graph-svg path.edge.highlighted"));
    Assert.Equal(2, await Count(page, "#graph-svg g.node.dim")); // Utils and ThisWorkbook
    Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Utils']"));
  }

  [Fact]
  public async Task Transitive_highlights_the_whole_chain_in_each_direction()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='Main']");

    await page.ClickAsync("[data-reach='transitive']");

    // Main -> Order -> Utils downstream; ThisWorkbook -> AppInit -> Sheet1 -> Main upstream.
    Assert.Equal(0, await Count(page, "#graph-svg g.node.dim"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='Utils']"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='ThisWorkbook']"));
  }

  [Fact]
  public async Task Direction_highlights_only_dependencies_or_only_dependents()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='Main']");

    await page.ClickAsync("[data-direction='dependencies']");
    Assert.Equal(8, await Count(page, "#graph-svg g.node.related")); // the 8 modules Main uses
    Assert.Equal(8, await Count(page, "#graph-svg path.edge.highlighted[data-from='Main']"));
    Assert.Equal(8, await Count(page, "#graph-svg path.edge.highlighted"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Sheet1']"));

    await page.ClickAsync("[data-direction='dependents']");
    Assert.Equal(new[] { "Sheet1" }, await RelatedIds(page));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.highlighted[data-from='Sheet1'][data-to='Main']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.highlighted"));

    // Transitive dependents: ThisWorkbook -> AppInit -> Sheet1 -> Main, and SheetOrderRepository -> Sheet1.
    await page.ClickAsync("[data-reach='transitive']");
    Assert.Equal(new[] { "AppInit", "Sheet1", "SheetOrderRepository", "ThisWorkbook" }, await RelatedIds(page));

    await page.ClickAsync("[data-direction='all']");
    Assert.Equal(0, await Count(page, "#graph-svg g.node.dim"));
  }

  [Fact]
  public async Task Direction_applies_inside_a_module_for_a_member_but_not_for_a_selected_module()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    await page.ClickAsync("[data-direction='dependents']");
    var internalEdge = "#graph-svg path.edge[data-from='Logger.LogInfo'][data-to='Logger.WriteLine']";

    // A member: LogInfo's call to WriteLine is a dependency, so it isn't shown, even in the same module.
    await opened.SelectAsync("Logger.LogInfo");
    Assert.Equal(1, await Count(page, internalEdge + ".dim"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Logger.WriteLine']"));
    Assert.Equal(0, await Count(page, "#graph-svg path.edge.highlighted[data-from='Logger.LogInfo']"));

    // The whole module: its own edges stay; only edges coming in from other modules are added.
    await opened.SelectAsync("Logger");
    Assert.Equal(1, await Count(page, internalEdge + ".highlighted"));
    var highlightedTargets = await page.Locator("#graph-svg path.edge.highlighted")
        .EvaluateAllAsync<string[]>("es => es.map(e => e.dataset.to)");
    Assert.True(highlightedTargets.Length > 1);
    Assert.All(highlightedTargets, to => Assert.StartsWith("Logger.", to));

    await page.ClickAsync("[data-direction='dependencies']");
    Assert.Equal(1, await Count(page, internalEdge + ".highlighted"));
    var highlightedSources = await page.Locator("#graph-svg path.edge.highlighted")
        .EvaluateAllAsync<string[]>("es => es.map(e => e.dataset.from)");
    Assert.All(highlightedSources, from => Assert.StartsWith("Logger.", from));
  }

  static async Task<string[]> RelatedIds(IPage page) =>
      (await page.Locator("#graph-svg g.node.related").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)")).Order().ToArray();

  [Fact]
  public async Task Clicking_the_background_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='Order']");

    await page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");

    Assert.Null(await Selection(page));
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
  }

  [Fact]
  public async Task Member_level_groups_members_by_module_and_shows_recursion()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await ShowMembers(page);

    Assert.Equal(12, await Count(page, "#graph-svg g.hull-group"));
    Assert.Equal(1, await Count(page, "#graph-svg g.hull-group[data-module='Order'] text.hull-label"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node[data-id='Order.Status[Let]']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.self-loop[data-from='Main.Factorial']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.events[data-from='ProgressReporter.m_svc_Progress'][data-to='OrderService.Progress']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.implements[data-from='CsvOrderRepository.IOrderRepository_Save']"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.unreferenced[data-id='Utils.UnusedHelper']"));
    Assert.Empty(opened.Errors);
  }

  /// <summary>Pairs of labels whose boxes on screen overlap (all labels are shown by default).</summary>
  static async Task<string[]> OverlappingLabels(IPage page)
  {
    return await page.EvaluateAsync<string[]>(@"() => {
            const boxes = [...document.querySelectorAll('#graph-svg g.node')].map(n => ({ id: n.dataset.id, r: n.querySelector('text.label').getBoundingClientRect() }));
            const overlaps = [];
            for (let i = 0; i < boxes.length; i++)
                for (let j = i + 1; j < boxes.length; j++) {
                    const a = boxes[i].r, b = boxes[j].r;
                    if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) overlaps.push(boxes[i].id + ' / ' + boxes[j].id);
                }
            return overlaps;
        }");
  }

  [Fact]
  public async Task Member_names_never_overlap()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    await ShowMembers(opened.Page);

    Assert.Empty(await OverlappingLabels(opened.Page));
  }

  const string CrossModuleEdge = "#graph-svg path.edge[data-from='Order.Describe'][data-to='Utils.FormatMoney']";

  static async Task OpenSettings(IPage page)
  {
    await page.ClickAsync("#graph-settings summary");
    await page.WaitForSelectorAsync("#graph-settings-menu", new() { State = WaitForSelectorState.Visible });
  }

  static Task WaitUntilStill(IPage page) => page.WaitForFunctionAsync("() => !window.vbadocGraph.moving()", null, new() { Timeout = 20000 });

  static Task<double[]> Position(IPage page, string id) =>
      page.EvaluateAsync<double[]>("id => { const p = window.vbadocGraph.position(id); return [p.x, p.y]; }", id);

  /// <summary>Drags from the middle of an element by (dx, dy) screen pixels in small steps, like a person would.</summary>
  static async Task Drag(IPage page, string selector, float dx, float dy)
  {
    var box = (await page.Locator(selector).BoundingBoxAsync())!;
    await DragFrom(page, box.X + box.Width / 2, box.Y + box.Height / 2, dx, dy);
  }

  static async Task DragFrom(IPage page, float x, float y, float dx, float dy)
  {
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 10; i++) await page.Mouse.MoveAsync(x + dx * i / 10, y + dy * i / 10);
    await page.Mouse.UpAsync();
  }

  [Fact]
  public async Task Member_names_are_shown_without_a_selection_unless_the_setting_says_otherwise()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    var label = page.Locator("#graph-svg g.node[data-id='Order.Describe'] text.label");
    Assert.True(await label.IsVisibleAsync());

    await OpenSettings(page);
    await page.SelectOptionAsync("#graph-setting-labels", "selection");
    Assert.False(await label.IsVisibleAsync());

    await page.ReloadAsync(); // remembered per browser
    await page.WaitForFunctionAsync("() => window.vbadocGraph && window.vbadocGraph.level() === 'members'");
    Assert.False(await label.IsVisibleAsync());

    await OpenSettings(page);
    await page.ClickAsync("#graph-reset-settings");
    Assert.True(await label.IsVisibleAsync());
    Assert.Equal("always", await page.InputValueAsync("#graph-setting-labels"));
  }

  [Fact]
  public async Task Drop_downs_close_when_clicking_elsewhere_or_pressing_Escape()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    var settingsOpen = "() => document.getElementById('graph-settings').open";
    var filtersOpen = "() => document.getElementById('graph-filters').open";

    await OpenSettings(page);
    await page.ClickAsync("#graph-setting-curvedEdges"); // using a control inside keeps it open
    Assert.True(await page.EvaluateAsync<bool>(settingsOpen));

    await page.ClickAsync("#graph-filters summary"); // opening the other one closes it
    Assert.False(await page.EvaluateAsync<bool>(settingsOpen));
    Assert.True(await page.EvaluateAsync<bool>(filtersOpen));

    await page.Mouse.ClickAsync(900, 700); // the graph background
    Assert.False(await page.EvaluateAsync<bool>(filtersOpen));

    await OpenSettings(page);
    await page.Keyboard.PressAsync("Escape");
    Assert.False(await page.EvaluateAsync<bool>(settingsOpen));
  }

  [Fact]
  public async Task Edges_are_single_curves_unless_bundling_is_switched_on()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    Assert.Contains("Q", await page.GetAttributeAsync(CrossModuleEdge, "d"));

    await OpenSettings(page);
    await page.CheckAsync("#graph-setting-bundleEdges");
    Assert.Contains("C", await page.GetAttributeAsync(CrossModuleEdge, "d")); // a spline through the shared points

    await page.UncheckAsync("#graph-setting-bundleEdges");
    await page.UncheckAsync("#graph-setting-curvedEdges");
    Assert.Contains("L", await page.GetAttributeAsync(CrossModuleEdge, "d")); // a straight line
  }

  [Fact]
  public async Task Dragging_a_node_moves_it_and_the_graph_rearranges_without_selecting_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var main = await Position(page, "Main");
    var order = await Position(page, "Order");

    await Drag(page, "#graph-svg g.node[data-id='Main'] path.shape", 150, 100);
    await WaitUntilStill(page);

    var mainAfter = await Position(page, "Main");
    var orderAfter = await Position(page, "Order");
    Assert.True(Math.Abs(mainAfter[0] - main[0]) > 20, $"Main didn't move: {main[0]} -> {mainAfter[0]}");
    Assert.True(Math.Abs(orderAfter[0] - order[0]) + Math.Abs(orderAfter[1] - order[1]) > 1, "Order, which Main uses, didn't follow");
    Assert.Null(await Selection(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Dragged_nodes_can_be_pinned_and_released_with_a_double_click()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenSettings(page);
    await page.CheckAsync("#graph-setting-pinDragged");
    await page.ClickAsync("#graph-settings summary"); // close the panel

    await Drag(page, "#graph-svg g.node[data-id='Utils'] path.shape", -120, 80);
    await WaitUntilStill(page);
    Assert.Equal(1, await Count(page, "#graph-svg g.node.pinned[data-id='Utils']"));

    await page.DblClickAsync("#graph-svg g.node[data-id='Utils'] path.shape", new() { Force = true });
    Assert.Equal(0, await Count(page, "#graph-svg g.node.pinned"));
  }

  [Fact]
  public async Task Dragging_a_module_outline_moves_all_its_members()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    await OpenSettings(page);
    await page.UncheckAsync("#graph-setting-live"); // move only what's dragged, so the shift is exact
    await page.ClickAsync("#graph-settings summary");
    var before = await Position(page, "Logger.LogInfo");
    var other = await Position(page, "Logger.WriteLine");

    // A point where the outline itself, not a member or name on top of it, is under the mouse.
    var point = await page.EvaluateAsync<float[]>(@"() => {
            const hull = document.querySelector(""#graph-svg g.hull-group[data-module='Logger'] path.hull"");
            const r = hull.getBoundingClientRect();
            for (let y = r.top + 2; y < r.bottom; y += 3)
                for (let x = r.left + 2; x < r.right; x += 3)
                    if (document.elementFromPoint(x, y) === hull) return [x, y];
            return null;
        }");
    await DragFrom(page, point[0], point[1], 100, 60);

    var after = await Position(page, "Logger.LogInfo");
    var otherAfter = await Position(page, "Logger.WriteLine");
    Assert.True(after[0] - before[0] > 5);
    Assert.Equal(after[0] - before[0], otherAfter[0] - other[0], 3);
    Assert.Equal(after[1] - before[1], otherAfter[1] - other[1], 3);
  }

  [Fact]
  public async Task Layout_settings_change_the_graph_live_and_names_still_never_overlap()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    var height = "() => document.querySelector('#graph-svg .viewport').getBBox().height";
    var before = await page.EvaluateAsync<double>(height);

    await OpenSettings(page);
    await page.Locator("#graph-setting-memberSpacing").FillAsync("40");
    await page.Locator("#graph-setting-labelSize").FillAsync("14");
    await WaitUntilStill(page);

    Assert.Contains("40px", await page.InnerTextAsync("#graph-settings-menu"));
    Assert.True(await page.EvaluateAsync<double>(height) > before);
    Assert.Empty(await OverlappingLabels(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_member_node_shows_its_own_source_and_maps_to_its_module_at_module_level()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);

    await page.ClickAsync("#graph-svg g.node[data-id='Order.Describe']", new() { Force = true });

    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
    Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='Utils.FormatMoney']"));

    await page.ClickAsync("[data-level='modules']");
    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='Order']"));
    Assert.Equal("Order.Describe", await Selection(page)); // the selection itself is unchanged
  }

  [Fact]
  public async Task A_module_selected_at_member_level_highlights_its_whole_cluster()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);

    await opened.SelectAsync("Logger");

    Assert.Equal(3, await Count(page, "#graph-svg g.node.selected")); // LogInfo, LogError, WriteLine
    Assert.Equal(0, await Count(page, "#graph-svg g.hull-group.dim[data-module='Logger']"));
  }

  [Fact]
  public async Task The_selection_survives_switching_tabs()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='OrderService']");

    await page.ClickAsync("#tab-classes");
    await page.ClickAsync("#tab-graph");

    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='OrderService']"));
  }

  static Task<string[]> DrawnIds(IPage page, string selector = "#graph-svg g.node") =>
      page.Locator(selector).EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)");

  [Fact]
  public async Task Show_hides_member_kinds_private_members_or_filters_by_references()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);
    var before = await Count(page, "#graph-svg g.node");

    await page.ClickAsync("#graph-filters summary");
    await page.UncheckAsync("input[data-category='data']");

    Assert.Equal(0, await Count(page, "#graph-svg g.node[data-id='Order.m_Id']"));
    Assert.True(await Count(page, "#graph-svg g.node") < before);
    Assert.Equal("Show… (1)", await page.InnerTextAsync("#graph-filters summary"));

    await page.CheckAsync("input[data-category='data']");
    await page.CheckAsync("input[name='graph-references'][value='unreferenced']");
    Assert.Equal(new[] { "Utils.UnusedHelper" }, await DrawnIds(page));

    await page.CheckAsync("input[name='graph-references'][value='referenced']");
    var referenced = await DrawnIds(page);
    Assert.DoesNotContain("Utils.UnusedHelper", referenced);
    Assert.Contains("AppInit.LoadSettings", referenced);

    await page.CheckAsync("input[name='graph-references'][value='all']");
    await page.UncheckAsync("#graph-show-private");
    var publicOnly = await DrawnIds(page);
    Assert.DoesNotContain("AppInit.LoadSettings", publicOnly); // Private Sub
    Assert.DoesNotContain("Order.m_Id", publicOnly);           // module-level Private variable
    Assert.Contains("AppInit.Startup", publicOnly);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Show_hides_module_kinds_edge_kinds_and_unconnected_nodes_at_both_levels()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-filters summary");
    Assert.True(await page.IsHiddenAsync("#graph-show-private")); // member-level choices are hidden at module level

    await page.UncheckAsync("input[data-module-kind='document']");
    var modules = await DrawnIds(page);
    Assert.DoesNotContain("ThisWorkbook", modules);
    Assert.DoesNotContain("Sheet1", modules);
    Assert.Contains("Main", modules);

    await page.CheckAsync("input[data-module-kind='document']");
    await page.UncheckAsync("input[data-edge-kind='uses']");
    Assert.Equal(0, await Count(page, "#graph-svg path.edge.uses"));
    Assert.Equal(2, await Count(page, "#graph-svg path.edge.implements"));
    Assert.Equal(12, await Count(page, "#graph-svg g.node")); // unconnected nodes are still shown...

    await page.UncheckAsync("#graph-show-unconnected");    // ...until this is unticked
    Assert.Equal(new[] { "CsvOrderRepository", "IOrderRepository", "OrderService", "ProgressReporter", "SheetOrderRepository" },
        (await DrawnIds(page)).Order());

    await ShowMembers(page);
    Assert.Equal(0, await Count(page, "#graph-svg path.edge.uses"));
    Assert.Equal(0, await Count(page, "#graph-svg g.node[data-id='Utils.UnusedHelper']"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node[data-id='IOrderRepository.Save']"));
  }

  [Fact]
  public async Task Show_choices_are_remembered_and_Show_everything_resets_them()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-filters summary");
    await page.UncheckAsync("input[data-module-kind='document']");
    await page.UncheckAsync("input[data-edge-kind='events']");

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");

    Assert.Equal("Show… (2)", await page.InnerTextAsync("#graph-filters summary"));
    Assert.Equal(10, await Count(page, "#graph-svg g.node"));
    Assert.Equal(0, await Count(page, "#graph-svg path.edge.events"));

    await page.ClickAsync("#graph-filters summary");
    Assert.False(await page.IsCheckedAsync("input[data-module-kind='document']"));
    await page.ClickAsync("#graph-show-everything");
    Assert.Equal("Show…", await page.InnerTextAsync("#graph-filters summary"));
    Assert.Equal(12, await Count(page, "#graph-svg g.node"));
    Assert.True(await page.IsCheckedAsync("input[data-module-kind='document']"));
  }

  [Fact]
  public async Task The_Show_menu_uses_two_columns_at_member_level_unless_they_would_not_fit()
  {
    // The widest source panel leaves the graph too narrow for two columns.
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded,
        initScript: "localStorage.setItem('vbadoc-source-size', '0.7');");
    var page = opened.Page;
    await ShowMembers(page);
    const string stacked = "() => document.getElementById('graph-filter-menu').classList.contains('stacked')";
    const string withinView = @"() => document.getElementById('graph-filter-menu').getBoundingClientRect().right
            <= document.getElementById('view-graph').getBoundingClientRect().right";

    await page.ClickAsync("#graph-filters summary");
    Assert.True(await page.EvaluateAsync<bool>(stacked));
    Assert.True(await page.EvaluateAsync<bool>(withinView));

    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#source-toggle");
    await page.ClickAsync("#graph-filters summary");
    Assert.False(await page.EvaluateAsync<bool>(stacked));
    Assert.True(await page.EvaluateAsync<bool>(withinView));
    Assert.True(await page.IsVisibleAsync("#graph-show-private"));
  }

  [Fact]
  public async Task Clicking_a_legend_item_highlights_matching_nodes()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#graph-legend [data-legend='module:interface']");

    Assert.Equal("true", await page.GetAttributeAsync("#graph-legend [data-legend='module:interface']", "aria-pressed"));
    Assert.Equal(new[] { "IOrderRepository" }, await DrawnIds(page, "#graph-svg g.node:not(.dim)"));
    Assert.Equal(29, await Count(page, "#graph-svg path.edge.dim")); // no edge has both ends lit
    Assert.Null(await Selection(page));                               // the legend doesn't select anything

    // Items add to each other.
    await page.ClickAsync("#graph-legend [data-legend='module:document']");
    Assert.Equal(new[] { "IOrderRepository", "Sheet1", "ThisWorkbook" }, (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());

    // Clicking again turns an item off; Clear turns them all off.
    await page.ClickAsync("#graph-legend [data-legend='module:interface']");
    Assert.Equal(new[] { "Sheet1", "ThisWorkbook" }, (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());
    await page.ClickAsync("#graph-legend-clear");
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
    Assert.True(await page.IsHiddenAsync("#graph-legend-clear"));
  }

  [Fact]
  public async Task An_edge_legend_item_highlights_those_edges_and_the_nodes_they_connect()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#graph-legend [data-legend='edge:implements']");

    Assert.Equal(2, await Count(page, "#graph-svg path.edge.highlighted"));
    Assert.Equal(2, await Count(page, "#graph-svg path.edge.implements.highlighted"));
    Assert.Equal(new[] { "CsvOrderRepository", "IOrderRepository", "SheetOrderRepository" },
        (await DrawnIds(page, "#graph-svg g.node.related")).Order());
    Assert.Equal(9, await Count(page, "#graph-svg g.node.dim"));
  }

  [Fact]
  public async Task Legend_and_selection_narrow_each_other()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='CsvOrderRepository']");
    var neighbours = await Count(page, "#graph-svg g.node.related");
    Assert.True(neighbours > 1);

    await page.ClickAsync("#graph-legend [data-legend='edge:implements']");

    // Only the selection's Implements link is left, and the selection itself stays lit.
    Assert.Equal(new[] { "CsvOrderRepository" }, await DrawnIds(page, "#graph-svg g.node.selected"));
    Assert.Equal(new[] { "IOrderRepository" }, await DrawnIds(page, "#graph-svg g.node.related"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.highlighted[data-from='CsvOrderRepository'][data-to='IOrderRepository']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.highlighted"));
  }

  [Fact]
  public async Task Member_level_legend_items_highlight_member_kinds_and_unreferenced_members()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await ShowMembers(page);

    await page.ClickAsync("#graph-legend [data-legend='unreferenced']");
    Assert.Equal(new[] { "Utils.UnusedHelper" }, await DrawnIds(page, "#graph-svg g.node.related"));
    Assert.Equal(1, await Count(page, "#graph-svg g.hull-group:not(.dim)")); // only Utils' outline stays lit

    await page.ClickAsync("#graph-legend [data-legend='unreferenced']");
    await page.ClickAsync("#graph-legend [data-legend='category:property']");
    var lit = await DrawnIds(page, "#graph-svg g.node.related");
    Assert.Contains("Order.Status[Let]", lit);
    Assert.DoesNotContain("Main.Factorial", lit);

    // A selected module selects all its members, and the legend narrows those too.
    await page.ClickAsync("#graph-legend [data-legend='category:property']");
    await page.ClickAsync("#graph-legend [data-legend='category:procedure']");
    await opened.SelectAsync("Order");
    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='Order.Load']"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Order.m_Id']"));
    Assert.Equal(0, await Count(page, "#graph-svg g.hull-group.dim[data-module='Order']"));

    // Switching level clears the legend, whose items differ between levels.
    await page.ClickAsync("[data-level='modules']");
    Assert.Equal(0, await Count(page, "#graph-legend [aria-pressed='true']"));
    Assert.Equal(7, await Count(page, "#graph-svg g.node:not(.dim)")); // just Order and its 6 neighbours
  }

  [Fact]
  public async Task The_background_or_Escape_clears_the_legend_and_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-legend [data-legend='edge:events']");

    await page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
    Assert.Equal("false", await page.GetAttributeAsync("#graph-legend [data-legend='edge:events']", "aria-pressed"));

    await page.ClickAsync("#graph-svg g.node[data-id='Main']");
    await page.ClickAsync("#graph-legend [data-legend='edge:uses']");
    await page.ClickAsync("#graph-filters summary");
    await page.Keyboard.PressAsync("Escape"); // closes the menu only
    Assert.Equal("Main", await Selection(page));

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await Selection(page));
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
  }

  [Fact]
  public async Task Focus_shows_only_the_selection_and_its_neighbours_until_Show_all()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.True(await page.IsDisabledAsync("#graph-focus"));
    await page.ClickAsync("#graph-svg g.node[data-id='Utils']");

    await page.ClickAsync("#graph-focus");

    // Utils uses Logger; Order uses Utils.
    Assert.Equal(new[] { "Logger", "Order", "Utils" },
        (await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)")).Order());
    Assert.Equal("Show all", await page.InnerTextAsync("#graph-focus"));

    await page.ClickAsync("#graph-focus");
    Assert.Equal(12, await Count(page, "#graph-svg g.node"));
  }

  [Fact]
  public async Task Zooms_and_fits()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var transform = "() => document.querySelector('#graph-svg .viewport').getAttribute('transform')";
    var fitted = await page.EvaluateAsync<string>(transform);

    await page.Mouse.MoveAsync(400, 400);
    await page.Mouse.WheelAsync(0, -400);
    await page.WaitForFunctionAsync($"t => ({transform})() !== t", fitted);

    await page.ClickAsync("#graph-fit");
    await page.WaitForFunctionAsync($"t => ({transform})() === t", fitted);
  }

  [Fact]
  public async Task Node_colours_follow_the_theme()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var fill = "() => getComputedStyle(document.querySelector(\"#graph-svg g.node[data-id='Main'] .shape\")).fill";
    var dark = await page.EvaluateAsync<string>(fill);

    await page.ClickAsync("#theme-toggle");

    Assert.NotEqual(dark, await page.EvaluateAsync<string>(fill));
  }

  [Fact]
  public async Task A_large_project_renders_both_levels_in_reasonable_time()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.LargeEmbedded);
    var page = opened.Page;
    Assert.Equal(300, await Count(page, "#graph-svg g.node"));

    var timer = Stopwatch.StartNew();
    await ShowMembers(page);
    timer.Stop();

    Assert.True(await Count(page, "#graph-svg g.node") > 2600);
    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Member level took {timer.Elapsed.TotalSeconds:F1}s");
    Assert.Empty(await OverlappingLabels(page));
    await opened.SelectAsync("Mod042.P3");
    Assert.True(await Count(page, "#graph-svg g.node.related") >= 4);
    Assert.Empty(opened.Errors);
  }
}
