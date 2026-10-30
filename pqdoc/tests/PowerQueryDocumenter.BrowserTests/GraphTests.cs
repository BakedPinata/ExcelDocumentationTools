using System.Diagnostics;
using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The dependency graph view, on the sample queries: Source, fnCleanText and Sales Region Map feed Orders; Source
/// also feeds Customers; Orders feeds Q1.Summary and Sales Report, which also takes Customers and Q1.Summary; Sales
/// Report feeds Revenue &amp; Margin (2024). Cycle A and Cycle B use each other, and Unused Parameter stands alone.
/// </summary>
[Collection("Browser")]
public sealed class GraphTests(PageFixture fixture)
{
  private const string Revenue = "Revenue & Margin (2024)";

  private static Task<int> Count(IPage page, string selector) => page.Locator(selector).CountAsync();

  private static Task<double[]> Position(IPage page, string id) =>
    page.EvaluateAsync<double[]>("id => { const p = window.pqdocGraph.position(id); return [p.x, p.y, p.column]; }", id);

  private static Task WaitUntilStill(IPage page) =>
    page.WaitForFunctionAsync("() => !window.pqdocGraph.moving()", null, new() { Timeout = 20000 });

  private static Task ClickBackground(IPage page) =>
    page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");

  /// <summary>Drags a query by (dx, dy) screen pixels in small steps, like a person would.</summary>
  private static async Task Drag(IPage page, ILocator target, float dx, float dy)
  {
    var box = (await target.BoundingBoxAsync())!;
    var x = box.X + box.Width / 2;
    var y = box.Y + box.Height / 2;
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (var i = 1; i <= 10; i++)
    {
      await page.Mouse.MoveAsync(x + dx * i / 10, y + dy * i / 10);
    }

    await page.Mouse.UpAsync();
  }

  [Fact]
  public async Task Draws_every_query_and_dependency_styled_by_role()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    Assert.Equal(11, await Count(page, "#graph-svg g.node"));
    Assert.Equal(11, await Count(page, "#graph-svg path.edge"));
    Assert.Equal(["fnCleanText", "Sales Region Map", "Source"], await opened.DrawnIdsAsync("#graph-svg g.node.root"));
    Assert.Equal([Revenue], await opened.DrawnIdsAsync("#graph-svg g.node.leaf"));
    Assert.Equal(["Unused Parameter"], await opened.DrawnIdsAsync("#graph-svg g.node.isolated"));
    Assert.Equal(6, await Count(page, "#graph-svg g.node.intermediate"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Circular_dependencies_are_flagged()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    Assert.Equal(2, await Count(page, "#graph-svg g.node .ring"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node[data-id='Cycle A'] .ring"));
    Assert.Equal(2, await Count(page, "#graph-svg path.edge.cycle"));
    Assert.Equal("url(#arrow-cycle)", await page.GetAttributeAsync("#graph-svg path.edge.cycle", "marker-end"));
  }

  [Fact]
  public async Task Data_flows_left_to_right_in_columns_by_depth()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    Assert.Equal(-1, (await Position(page, "Unused Parameter"))[2]);
    Assert.Equal(0, (await Position(page, "Source"))[2]);
    Assert.Equal(1, (await Position(page, "Orders"))[2]);
    Assert.Equal(2, (await Position(page, "Q1.Summary"))[2]);
    Assert.Equal(3, (await Position(page, "Sales Report"))[2]); // its longest chain, not its shortest
    Assert.Equal(4, (await Position(page, Revenue))[2]);

    var edges = await page.Locator("#graph-svg path.edge:not(.cycle)")
      .EvaluateAllAsync<string[][]>("es => es.map(e => [e.dataset.from, e.dataset.to])");
    foreach (var edge in edges)
    {
      Assert.True((await Position(page, edge[0]))[0] < (await Position(page, edge[1]))[0], $"{edge[0]} isn't left of {edge[1]}");
    }
  }

  [Fact]
  public async Task The_queries_of_a_circular_dependency_share_a_column()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    Assert.Equal((await Position(opened.Page, "Cycle A"))[2], (await Position(opened.Page, "Cycle B"))[2]);
  }

  [Fact]
  public async Task The_same_queries_give_the_same_first_picture()
  {
    await using var first = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    await using var second = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    Assert.Equal(await Position(first.Page, "Orders"), await Position(second.Page, "Orders"));
  }

  [Fact]
  public async Task The_legend_explains_every_role_and_both_traces()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var legend = await opened.Page.InnerTextAsync("#graph-legend");

    foreach (var text in new[] { "Root", "Leaf", "Isolated", "Part of a circular dependency", "Feeds the selected query", "Fed by the selected query" })
    {
      Assert.Contains(text, legend);
    }
  }

  [Fact]
  public async Task Clicking_a_query_traces_everything_upstream_and_downstream_of_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await (await opened.NodeAsync("Orders")).ClickAsync();

    Assert.Equal("Orders", await opened.SelectionAsync());
    Assert.Equal(["Orders"], await opened.DrawnIdsAsync("#graph-svg g.node.selected"));
    Assert.Equal(
      ["fnCleanText", "Q1.Summary", Revenue, "Sales Region Map", "Sales Report", "Source"],
      await opened.DrawnIdsAsync("#graph-svg g.node.related"));
    Assert.Equal(
      ["Customers", "Cycle A", "Cycle B", "Unused Parameter"],
      await opened.DrawnIdsAsync("#graph-svg g.node.dim"));
    Assert.Equal(3, await Count(page, "#graph-svg path.edge.upstream"));
    Assert.Equal(4, await Count(page, "#graph-svg path.edge.downstream"));
    Assert.Equal("url(#arrow-upstream)", await page.GetAttributeAsync("#graph-svg path.edge.upstream", "marker-end"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.upstream[data-from='Source'][data-to='Orders']"));
    Assert.Equal(1, await Count(page, "#graph-svg path.edge.dim[data-from='Source'][data-to='Customers']"));
  }

  [Fact]
  public async Task A_query_in_a_cycle_traces_round_it_without_counting_itself()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    await (await opened.NodeAsync("Cycle A")).ClickAsync();

    Assert.Equal(["Cycle B"], await opened.DrawnIdsAsync("#graph-svg g.node.related"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Clicking_the_background_or_the_query_again_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    var orders = await opened.NodeAsync("Orders");

    await orders.ClickAsync();
    await page.Mouse.MoveAsync(0, 0); // stop hovering, which lights its neighbours
    await ClickBackground(page);
    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));

    await orders.ClickAsync();
    await orders.ClickAsync();
    await page.Mouse.MoveAsync(0, 0);
    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
  }

  [Fact]
  public async Task Escape_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    await opened.SelectAsync("Orders");

    await opened.Page.Keyboard.PressAsync("Escape");

    Assert.Null(await opened.SelectionAsync());
  }

  [Fact]
  public async Task Hovering_a_query_shows_its_counts_and_lights_its_direct_neighbours_only()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await (await opened.NodeAsync("Q1.Summary")).HoverAsync();

    Assert.Contains("Q1.Summary", await page.InnerTextAsync("#graph-tooltip"));
    Assert.Contains("Uses 1 query, used by 1 query", await page.InnerTextAsync("#graph-tooltip"));
    Assert.Equal(["Orders", "Q1.Summary", "Sales Report"], await opened.DrawnIdsAsync("#graph-svg g.node.related"));
    Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Source']"));

    await page.Mouse.MoveAsync(0, 0);
    Assert.True(await page.IsHiddenAsync("#graph-tooltip"));
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
  }

  [Fact]
  public async Task The_tooltip_says_when_a_query_is_in_a_circular_dependency()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    await (await opened.NodeAsync("Cycle B")).HoverAsync();

    Assert.Contains("Part of a circular dependency", await opened.Page.InnerTextAsync("#graph-tooltip"));
  }

  [Fact]
  public async Task Queries_work_from_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    var orders = await opened.NodeAsync("Orders");

    Assert.Equal("Orders: uses 3 queries, used by 2 queries", await orders.GetAttributeAsync("aria-label"));

    await orders.FocusAsync();
    Assert.Contains("Orders", await page.InnerTextAsync("#graph-tooltip"));

    await page.Keyboard.PressAsync("Enter");
    Assert.Equal("Orders", await opened.SelectionAsync());
  }

  [Fact]
  public async Task Dragging_a_query_pins_it_and_a_double_click_lets_it_go()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    var target = await opened.NodeAsync("Customers");
    var before = await Position(page, "Customers");
    var neighbour = await Position(page, "Sales Report");

    await Drag(page, target.Locator(".shape"), 60, 90);
    await WaitUntilStill(page);

    var after = await Position(page, "Customers");
    Assert.True(Math.Abs(after[1] - before[1]) > 20, $"Customers didn't move: {before[1]} -> {after[1]}");
    Assert.Equal(1, await Count(page, "#graph-svg g.node.pinned"));
    Assert.Equal(["Customers"], await opened.DrawnIdsAsync("#graph-svg g.node.pinned"));
    Assert.NotEqual(neighbour, await Position(page, "Sales Report")); // the rest rearranges around it
    Assert.Null(await opened.SelectionAsync());                       // a drag isn't a click

    await target.Locator(".shape").DblClickAsync(new() { Force = true });
    Assert.Equal(0, await Count(page, "#graph-svg g.node.pinned"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Clicking_a_query_does_not_pin_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    var box = (await (await opened.NodeAsync("Orders")).Locator(".shape").BoundingBoxAsync())!;
    var x = box.X + box.Width / 2;
    var y = box.Y + box.Height / 2;

    // A hand that shakes a little while clicking.
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(x + 2, y + 1);
    await page.Mouse.UpAsync();

    Assert.Equal("Orders", await opened.SelectionAsync());
    Assert.Equal(0, await Count(page, "#graph-svg g.node.pinned"));
    Assert.False(await page.EvaluateAsync<bool>("() => window.pqdocGraph.moving()"));
  }

  [Fact]
  public async Task Search_lights_up_the_matches_and_says_how_many()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await page.FillAsync("#search", "SALES");

    Assert.Equal(["Sales Region Map", "Sales Report"], await opened.DrawnIdsAsync("#graph-svg g.node.match"));
    Assert.Equal(9, await Count(page, "#graph-svg g.node.dim"));
    Assert.Equal("2 matches", await page.InnerTextAsync("#search-status"));

    await page.FillAsync("#search", "q1.");
    Assert.Equal("1 match", await page.InnerTextAsync("#search-status"));

    await page.FillAsync("#search", "nothing like this");
    Assert.Equal("No query matches", await page.InnerTextAsync("#search-status"));
    Assert.Equal(0, await Count(page, "#graph-svg g.node.match"));
    Assert.Equal(11, await Count(page, "#graph-svg g.node.dim"));
  }

  [Fact]
  public async Task Enter_selects_the_first_match_by_name_and_centres_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await page.FillAsync("#search", "sales");

    await page.PressAsync("#search", "Enter");

    Assert.Equal("Sales Region Map", await opened.SelectionAsync());
    await page.WaitForFunctionAsync(
      @"() => {
          const view = document.getElementById('view-graph').getBoundingClientRect();
          const shape = document.querySelector(""#graph-svg g.node[data-id='Sales Region Map'] .shape"").getBoundingClientRect();
          return Math.abs(shape.left + shape.width / 2 - (view.left + view.width / 2)) < 2;
        }");
  }

  [Fact]
  public async Task Slash_jumps_to_the_search_box_and_Escape_empties_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    await page.Keyboard.PressAsync("/");
    Assert.Equal("search", await page.EvaluateAsync<string>("() => document.activeElement.id"));
    Assert.Equal(string.Empty, await page.InputValueAsync("#search")); // the slash isn't typed into it

    await page.Keyboard.TypeAsync("a/b");
    Assert.Equal("a/b", await page.InputValueAsync("#search")); // once there, it's an ordinary character

    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(string.Empty, await page.InputValueAsync("#search"));
    Assert.True(await page.IsHiddenAsync("#search-status"));
    Assert.Equal("Orders", await opened.SelectionAsync()); // only the box was emptied
  }

  [Fact]
  public async Task Zooms_with_the_wheel_and_Reset_view_puts_everything_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    const string transform = "() => document.querySelector('#graph-svg .viewport').getAttribute('transform')";
    var fitted = await page.EvaluateAsync<string>(transform);

    await Drag(page, (await opened.NodeAsync("Customers")).Locator(".shape"), 60, 90);
    await WaitUntilStill(page);
    await page.FillAsync("#search", "sales");
    await opened.SelectAsync("Orders");
    await page.Mouse.MoveAsync(300, 400);
    await page.Mouse.WheelAsync(0, -400);
    await page.WaitForFunctionAsync($"t => ({transform})() !== t", fitted);

    await page.ClickAsync("#graph-reset");
    await WaitUntilStill(page);

    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(string.Empty, await page.InputValueAsync("#search"));
    Assert.Equal(0, await Count(page, "#graph-svg g.node.pinned"));
    Assert.Equal(0, await Count(page, "#graph-svg .dim"));
    await AssertGraphInsideView(page);
    Assert.Empty(opened.Errors);
  }

  private static async Task AssertGraphInsideView(IPage page)
  {
    const string outside = @"() => {
        const view = document.getElementById('view-graph').getBoundingClientRect();
        return [...document.querySelectorAll('#graph-svg g.node .shape')]
          .map(s => s.getBoundingClientRect())
          .filter(r => r.left < view.left || r.right > view.right || r.top < view.top || r.bottom > view.bottom).length;
      }";
    await page.WaitForFunctionAsync($"() => ({outside})() === 0");
  }

  [Fact]
  public async Task The_graph_fits_between_the_toolbar_and_the_legend()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    var clear = await opened.Page.EvaluateAsync<bool>(
      @"() => {
          const toolbar = document.querySelector('.view-toolbar').getBoundingClientRect();
          const legend = document.getElementById('graph-legend').getBoundingClientRect();
          return [...document.querySelectorAll('#graph-svg g.node .shape')]
            .map(s => s.getBoundingClientRect())
            .every(r => r.top >= toolbar.bottom && r.bottom <= legend.top);
        }");

    Assert.True(clear);
  }

  [Fact]
  public async Task Query_names_never_overlap()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    var overlaps = await opened.Page.EvaluateAsync<string[]>(
      @"() => {
          const boxes = [...document.querySelectorAll('#graph-svg g.node')].map(n => ({ id: n.dataset.id, r: n.querySelector('text.label').getBoundingClientRect() }));
          const found = [];
          for (let i = 0; i < boxes.length; i++)
            for (let j = i + 1; j < boxes.length; j++) {
              const a = boxes[i].r, b = boxes[j].r;
              if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) found.push(boxes[i].id + ' / ' + boxes[j].id);
            }
          return found;
        }");

    Assert.Empty(overlaps);
  }

  [Fact]
  public async Task Query_colours_follow_the_theme()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    const string fill = "() => getComputedStyle(document.querySelector(\"#graph-svg g.node[data-id='Source'] .shape\")).fill";
    var dark = await page.EvaluateAsync<string>(fill);

    await page.ClickAsync("#theme-toggle");

    Assert.NotEqual(dark, await page.EvaluateAsync<string>(fill));
  }

  [Fact]
  public async Task Each_role_has_a_different_colour_in_both_themes()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    const string colours = @"() => ['root', 'leaf', 'intermediate'].map(role => getComputedStyle(document.querySelector(`#graph-svg g.node.${role} .shape`)).fill)
      .concat(getComputedStyle(document.querySelector('#graph-svg g.node .ring')).stroke)";

    Assert.Equal(4, (await page.EvaluateAsync<string[]>(colours)).Distinct().Count());

    await page.ClickAsync("#theme-toggle");
    Assert.Equal(4, (await page.EvaluateAsync<string[]>(colours)).Distinct().Count());
  }

  [Fact]
  public async Task Names_are_shown_as_text_however_awkward()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.AwkwardPage);
    var page = opened.Page;

    Assert.Equal("R&D <draft>", await page.InnerTextAsync("#page-title"));
    Assert.Equal(9, await Count(page, "#graph-svg g.node"));
    Assert.Equal(0, await Count(page, "img"));

    await opened.SelectAsync(PageFixture.HostileQuery);
    Assert.Equal(PageFixture.HostileQuery, await page.InnerTextAsync("#details-header h2"));
    Assert.Equal(0, await Count(page, "img"));

    await opened.SelectAsync("__proto__");
    Assert.Equal("__proto__", await page.InnerTextAsync("#details-header h2"));

    // Only two queries that use each other get curved arrows, however their names read when joined up.
    Assert.Equal(0, await Count(page, "#graph-svg path.edge[d*='Q']"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_long_name_is_cut_short_in_the_graph_and_shown_in_full_in_the_tooltip()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.AwkwardPage);
    var target = await opened.NodeAsync(PageFixture.LongQuery);

    var label = await target.Locator("text.label").TextContentAsync();
    Assert.EndsWith("…", label);
    Assert.Equal(28, label!.Length);

    await target.HoverAsync();
    Assert.Contains(PageFixture.LongQuery, await opened.Page.InnerTextAsync("#graph-tooltip"));
  }

  [Fact]
  public async Task A_large_model_renders_in_reasonable_time()
  {
    var timer = Stopwatch.StartNew();
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.LargePage);
    timer.Stop();
    var page = opened.Page;

    Assert.Equal(300, await Count(page, "#graph-svg g.node"));
    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Opening the page took {timer.Elapsed.TotalSeconds:F1}s");

    await opened.SelectAsync("Q042");
    Assert.True(await Count(page, "#graph-svg g.node.related") >= 8);
    Assert.Empty(opened.Errors);
  }
}
