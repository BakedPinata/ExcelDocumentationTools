using System.Diagnostics;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The class diagram view, on SampleVba (see ClassDiagramTests in the unit tests for its relationships).</summary>
[Collection("Browser")]
public class ClassDiagramTests(PageFixture fixture)
{
  static Task<int> Count(IPage page, string selector) => page.Locator(selector).CountAsync();

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static string Box(string module) => $"#classes-canvas g.node[data-module='{module}']";

  static string Line(string member) => $"#classes-canvas [data-member='{member}']";

  /// <summary>A box's text, one line per label (InnerText doesn't work on SVG elements).</summary>
  static Task<string> BoxText(IPage page, string module) =>
      page.Locator(Box(module)).EvaluateAsync<string>("n => [...n.querySelectorAll('p')].map(p => p.textContent).join('\\n')");

  /// <summary>Clicks an empty spot of the diagram's scrolling area, near its bottom right corner.</summary>
  static async Task ClickBackground(IPage page)
  {
    var area = (await page.Locator("#classes-canvas").BoundingBoxAsync())!;
    await page.Mouse.ClickAsync(area.X + area.Width - 30, area.Y + area.Height - 30);
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

  static Task<string[]> ShownModules(IPage page) =>
      page.Locator("#classes-canvas g.node[data-module]").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");

  [Fact]
  public async Task Draws_one_box_per_module_with_its_stereotype_and_members()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    Assert.Equal(12, await Count(page, "#classes-canvas g.node[data-module]"));
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
    var text = await page.EvaluateAsync<string>("() => window.vbadocClasses.text()");
    Assert.Equal(28, text.Split('\n').Count(l => l.Contains("<|..") || l.Contains("-->") || l.Contains("..>")));
    Assert.Equal(28, await Count(page, "#classes-canvas svg .edgePaths > path"));
    var labels = await page.Locator("#classes-canvas svg .edgeLabel").AllInnerTextsAsync();
    Assert.Contains("WithEvents m_svc", labels);
    Assert.Contains("m_repo", labels);
  }

  [Fact]
  public async Task Clicking_a_box_selects_its_module_and_clicking_a_line_selects_its_member()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync($"{Box("Order")} .label-group");
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
    Assert.Equal(0, await Count(page, "#classes-canvas .selected, #classes-canvas .selected-line"));
  }

  [Fact]
  public async Task The_selection_is_shared_with_the_dependency_graph()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-svg g.node[data-id='OrderService']");

    await page.ClickAsync("#tab-classes");
    await WaitUntilDrawn(page);
    Assert.Equal(1, await Count(page, $"{Box("OrderService")}.selected"));

    await page.ClickAsync(Line("Logger.LogInfo"));
    await page.ClickAsync("#tab-graph");
    Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='Logger']"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Focus_shows_the_selected_module_and_its_relations_and_follows_the_selection()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.True(await page.IsDisabledAsync("#classes-focus"));
    await opened.SelectAsync("OrderService.Process");

    await page.ClickAsync("#classes-focus");
    await WaitUntilDrawn(page);

    // OrderService uses IOrderRepository (m_repo), Logger and Order; Main and ProgressReporter (m_svc) use it.
    Assert.Equal(new[] { "IOrderRepository", "Logger", "Main", "Order", "OrderService", "ProgressReporter" }, await ShownModules(page));
    Assert.Equal("Show all", await page.InnerTextAsync("#classes-focus"));

    await opened.SelectAsync("Utils");
    await WaitUntilDrawn(page);
    Assert.Equal(new[] { "Logger", "Order", "Utils" }, await ShownModules(page)); // Utils uses Logger; Order uses Utils

    await page.ClickAsync("#classes-focus");
    await WaitUntilDrawn(page);
    Assert.Equal(12, (await ShownModules(page)).Length);
  }

  [Fact]
  public async Task Show_options_hide_members_private_members_or_dependencies_and_are_remembered()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#classes-filters summary");

    await page.UncheckAsync("input[data-option='private']");
    await WaitUntilDrawn(page);
    Assert.Equal(0, await Count(page, Line("Order.m_Id")));
    Assert.Equal(1, await Count(page, Line("Order.Describe")));

    await page.UncheckAsync("input[data-option='dependencies']");
    await WaitUntilDrawn(page);
    Assert.Equal(4, await Count(page, "#classes-canvas svg .edgePaths > path")); // realisations and associations

    await page.UncheckAsync("input[data-option='members']");
    await WaitUntilDrawn(page);
    Assert.Equal(0, await Count(page, "#classes-canvas [data-member]"));

    await page.ReloadAsync();
    await WaitUntilDrawn(page); // the class diagram tab is remembered too
    Assert.Equal(0, await Count(page, "#classes-canvas [data-member]"));
    Assert.Equal(4, await Count(page, "#classes-canvas svg .edgePaths > path"));
  }

  [Fact]
  public async Task Zooms_in_out_and_to_fit()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var width = "() => document.querySelector('#classes-canvas svg').getBoundingClientRect().width";
    var before = await page.EvaluateAsync<double>(width);

    await page.ClickAsync("#classes-zoom-in");
    Assert.True(await page.EvaluateAsync<double>(width) > before * 1.2);

    await page.ClickAsync("#classes-fit");
    var fitted = await page.EvaluateAsync<double>("() => document.querySelector('#classes-canvas svg').getBoundingClientRect().height");
    Assert.True(fitted <= await page.EvaluateAsync<double>("() => document.getElementById('classes-canvas').clientHeight"));
  }

  [Fact]
  public async Task Switching_the_theme_redraws_in_the_new_colours()
  {
    await using var opened = await OpenClasses(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var fill = $"() => getComputedStyle(document.querySelector(\"{Box("Main")} .outer-path path\")).fill";
    var dark = await page.EvaluateAsync<string>(fill);

    await page.ClickAsync("#theme-toggle");
    await page.WaitForFunctionAsync($"d => ({fill})() !== d", dark);
    await WaitUntilDrawn(page);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Names_and_values_that_mean_something_to_Mermaid_or_markdown_are_shown_as_written()
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
    Assert.Equal(0, await Count(page, "#classes-canvas strong, #classes-canvas em, #classes-canvas b, #classes-canvas a, #classes-canvas code"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_large_project_renders_completely_and_scrolls()
  {
    var timer = Stopwatch.StartNew();
    await using var opened = await OpenClasses(fixture, fixture.LargeEmbedded, timeoutMs: 120000);
    timer.Stop();
    var page = opened.Page;

    Assert.Equal(300, await Count(page, "#classes-canvas g.node[data-module]"));
    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(60), $"The class diagram took {timer.Elapsed.TotalSeconds:F1}s");

    // Hundreds of dependencies would make Mermaid take minutes, so the full diagram leaves them out and says so.
    Assert.Equal(0, await Count(page, "#classes-canvas svg .edgePaths > path"));
    Assert.Contains("dependencies hidden", await page.InnerTextAsync("#classes-note"));
    Assert.False(await page.IsCheckedAsync("input[data-option='dependencies']"));

    // Every box was laid out: it has a real size and lies inside the drawing (no NaN or cut-off positions).
    var misplaced = await page.EvaluateAsync<string[]>(@"() => {
            const svg = document.querySelector('#classes-canvas svg').getBoundingClientRect();
            return [...document.querySelectorAll('#classes-canvas g.node[data-module]')]
                .filter(n => { const r = n.getBoundingClientRect();
                    return !(r.width > 0 && r.height > 0 && r.left >= svg.left - 1 && r.right <= svg.right + 1 && r.top >= svg.top - 1 && r.bottom <= svg.bottom + 1); })
                .map(n => n.dataset.module);
        }");
    Assert.Empty(misplaced);
    Assert.True(await page.EvaluateAsync<bool>("() => { const c = document.getElementById('classes-canvas'); return c.scrollHeight > c.clientHeight || c.scrollWidth > c.clientWidth; }"));

    // Selecting a box far away scrolls it into view.
    await opened.SelectAsync("Mod299");
    var inView = await page.EvaluateAsync<bool>(@"() => {
            const c = document.getElementById('classes-canvas').getBoundingClientRect();
            const r = document.querySelector(""#classes-canvas g.node[data-module='Mod299']"").getBoundingClientRect();
            return r.right > c.left && r.left < c.right && r.bottom > c.top && r.top < c.bottom;
        }");
    Assert.True(inView);
    Assert.Empty(opened.Errors);
  }
}
