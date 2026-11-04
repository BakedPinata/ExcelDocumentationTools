using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The panel controls (step D1a of the feedback plan): the toggles at the two ends of the top bar, the details panel
/// following the selection unless locked, the graph keeping still while it does, and copying a query's M code.
/// </summary>
[Collection("Browser")]
public sealed class PanelControlsTests(PageFixture fixture)
{
  /// <summary>A clipboard that keeps what's written to it in window.copied.</summary>
  private const string Clipboard =
    "window.copied = null; Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: t => { window.copied = t; return Promise.resolve(); } } });";

  private const string Transform = "() => document.querySelector('#graph-svg .viewport').getAttribute('transform')";

  private static Task<bool> IsHiddenAsync(IPage page) => page.Locator("#side-panel").IsHiddenAsync();

  /// <summary>Waits two frames, by when the graph has taken any new size, and a little longer for a move to start.</summary>
  private static async Task SettleAsync(IPage page)
  {
    await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    await page.WaitForTimeoutAsync(500);
  }

  [Fact]
  public async Task FR_HTML_12_the_toggles_are_icons_at_the_two_ends_of_the_top_bar()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    var controls = await page.EvaluateAsync<string[]>(
      "() => Array.from(document.querySelectorAll('.topbar button, .topbar a, .topbar h1')).map(e => e.id)");
    Assert.Equal("explorer-toggle", controls[0]);
    Assert.Equal("page-title", controls[1]);
    Assert.Equal("side-panel-toggle", controls[^1]);
    Assert.Equal(string.Empty, (await page.InnerTextAsync("#side-panel-toggle")).Trim());
    Assert.Equal("Details", await page.GetAttributeAsync("#side-panel-toggle", "aria-label"));
    Assert.Equal("Show or hide the details panel", await page.GetAttributeAsync("#side-panel-toggle", "title"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_12_the_panel_opens_with_a_selection_and_Clear_selection_closes_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    Assert.True(await IsHiddenAsync(page));

    await opened.SelectAsync("Orders");
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal("Orders", await page.InnerTextAsync("#side-panel-header h2"));

    await page.ClickAsync("#details-clear");
    Assert.Null(await opened.SelectionAsync());
    Assert.True(await IsHiddenAsync(page));
  }

  [Fact]
  public async Task FR_HTML_12_the_lock_keeps_the_panel_open_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    await page.ClickAsync("#side-panel-lock");
    await opened.SelectAsync(null);
    Assert.False(await IsHiddenAsync(page));
    Assert.Contains("Select a query", await page.InnerTextAsync("#side-panel-body"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));

    await page.ClickAsync("#side-panel-lock");
    await opened.SelectAsync("Orders");
    await opened.SelectAsync(null);
    Assert.True(await IsHiddenAsync(page));
  }

  [Fact]
  public async Task FR_HTML_12_the_graph_keeps_still_when_the_panel_opens_or_closes_with_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await SettleAsync(page);
    var before = await page.EvaluateAsync<string>(Transform);

    // Source is a root, at the left, where the panel doesn't reach.
    await (await opened.NodeAsync("Source")).Locator("circle, rect, path").First.ClickAsync();
    await SettleAsync(page);
    Assert.Equal("Source", await opened.SelectionAsync());
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal(before, await page.EvaluateAsync<string>(Transform));

    await opened.SelectAsync(null);
    await SettleAsync(page);
    Assert.True(await IsHiddenAsync(page));
    Assert.Equal(before, await page.EvaluateAsync<string>(Transform));
  }

  [Fact]
  public async Task FR_HTML_12_a_query_clicked_where_the_panel_opens_is_brought_into_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await SettleAsync(page);
    var id = await page.EvaluateAsync<string>(@"() => {
        const nodes = Array.from(document.querySelectorAll('#graph-svg g.node'));
        nodes.sort((a, b) => b.getBoundingClientRect().right - a.getBoundingClientRect().right);
        return nodes[0].dataset.id;
      }");
    var node = await opened.NodeAsync(id);
    var box = (await node.BoundingBoxAsync())!;
    Assert.True(box.X > 1280 * 0.72, $"{id} should be where the panel opens, but is at {box.X}");

    await node.Locator("circle, rect, path").First.ClickAsync();
    await SettleAsync(page);

    Assert.Equal(id, await opened.SelectionAsync());
    var view = (await page.Locator("#view-graph").BoundingBoxAsync())!;
    var after = (await node.BoundingBoxAsync())!;
    Assert.True(after.X + after.Width / 2 < view.X + view.Width - 20, $"{id} is at {after.X}, the view ends at {view.X + view.Width}");
  }

  [Fact]
  public async Task FR_SRC_4_the_copy_button_copies_the_querys_M_code_as_its_file_has_it_apart_from_the_last_line_break()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: Clipboard);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    await page.ClickAsync("#details-source .copy-code");
    await page.WaitForFunctionAsync("() => window.copied !== null");

    var expected = (await File.ReadAllTextAsync(Path.Combine(fixture.SourceFolder, "Orders.m"), TestContext.Current.CancellationToken))
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .TrimEnd('\n');
    Assert.Equal(expected, await page.EvaluateAsync<string>("() => window.copied"));
    Assert.Equal("Copied", (await page.InnerTextAsync("#details-source .copy-code")).Trim());
  }
}
