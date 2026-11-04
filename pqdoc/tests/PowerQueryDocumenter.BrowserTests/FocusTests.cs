using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Focus on click (FR-HTML-13, step D1c of the feedback plan): on by default, selecting a query shows only it and its
/// upstream and downstream chains; Ctrl+click does the other thing; Esc clears the selection.
/// </summary>
[Collection("Browser")]
public sealed class FocusTests(PageFixture fixture)
{
  private static readonly LocatorClickOptions CtrlClick = new() { Modifiers = [KeyboardModifier.Control] };

  private static Task<int> HiddenCount(IPage page) => page.Locator("#graph-svg g.node.out-of-focus").CountAsync();

  private static async Task<string[]> ShownIds(OpenedPage opened) =>
    await opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static async Task ClickQueryAsync(OpenedPage opened, string id, LocatorClickOptions? options = null) =>
    await (await opened.NodeAsync(id)).Locator("circle, rect, path").First.ClickAsync(options);

  [Fact]
  public async Task FR_HTML_13_focus_on_click_shows_only_the_query_and_its_chains_until_Esc()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    Assert.Equal("true", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));

    await ClickQueryAsync(opened, "Orders");

    // Orders uses fnCleanText, Sales Region Map and Source; Q1.Summary and Sales Report use it, and Revenue & Margin
    // (2024) uses one of those.
    Assert.Equal(
      ["fnCleanText", "Orders", "Q1.Summary", "Revenue & Margin (2024)", "Sales Region Map", "Sales Report", "Source"],
      await ShownIds(opened));
    Assert.Equal(0, await page.Locator("#graph-svg path.edge.out-of-focus[data-from='Source'][data-to='Orders']").CountAsync());

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await opened.SelectionAsync());
    Assert.Equal(0, await HiddenCount(page));
  }

  [Fact]
  public async Task FR_HTML_13_Ctrl_click_selects_without_focusing()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await ClickQueryAsync(opened, "Orders", CtrlClick);

    Assert.Equal("Orders", await opened.SelectionAsync());
    Assert.Equal(0, await HiddenCount(page));
    Assert.True(await page.Locator("#graph-svg g.node.dim").CountAsync() > 0); // highlighted, not focused
  }

  [Fact]
  public async Task FR_HTML_13_with_focus_on_click_off_a_click_selects_and_Ctrl_click_focuses()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await page.ClickAsync("#graph-focus");
    Assert.Equal("false", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));

    await ClickQueryAsync(opened, "Orders");
    Assert.Equal(0, await HiddenCount(page));

    await ClickQueryAsync(opened, "Orders", CtrlClick);
    Assert.Equal(7, (await ShownIds(opened)).Length);

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal("false", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));
  }

  [Fact]
  public async Task FR_HTML_13_turning_focus_on_click_on_focuses_the_selection_now()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Orders");
    Assert.Equal(0, await HiddenCount(page));

    await page.ClickAsync("#graph-focus");
    Assert.Equal(7, (await ShownIds(opened)).Length);

    await page.ClickAsync("#graph-focus");
    Assert.Equal(0, await HiddenCount(page));
    Assert.Equal("Orders", await opened.SelectionAsync());
  }

  [Fact]
  public async Task FR_HTML_13_Esc_empties_the_search_box_before_it_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");
    await page.FillAsync("#search", "sales");

    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(string.Empty, await page.InputValueAsync("#search"));
    Assert.Equal("Orders", await opened.SelectionAsync());

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await opened.SelectionAsync());
  }
}
