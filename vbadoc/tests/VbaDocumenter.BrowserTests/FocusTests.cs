using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Focus on click (step D1c of the feedback plan): one setting for the page, on by default and shared by every view's
/// Focus button; Ctrl+click for the other; and Esc to clear the selection, after an open menu or a box with text in it.
/// </summary>
[Collection("Browser")]
public class FocusTests(PageFixture fixture)
{
  static readonly PageClickOptions CtrlClick = new() { Modifiers = [KeyboardModifier.Control] };

  static Task<string[]> ShownNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static string Node(string id) => $"#graph-svg g.node[data-id='{id}']";

  [Fact]
  public async Task Ctrl_click_selects_without_focusing_and_a_plain_click_then_focuses()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync(Node("Utils"), CtrlClick);
    Assert.Equal("Utils", await Selection(page));
    Assert.Equal(12, (await ShownNodes(page)).Length);
    Assert.True(await page.Locator("#graph-svg g.node.dim").CountAsync() > 0); // highlighted, not focused

    await page.ClickAsync(Node("Utils"));
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
  }

  [Fact]
  public async Task With_focus_on_click_off_a_click_selects_and_Ctrl_click_focuses()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#graph-focus");
    Assert.Equal("false", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));

    await page.ClickAsync(Node("Utils"));
    Assert.Equal(12, (await ShownNodes(page)).Length);

    await page.ClickAsync(Node("Utils"), CtrlClick);
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
  }

  [Fact]
  public async Task Turning_focus_on_click_on_focuses_the_selection_now_and_off_shows_everything()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Utils");
    Assert.Equal(12, (await ShownNodes(page)).Length);

    await page.ClickAsync("#graph-focus");
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));

    await page.ClickAsync("#graph-focus");
    Assert.Equal(12, (await ShownNodes(page)).Length);
    Assert.Equal("Utils", await Selection(page));
  }

  [Fact]
  public async Task The_setting_is_one_for_the_whole_page_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.ClickAsync("#graph-focus");
    await page.ClickAsync("#tab-classes");
    Assert.Equal("false", await page.GetAttributeAsync("#classes-focus", "aria-pressed"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc");
    Assert.Equal("false", await page.GetAttributeAsync("#classes-focus", "aria-pressed"));
    Assert.Equal("false", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));
  }

  [Fact]
  public async Task Ctrl_click_in_the_explorer_selects_without_focusing()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    const string utils = "#explorer-tree li[data-id='Utils'] > .tree-row .tree-label";

    await page.ClickAsync(utils, CtrlClick);
    Assert.Equal("Utils", await Selection(page));
    Assert.Equal(12, (await ShownNodes(page)).Length);

    await page.ClickAsync(utils);
    Assert.Equal(["Logger", "Order", "Utils"], await ShownNodes(page));
  }

  [Fact]
  public async Task Esc_closes_an_open_menu_before_it_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync(Node("Utils"));
    await page.ClickAsync("#graph-filters summary");

    await page.Keyboard.PressAsync("Escape");
    Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('graph-filters').open"));
    Assert.Equal("Utils", await Selection(page));

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await Selection(page));
    Assert.Equal(12, (await ShownNodes(page)).Length);
  }

  [Fact]
  public async Task Esc_in_a_box_with_text_empties_it_before_it_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Utils");
    await page.FillAsync("#explorer-filter", "util");

    await page.Keyboard.PressAsync("Escape");
    Assert.Equal("", await page.InputValueAsync("#explorer-filter"));
    Assert.Equal("Utils", await Selection(page));

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await Selection(page));
  }

  [Fact]
  public async Task Esc_clears_the_selection_in_the_class_diagram_too()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    await opened.SelectAsync("Utils");
    await page.WaitForFunctionAsync("() => document.querySelectorAll('#classes-svg g.class-box').length === 3");

    await page.Keyboard.PressAsync("Escape");

    Assert.Null(await Selection(page));
    await page.WaitForFunctionAsync("() => document.querySelectorAll('#classes-svg g.class-box').length === 12");
  }

  [Fact]
  public async Task Direct_or_Transitive_is_a_setting_now_and_keeps_an_earlier_choice()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded,
        initScript: "localStorage.setItem('vbadoc-graph-reach', 'transitive');");
    var page = opened.Page;

    Assert.Equal(0, await page.Locator("#view-graph [data-reach]").CountAsync());
    await page.ClickAsync("#graph-settings summary");
    Assert.Equal("transitive", await page.InputValueAsync("#graph-setting-reach"));
    Assert.Equal("graph-focus", await page.EvaluateAsync<string>("() => document.querySelector('#view-graph .view-toolbar > *').id"));
  }
}
