using System.Diagnostics;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The class diagram view, on SampleVba (see ClassDiagramTests in the unit tests for its relationships).</summary>
[Collection("Browser")]
public class ClassDiagramTests(PageFixture fixture)
{
  static Task<int> Count(IPage page, string selector) => page.Locator(selector).CountAsync();

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static string Box(string module) => $"#classes-svg g.class-box[data-module='{module}']";

  static string Line(string member) => $"#classes-svg g.class-line[data-member='{member}']";

  const string Edges = "#classes-svg path.class-edge";

  /// <summary>A box's text, one line per text element (InnerText doesn't work on SVG elements).</summary>
  static Task<string> BoxText(IPage page, string module) =>
      page.Locator(Box(module)).EvaluateAsync<string>("n => [...n.querySelectorAll('text')].map(t => t.textContent).join('\\n')");

  static Task<double[]> Position(IPage page, string module) =>
      page.EvaluateAsync<double[]>("id => { const p = window.vbadocClasses.position(id); return [p.x, p.y]; }", module);

  /// <summary>Clicks a spot of the diagram where nothing is drawn, searching up from its bottom right corner.</summary>
  static async Task ClickBackground(IPage page)
  {
    var point = await page.EvaluateAsync<float[]?>(@"() => {
            const svg = document.getElementById('classes-svg');
            const r = svg.getBoundingClientRect();
            for (let y = r.bottom - 20; y > r.top + 60; y -= 10)
                for (let x = r.right - 20; x > r.left + 20; x -= 10)
                    if (document.elementFromPoint(x, y) === svg) return [x, y];
            return null;
        }");
    Assert.NotNull(point);
    await page.Mouse.ClickAsync(point[0], point[1]);
  }

  static Task WaitUntilDrawn(IPage page, int timeoutMs = 30000) =>
      page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()", null, new() { Timeout = timeoutMs });

  static async Task<OpenedPage> OpenClasses(PageFixture fixture, string folder, int timeoutMs = 30000)
  {
    var opened = await OpenedPage.OpenAsync(fixture, folder);
    await opened.Page.ClickAsync("#tab-classes");
    await WaitUntilDrawn(opened.Page, timeoutMs);
    return opened;
  }

  /// <summary>
  /// Brings a box into view as a person would, by selecting it, then clears the selection. The test window is
  /// small, with the source panel open, so the sample's diagram doesn't all fit at the starting zoom.
  /// </summary>
  static async Task Reveal(OpenedPage opened, string module)
  {
    await opened.SelectAsync(module);
    await opened.Page.WaitForFunctionAsync(@"id => {
            const view = document.getElementById('classes-svg').getBoundingClientRect();
            const legend = document.getElementById('classes-legend').getBoundingClientRect();
            const box = document.querySelector(`#classes-svg g.class-box[data-module='${id}']`).getBoundingClientRect();
            return !window.vbadocClasses.moving() &&
                box.left >= view.left && box.right <= view.right && box.top >= view.top && box.bottom <= legend.top;
        }", module);
    await opened.Page.EvaluateAsync("() => window.vbadoc.select(null)");
  }

  static Task<string[]> ShownModules(IPage page) =>
      page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");

  /// <summary>Drags from the middle of an element by (dx, dy) screen pixels in small steps, like a person would.</summary>
  static async Task Drag(IPage page, string selector, float dx, float dy)
  {
    var box = (await page.Locator(selector).BoundingBoxAsync())!;
    float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 10; i++) await page.Mouse.MoveAsync(x + dx * i / 10, y + dy * i / 10);
    await page.Mouse.UpAsync();
  }

  [Fact]
  public async Task Draws_one_box_per_module_with_its_stereotype_and_members()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    Assert.Equal(12, await Count(page, "#classes-svg g.class-box"));
    Assert.Contains("«interface»", await BoxText(page, "IOrderRepository"));
    Assert.Contains("«module»", await BoxText(page, "Main"));
    Assert.Contains("«document»", await BoxText(page, "Sheet1"));
    var order = await BoxText(page, "Order");
    Assert.Contains("«class»", order);
    Assert.Contains("-m_Id : String", order);
    Assert.Contains("+Property Status : String «Get/Let»", order);
    Assert.Contains("+Describe() : String", order);
    Assert.Contains("+Event Progress(message As String)", await BoxText(page, "OrderService"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Draws_every_relationship_with_association_labels()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    // 2 realisations, 2 associations and 24 dependencies (checked in the unit tests).
    Assert.Equal(28, await Count(page, Edges));
    Assert.Equal(2, await Count(page, $"{Edges}.realization"));
    Assert.Equal(2, await Count(page, $"{Edges}.association"));
    Assert.Equal(24, await Count(page, $"{Edges}.dependency"));
    var labels = await page.Locator("#classes-svg text.class-edge-label").AllTextContentsAsync();
    Assert.Contains("WithEvents m_svc", labels);
    Assert.Contains("m_repo", labels);
  }

  [Fact]
  public async Task The_starting_layout_is_always_the_same_and_users_sit_above_what_they_use()
  {
    await using var first = await OpenClasses(fixture, fixture.SampleEmbedded);
    await using var second = await OpenClasses(fixture, fixture.SampleEmbedded);
    var positions = "() => [...document.querySelectorAll('#classes-svg g.class-box')].map(n => n.dataset.module + n.getAttribute('transform')).sort()";
    Assert.Equal(await first.Page.EvaluateAsync<string[]>(positions), await second.Page.EvaluateAsync<string[]>(positions));

    // Main uses OrderService, which uses Logger, which nothing below it uses.
    var main = await Position(first.Page, "Main");
    var service = await Position(first.Page, "OrderService");
    var logger = await Position(first.Page, "Logger");
    Assert.True(main[1] < service[1] && service[1] < logger[1], $"Main {main[1]}, OrderService {service[1]}, Logger {logger[1]}");

    // No two boxes overlap.
    Assert.Empty(await first.Page.EvaluateAsync<string[]>(Overlaps));
  }

  /// <summary>Pairs of boxes that overlap, as "A/B".</summary>
  const string Overlaps = @"() => {
        const boxes = [...document.querySelectorAll('#classes-svg g.class-box')].map(n => ({ id: n.dataset.module, r: n.getBBox(), p: window.vbadocClasses.position(n.dataset.module) }));
        const clashes = [];
        for (let i = 0; i < boxes.length; i++)
            for (let j = i + 1; j < boxes.length; j++) {
                const a = boxes[i], b = boxes[j];
                if (a.p.x < b.p.x + b.r.width && b.p.x < a.p.x + a.r.width && a.p.y < b.p.y + b.r.height && b.p.y < a.p.y + a.r.height)
                    clashes.push(a.id + '/' + b.id);
            }
        return clashes;
    }";

  [Fact]
  public async Task Clicking_a_box_selects_its_module_and_clicking_a_line_selects_its_member()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var before = await Position(page, "Order");
    await Reveal(opened, "Order");

    await page.ClickAsync($"{Box("Order")} .box-name");
    Assert.Equal("Order", await Selection(page));
    Assert.Equal(1, await Count(page, $"{Box("Order")}.selected"));

    await page.ClickAsync(Line("Order.Describe"));
    Assert.Equal("Order.Describe", await Selection(page));
    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
    Assert.Equal(1, await Count(page, $"{Line("Order.Describe")}.selected-line"));

    // A property's one line selects its first accessor, and stays highlighted for the others.
    await page.ClickAsync(Line("Order.Status[Get]"));
    Assert.Equal("Order.Status[Get]", await Selection(page));
    await opened.SelectAsync("Order.Status[Let]");
    Assert.Equal(1, await Count(page, $"{Line("Order.Status[Get]")}.selected-line"));

    await ClickBackground(page);
    Assert.Null(await Selection(page));
    Assert.Equal(0, await Count(page, "#classes-svg .selected, #classes-svg .selected-line"));

    // Clicks never move a box.
    Assert.Equal(before, await Position(page, "Order"));
  }

  [Fact]
  public async Task Dragging_a_box_moves_it_with_its_edges_and_is_remembered_until_the_layout_is_run_again()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await Reveal(opened, "Logger");
    var start = await Position(page, "Logger");
    var edge = $"{Edges}[data-to='Logger']";
    var path = await page.GetAttributeAsync(edge, "d");
    var zoom = await page.EvaluateAsync<double>("() => window.vbadocClasses.zoom()");

    await Drag(page, $"{Box("Logger")} .box-name", 150, -60);

    var moved = await Position(page, "Logger"); // rounded to whole units when dropped
    Assert.InRange(moved[0], start[0] + 150 / zoom - 1, start[0] + 150 / zoom + 1);
    Assert.InRange(moved[1], start[1] - 60 / zoom - 1, start[1] - 60 / zoom + 1);
    Assert.NotEqual(path, await page.GetAttributeAsync(edge, "d"));
    Assert.Null(await Selection(page)); // a drag isn't a click

    await page.ReloadAsync();
    await WaitUntilDrawn(page); // the class diagram tab is remembered
    Assert.Equal(moved, await Position(page, "Logger"));

    await page.ClickAsync("#classes-relayout");
    Assert.Equal(start, await Position(page, "Logger"));
    await page.ReloadAsync();
    await WaitUntilDrawn(page);
    Assert.Equal(start, await Position(page, "Logger"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_selection_is_shared_with_the_dependency_graph()
  {
    // Without the explorer, so OrderService is in view in the graph at the starting zoom.
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutExplorer);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='OrderService']");

    await page.ClickAsync("#tab-classes");
    await WaitUntilDrawn(page);
    Assert.Equal(1, await Count(page, $"{Box("OrderService")}.selected"));

    await Reveal(opened, "Logger");
    await page.ClickAsync(Line("Logger.LogInfo"));
    await page.ClickAsync("#tab-graph");
    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='Logger']"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Focus_on_click_shows_the_selected_module_and_its_relations_and_follows_the_selection()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.Equal("true", await page.GetAttributeAsync("#classes-focus", "aria-pressed"));

    await opened.SelectAsync("OrderService.Process");
    await WaitUntilDrawn(page);

    // OrderService uses IOrderRepository (m_repo), Logger and Order; Main and ProgressReporter (m_svc) use it.
    Assert.Equal(new[] { "IOrderRepository", "Logger", "Main", "Order", "OrderService", "ProgressReporter" }, await ShownModules(page));

    await opened.SelectAsync("Utils");
    await WaitUntilDrawn(page);
    Assert.Equal(new[] { "Logger", "Order", "Utils" }, await ShownModules(page)); // Utils uses Logger; Order uses Utils

    await page.ClickAsync("#classes-focus"); // focus on click off, which unfocuses the selection too
    await WaitUntilDrawn(page);
    Assert.Equal(12, (await ShownModules(page)).Length);
    Assert.Equal("false", await page.GetAttributeAsync("#classes-focus", "aria-pressed"));
    Assert.Equal("Utils", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
  }

  [Fact]
  public async Task Show_choices_and_the_Members_switch_hide_private_members_uses_or_members_and_are_remembered()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#explorer-show summary");

    await page.UncheckAsync("#explorer-show [data-choice='private']");
    Assert.Equal(0, await Count(page, Line("Order.m_Id")));
    Assert.Equal(1, await Count(page, Line("Order.Describe")));

    await page.UncheckAsync("#explorer-show [data-choice='line:uses']");
    Assert.Equal(4, await Count(page, Edges)); // realisations and associations
    await page.Keyboard.PressAsync("Escape");

    await page.ClickAsync("#classes-members");
    Assert.Equal("false", await page.GetAttributeAsync("#classes-members", "aria-pressed"));
    Assert.Equal(0, await Count(page, "#classes-svg g.class-line"));

    await page.ReloadAsync();
    await WaitUntilDrawn(page); // the class diagram tab is remembered too
    Assert.Equal(0, await Count(page, "#classes-svg g.class-line"));
    Assert.Equal(4, await Count(page, Edges));
    Assert.Empty(await page.EvaluateAsync<string[]>(Overlaps));
  }

  [Fact]
  public async Task Zooms_in_out_and_to_fit()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var before = await page.EvaluateAsync<double>("() => window.vbadocClasses.zoom()");

    await page.ClickAsync("#classes-zoom-in");
    await page.WaitForFunctionAsync("z => window.vbadocClasses.zoom() > z * 1.2", before);

    await page.ClickAsync("#classes-fit");
    await page.WaitForFunctionAsync(@"() => {
            const view = document.getElementById('classes-svg').getBoundingClientRect();
            const drawing = document.querySelector('#classes-svg g.boxes').getBoundingClientRect();
            return drawing.left >= view.left && drawing.right <= view.right && drawing.top >= view.top && drawing.bottom <= view.bottom;
        }");
  }

  [Fact]
  public async Task Switching_the_theme_recolours_the_diagram_without_redrawing_it()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var fill = $"() => getComputedStyle(document.querySelector(\"{Box("Main")} .box-outline\")).fill";
    var before = await page.EvaluateAsync<string>(fill);
    await page.EvaluateAsync($"() => document.querySelector(\"{Box("Main")}\").dataset.marker = 'kept'");

    await page.ClickAsync("#theme-toggle");

    Assert.NotEqual(before, await page.EvaluateAsync<string>(fill));
    Assert.Equal("kept", await page.GetAttributeAsync(Box("Main"), "data-marker"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Names_and_values_with_special_characters_are_shown_as_written()
  {
    await using var opened = await OpenClasses(fixture, fixture.AwkwardEmbedded);
    var page = opened.Page;
    var box = await BoxText(page, PageFixture.AwkwardModule);

    Assert.Contains(PageFixture.AwkwardModule, box);
    Assert.Contains("+Const BRACES : String = \"{a} }\"", box);
    Assert.Contains("+Const PAREN : String = \"a)b\"", box);
    Assert.Contains("+Const MARKDOWN : String = \"**x** __y__ [l](u) `c`\"", box);
    Assert.Contains("+Const ENTITY : String = \"#59; &amp; <b>bold</b> ~T~\"", box);
    Assert.Contains("+Const DOLLAR : String = \"cost$\"", box);
    Assert.Contains("+Größe(Optional s As String = \"(\", Optional n As Long = 5) : String", box);
    Assert.Equal(0, await Count(page, "#classes-svg b, #classes-svg strong, #classes-svg a"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_large_project_is_laid_out_quickly_with_every_relationship()
  {
    var timer = Stopwatch.StartNew();
    await using var opened = await OpenClasses(fixture, fixture.LargeEmbedded, timeoutMs: 60000);
    timer.Stop();
    var page = opened.Page;

    Assert.Equal(300, await Count(page, "#classes-svg g.class-box"));
    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), $"The class diagram took {timer.Elapsed.TotalSeconds:F1}s");
    Assert.True(await Count(page, Edges) > 300, "Dependencies are all drawn, however many there are");

    // Every box has a real position and size, and none overlaps another.
    Assert.True(await page.EvaluateAsync<bool>(@"() => [...document.querySelectorAll('#classes-svg g.class-box')].every(n => {
            const p = window.vbadocClasses.position(n.dataset.module), r = n.getBBox();
            return isFinite(p.x) && isFinite(p.y) && r.width > 0 && r.height > 0; })"));
    Assert.Empty(await page.EvaluateAsync<string[]>(Overlaps));

    // Selecting a box far away brings it into view.
    await opened.SelectAsync("Mod299");
    await page.WaitForFunctionAsync(@"() => {
            const c = document.getElementById('classes-svg').getBoundingClientRect();
            const r = document.querySelector(""#classes-svg g.class-box[data-module='Mod299']"").getBoundingClientRect();
            return r.right > c.left && r.left < c.right && r.bottom > c.top && r.top < c.bottom;
        }");
    Assert.Empty(opened.Errors);
  }

  static Task<string[]> LitBoxes(IPage page) =>
      page.Locator("#classes-svg g.class-box:not(.dim)").EvaluateAllAsync<string[]>("bs => bs.map(b => b.dataset.module).sort()");

  [Fact]
  public async Task Legend_items_light_up_what_they_stand_for_and_add_to_each_other()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#classes-legend [data-legend='box:interface']");
    Assert.Equal(["IOrderRepository"], await LitBoxes(page));
    Assert.Equal("true", await page.GetAttributeAsync("#classes-legend [data-legend='box:interface']", "aria-pressed"));

    // Implements adds its lines, and the boxes at their ends.
    await page.ClickAsync("#classes-legend [data-legend='edge:realization']");
    var implementers = await page.EvaluateAsync<string[]>(
        "() => [...new Set(window.vbadoc.data.classEdges.filter(e => e.kind === 'realization').flatMap(e => [e.from, e.to]))].sort()");
    Assert.Equal(implementers, await LitBoxes(page));
    Assert.Equal(0, await Count(page, "#classes-svg path.class-edge.realization.dim"));
    Assert.True(await Count(page, "#classes-svg path.class-edge.dependency.dim") > 0);

    await page.ClickAsync("#classes-legend-clear");
    Assert.Equal(0, await Count(page, "#classes-svg .dim"));
    Assert.True(await page.IsHiddenAsync("#classes-legend-clear"));
  }

  [Fact]
  public async Task The_background_or_Escape_turns_the_legend_off()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#classes-legend [data-legend='box:class']");
    Assert.True(await Count(page, "#classes-svg .dim") > 0);

    await ClickBackground(page);
    Assert.Equal(0, await Count(page, "#classes-svg .dim"));

    await page.ClickAsync("#classes-legend [data-legend='edge:association']");
    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(0, await Count(page, "#classes-svg .dim"));
  }
}
