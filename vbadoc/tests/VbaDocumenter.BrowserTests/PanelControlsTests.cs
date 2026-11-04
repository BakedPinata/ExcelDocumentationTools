using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The panel controls (step D1a of the feedback plan): the toggles at the two ends of the top bar, the source panel
/// following the selection unless locked, the views keeping still while it does, and copying the source.
/// </summary>
[Collection("Browser")]
public class PanelControlsTests(PageFixture fixture)
{
  /// <summary>A clipboard that keeps what's written to it in window.copied.</summary>
  const string Clipboard =
      "window.copied = null; Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: t => { window.copied = t; return Promise.resolve(); } } });";

  /// <summary>A browser that won't let the page write to the clipboard either way.</summary>
  const string NoClipboard =
      "Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: () => Promise.reject(new Error('denied')) } }); document.execCommand = () => false;";

  /// <summary>The text of the lines shown in the source panel, as the source has them.</summary>
  const string ShownText =
      "() => Array.from(document.querySelectorAll('#side-panel-body .line .lt')).map(e => e.textContent === ' ' ? '' : e.textContent).join('\\n')";

  const string Transform = "() => d3.zoomTransform(document.getElementById('graph-svg')).toString()";

  static Task<bool> IsHiddenAsync(IPage page) => page.Locator("#side-panel").IsHiddenAsync();

  /// <summary>Waits two frames, by when the views have taken any new size, and a little longer for a move to start.</summary>
  static async Task SettleAsync(IPage page)
  {
    await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    await page.WaitForTimeoutAsync(450);
  }

  static async Task WaitUntilStillAsync(IPage page) =>
      await page.WaitForFunctionAsync("() => window.vbadocGraph && window.vbadocGraph.nodeCount() > 0 && !window.vbadocGraph.moving()");

  [Fact]
  public async Task The_toggles_are_icons_at_the_two_ends_of_the_top_bar()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    var controls = await page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('.topbar button, .topbar a, .topbar h1')).map(e => e.id)");
    Assert.Equal("explorer-toggle", controls[0]);
    Assert.Equal("page-title", controls[1]);
    Assert.Equal("side-panel-toggle", controls[^1]);
    var explorer = (await page.Locator("#explorer-toggle").BoundingBoxAsync())!;
    var title = (await page.Locator("#page-title").BoundingBoxAsync())!;
    var panel = (await page.Locator("#side-panel-toggle").BoundingBoxAsync())!;
    Assert.True(explorer.X < title.X);
    Assert.True(panel.X + panel.Width > 1280 - 40, $"the side panel's toggle ends at {panel.X + panel.Width}");

    Assert.Equal("", (await page.InnerTextAsync("#explorer-toggle")).Trim());
    Assert.Equal("", (await page.InnerTextAsync("#side-panel-toggle")).Trim());
    Assert.Equal("Explorer", await page.GetAttributeAsync("#explorer-toggle", "aria-label"));
    Assert.Equal("Source", await page.GetAttributeAsync("#side-panel-toggle", "aria-label"));
    Assert.Equal("Show or hide the explorer", await page.GetAttributeAsync("#explorer-toggle", "title"));
    Assert.Equal("Show or hide the source panel", await page.GetAttributeAsync("#side-panel-toggle", "title"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task On_a_narrow_screen_the_explorers_toggle_stays_beside_the_title()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, width: 700, height: 900);
    var page = opened.Page;

    var toggle = (await page.Locator("#explorer-toggle").BoundingBoxAsync())!;
    var title = (await page.Locator("#page-title").BoundingBoxAsync())!;
    Assert.True(Math.Abs(toggle.Y + toggle.Height / 2 - (title.Y + title.Height / 2)) < 4, $"toggle at {toggle.Y}, title at {title.Y}");
    Assert.True(title.X > toggle.X);
  }

  [Fact]
  public async Task The_panel_opens_with_a_selection_and_closes_when_it_is_cleared()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.True(await IsHiddenAsync(page));
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));

    await opened.SelectAsync("Order.Describe");
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));
    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());

    await opened.SelectAsync("Logger");
    Assert.False(await IsHiddenAsync(page));

    await page.EvaluateAsync("() => window.vbadoc.select(null)");
    Assert.True(await IsHiddenAsync(page));
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));
  }

  [Fact]
  public async Task A_page_opened_with_a_selection_starts_with_the_panel_open()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.EvaluateAsync("() => history.replaceState(null, '', '#Order')");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc");

    Assert.Equal("Order", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
    Assert.False(await IsHiddenAsync(page));
  }

  [Fact]
  public async Task The_lock_keeps_the_panel_open_is_remembered_and_closing_the_panel_unlocks_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Order");

    await page.ClickAsync("#side-panel-lock");
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));
    await page.EvaluateAsync("() => window.vbadoc.select(null)");
    Assert.False(await IsHiddenAsync(page));
    Assert.Contains("Select a module or member", await page.InnerTextAsync("#side-panel-body"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc");
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));

    await page.ClickAsync("#side-panel-close");
    Assert.True(await IsHiddenAsync(page));
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));
    await opened.SelectAsync("Order");
    Assert.False(await IsHiddenAsync(page));
    await page.EvaluateAsync("() => window.vbadoc.select(null)");
    Assert.True(await IsHiddenAsync(page));
  }

  [Fact]
  public async Task The_toggle_still_opens_and_closes_the_panel_by_hand()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#side-panel-toggle");
    Assert.False(await IsHiddenAsync(page));
    Assert.Contains("Select a module or member", await page.InnerTextAsync("#side-panel-body"));

    await opened.SelectAsync("Order");
    await page.ClickAsync("#side-panel-toggle");
    Assert.True(await IsHiddenAsync(page));

    await opened.SelectAsync("Logger"); // it follows the selection again
    Assert.False(await IsHiddenAsync(page));
  }

  [Fact]
  public async Task The_graph_keeps_still_when_the_panel_opens_or_closes_with_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await WaitUntilStillAsync(page);
    // A node the panel won't cover: the panel takes 42% of the width, and its splitter 7 pixels.
    var id = await page.EvaluateAsync<string?>(@"() => {
        const view = document.getElementById('view-graph').getBoundingClientRect();
        const room = view.width - 0.42 * 1280 - 7;
        const fits = Array.from(document.querySelectorAll('#graph-svg g.node')).filter(n => {
          const box = n.getBoundingClientRect();
          return box.left - view.left > 60 && box.right - view.left < room - 60 && box.top - view.top > 80 && box.bottom - view.top < view.height - 60;
        });
        return fits.length ? fits[0].dataset.id : null;
      }");
    Assert.NotNull(id);
    var before = await page.EvaluateAsync<string>(Transform);

    await opened.SelectAsync(id!);
    await SettleAsync(page);
    Assert.False(await IsHiddenAsync(page));
    Assert.Equal(before, await page.EvaluateAsync<string>(Transform));

    await page.EvaluateAsync("() => window.vbadoc.select(null)");
    await SettleAsync(page);
    Assert.True(await IsHiddenAsync(page));
    Assert.Equal(before, await page.EvaluateAsync<string>(Transform));
  }

  [Fact]
  public async Task A_node_the_opening_panel_would_cover_is_brought_into_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await WaitUntilStillAsync(page);
    var id = await page.EvaluateAsync<string>(@"() => {
        const nodes = Array.from(document.querySelectorAll('#graph-svg g.node'));
        nodes.sort((a, b) => b.getBoundingClientRect().right - a.getBoundingClientRect().right);
        return nodes[0].dataset.id;
      }");
    var panelLeft = 1280 - 0.42 * 1280;
    Assert.True(await NodeCentreAsync(page, id) > panelLeft, "the rightmost node should be where the panel opens");

    await opened.SelectAsync(id);
    await SettleAsync(page);

    var view = (await page.Locator("#view-graph").BoundingBoxAsync())!;
    Assert.True(await NodeCentreAsync(page, id) < view.X + view.Width - 20, $"{id} is at {await NodeCentreAsync(page, id)}");
  }

  static Task<double> NodeCentreAsync(IPage page, string id) => page.EvaluateAsync<double>(
      "id => { const b = document.querySelector(`#graph-svg g.node[data-id='${id}']`).getBoundingClientRect(); return b.left + b.width / 2; }", id);

  [Fact]
  public async Task A_box_clicked_in_the_class_diagram_that_the_opening_panel_would_cover_is_brought_into_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#tab-classes");
    await page.WaitForSelectorAsync("#classes-svg g.class-box");
    // The rightmost box that's in view now, and would be under the panel once it opens.
    var module = await page.EvaluateAsync<string?>(@"() => {
        const view = document.getElementById('view-classes').getBoundingClientRect();
        const boxes = Array.from(document.querySelectorAll('#classes-svg g.class-box')).filter(b => {
          const r = b.getBoundingClientRect();
          return r.left > view.left && r.left + 40 < view.right && r.top > view.top + 60 && r.top + 20 < view.bottom;
        });
        boxes.sort((a, b) => b.getBoundingClientRect().left - a.getBoundingClientRect().left);
        return boxes.length && boxes[0].getBoundingClientRect().left > 1280 - 0.42 * 1280 ? boxes[0].dataset.module : null;
      }");
    Assert.NotNull(module);
    var title = page.Locator($"#classes-svg g.class-box[data-module='{module}']");
    var box = (await title.BoundingBoxAsync())!;

    await page.Mouse.ClickAsync(box.X + 20, box.Y + 10);
    await SettleAsync(page);

    Assert.Equal(module, await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
    var view = (await page.Locator("#view-classes").BoundingBoxAsync())!;
    var after = (await title.BoundingBoxAsync())!;
    Assert.True(after.X + after.Width <= view.X + view.Width + 1, $"the box ends at {after.X + after.Width}, the view at {view.X + view.Width}");
  }

  [Fact]
  public async Task The_copy_button_copies_the_lines_shown_as_plain_text()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: Clipboard);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");
    var shown = await page.EvaluateAsync<string>(ShownText);

    await page.ClickAsync("#side-panel-body .copy-code");
    await page.WaitForFunctionAsync("() => window.copied !== null");

    var copied = await page.EvaluateAsync<string>("() => window.copied");
    Assert.Equal(shown, copied);
    Assert.Equal(3, copied.Split('\n').Length);
    Assert.StartsWith("Public Function Describe() As String", copied, StringComparison.Ordinal);
    Assert.Equal("Copied", (await page.InnerTextAsync("#side-panel-body .copy-code")).Trim());
    await page.WaitForFunctionAsync("() => document.getElementById('announcer').textContent === 'Copied'");

    await page.ClickAsync("#toggle-context"); // the whole module, with the member marked
    await page.EvaluateAsync("() => { window.copied = null; }");
    await page.ClickAsync("#side-panel-body .copy-code");
    await page.WaitForFunctionAsync("() => window.copied !== null");
    Assert.Equal(await page.EvaluateAsync<string>(ShownText), await page.EvaluateAsync<string>("() => window.copied"));
    Assert.Equal(44, (await page.EvaluateAsync<string>("() => window.copied")).Split('\n').Length);
  }

  [Fact]
  public async Task When_the_browser_refuses_to_copy_the_code_is_selected_for_Ctrl_C()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: NoClipboard);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");

    await page.ClickAsync("#side-panel-body .copy-code");

    await page.WaitForFunctionAsync("() => document.querySelector('#side-panel-body .copy-code').textContent.includes('Ctrl+C')");
    var selected = await page.EvaluateAsync<string>("() => window.getSelection().toString()");
    Assert.Contains("Describe = m_Id", selected, StringComparison.Ordinal);
    Assert.DoesNotContain("42", selected, StringComparison.Ordinal); // the line numbers aren't part of it
  }

  [Fact]
  public async Task The_copy_button_stays_in_view_when_the_code_scrolls()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Order");
    await page.EvaluateAsync("() => { const body = document.getElementById('side-panel-body'); body.scrollTop = body.scrollHeight; }");

    var body = (await page.Locator("#side-panel-body").BoundingBoxAsync())!;
    var button = (await page.Locator("#side-panel-body .copy-code").BoundingBoxAsync())!;
    Assert.True(await page.EvaluateAsync<double>("() => document.getElementById('side-panel-body').scrollTop") > 0);
    Assert.InRange(button.Y, body.Y, body.Y + 20);
    Assert.InRange(button.X + button.Width, body.X + body.Width - 30, body.X + body.Width);
  }
}
