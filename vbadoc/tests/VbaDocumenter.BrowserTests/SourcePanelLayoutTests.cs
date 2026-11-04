using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Hiding the source panel and resizing it with the splitter, in both views and on a narrow screen. Each page starts
/// with the panel locked open, as it always was before it followed the selection.
/// </summary>
[Collection("Browser")]
public class SourcePanelLayoutTests(PageFixture fixture)
{
  static async Task<double> WidthAsync(IPage page, string selector) =>
      (await page.Locator(selector).BoundingBoxAsync())!.Width;

  static async Task<double> HeightAsync(IPage page, string selector) =>
      (await page.Locator(selector).BoundingBoxAsync())!.Height;

  [Fact]
  public async Task Closing_the_panel_gives_both_views_the_room_beside_the_explorer_and_unlocks_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    Assert.Equal("true", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));
    var graphWidth = await WidthAsync(page, "#view-graph");
    Assert.True(graphWidth < 900);
    var panX = "() => d3.zoomTransform(document.getElementById('graph-svg')).x";
    var panBefore = await page.EvaluateAsync<double>(panX);

    await page.ClickAsync("#side-panel-close");

    Assert.True(await page.Locator("#side-panel").IsHiddenAsync());
    Assert.True(await page.Locator("#side-panel-splitter").IsHiddenAsync());
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));
    var room = 1280 - await WidthAsync(page, "#explorer") - 7; // everything but the explorer and its splitter
    Assert.Equal(room, await WidthAsync(page, "#view-graph"), tolerance: 2);
    // The graph moves by half the extra width, so what was in the middle stays in the middle.
    await page.WaitForFunctionAsync(FormattableString.Invariant($"() => Math.abs(({panX})() - {panBefore} - {(room - graphWidth) / 2}) < 2"));
    await page.ClickAsync("#tab-classes");
    Assert.Equal(room, await WidthAsync(page, "#view-classes"), tolerance: 2);

    await page.ReloadAsync(); // unlocked, with nothing selected
    Assert.True(await page.Locator("#side-panel").IsHiddenAsync());

    await page.ClickAsync("#side-panel-toggle");
    Assert.True(await page.Locator("#side-panel").IsVisibleAsync());
    Assert.True(await WidthAsync(page, "#view-classes") < 900);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Dragging_the_splitter_resizes_the_panel_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    var before = await WidthAsync(page, "#side-panel");
    var splitter = (await page.Locator("#side-panel-splitter").BoundingBoxAsync())!;
    var y = splitter.Y + splitter.Height / 2;

    await page.Mouse.MoveAsync(splitter.X + 3, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(400, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();

    // The panel grows by as far as the pointer moved.
    Assert.Equal(before + splitter.X + 3 - 400, await WidthAsync(page, "#side-panel"), tolerance: 2);
    Assert.Equal("68", await page.GetAttributeAsync("#side-panel-splitter", "aria-valuenow"));

    // It can't be dragged past its largest size.
    splitter = (await page.Locator("#side-panel-splitter").BoundingBoxAsync())!;
    await page.Mouse.MoveAsync(splitter.X + 3, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(50, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();
    Assert.Equal(1280 * 0.7, await WidthAsync(page, "#side-panel"), tolerance: 2);

    await page.ReloadAsync();
    Assert.Equal(1280 * 0.7, await WidthAsync(page, "#side-panel"), tolerance: 2);

    // Double-click puts it back to the default.
    await page.DblClickAsync("#side-panel-splitter");
    Assert.Equal(1280 * 0.42, await WidthAsync(page, "#side-panel"), tolerance: 2);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_click_on_the_splitter_does_not_move_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    var before = await WidthAsync(page, "#side-panel");

    await page.ClickAsync("#side-panel-splitter");

    Assert.Equal(before, await WidthAsync(page, "#side-panel"), tolerance: 0.5);
  }

  [Fact]
  public async Task Double_click_resets_the_panel_away_from_its_largest_size()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    var before = await WidthAsync(page, "#side-panel");
    var splitter = (await page.Locator("#side-panel-splitter").BoundingBoxAsync())!;
    var x = splitter.X + splitter.Width / 2;
    var y = splitter.Y + splitter.Height / 2;

    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(x - 100, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();
    Assert.Equal(before + 100, await WidthAsync(page, "#side-panel"), tolerance: 2);

    await page.DblClickAsync("#side-panel-splitter");

    Assert.Equal(before, await WidthAsync(page, "#side-panel"), tolerance: 2);
  }

  [Fact]
  public async Task The_splitter_works_from_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    await page.FocusAsync("#side-panel-splitter");
    var before = await WidthAsync(page, "#side-panel");

    await page.Keyboard.PressAsync("ArrowLeft");
    Assert.Equal(before + 1280 * 0.02, await WidthAsync(page, "#side-panel"), tolerance: 2);

    await page.Keyboard.PressAsync("ArrowRight");
    await page.Keyboard.PressAsync("ArrowRight");
    Assert.Equal(before - 1280 * 0.02, await WidthAsync(page, "#side-panel"), tolerance: 2);

    await page.Keyboard.PressAsync("End");
    Assert.Equal("70", await page.GetAttributeAsync("#side-panel-splitter", "aria-valuenow"));
    await page.Keyboard.PressAsync("Home");
    Assert.Equal(1280 * 0.2, await WidthAsync(page, "#side-panel"), tolerance: 2);
    await page.Keyboard.PressAsync("Enter");
    Assert.Equal(before, await WidthAsync(page, "#side-panel"), tolerance: 2);
  }

  [Fact]
  public async Task On_a_narrow_screen_the_splitter_resizes_the_panel_below_the_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, width: 700, height: 900, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    Assert.Equal("horizontal", await page.GetAttributeAsync("#side-panel-splitter", "aria-orientation"));
    var layout = (await page.Locator("#layout").BoundingBoxAsync())!;
    var splitter = (await page.Locator("#side-panel-splitter").BoundingBoxAsync())!;

    await page.Mouse.MoveAsync(350, splitter.Y + 3);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(350, layout.Y + layout.Height * 0.6f, new() { Steps = 5 });
    await page.Mouse.UpAsync();

    // The splitter is 7 pixels high and was grabbed 3 down, so the panel starts 4 below the pointer.
    Assert.Equal(layout.Height * 0.4 - 4, await HeightAsync(page, "#side-panel"), tolerance: 2);
    Assert.Equal(700, await WidthAsync(page, "#side-panel"), tolerance: 2);

    await page.ClickAsync("#side-panel-toggle");
    Assert.Equal(layout.Height, await HeightAsync(page, "#view-graph"), tolerance: 2);
    Assert.Empty(opened.Errors);
  }
}
