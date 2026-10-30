using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>Hiding the source panel and resizing it with the splitter, in both views and on a narrow screen.</summary>
[Collection("Browser")]
public class SourcePanelLayoutTests(PageFixture fixture)
{
  static async Task<double> WidthAsync(IPage page, string selector) =>
      (await page.Locator(selector).BoundingBoxAsync())!.Width;

  static async Task<double> HeightAsync(IPage page, string selector) =>
      (await page.Locator(selector).BoundingBoxAsync())!.Height;

  [Fact]
  public async Task Hiding_the_panel_gives_both_views_the_full_width_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    Assert.Equal("true", await page.GetAttributeAsync("#source-toggle", "aria-pressed"));
    var graphWidth = await WidthAsync(page, "#view-graph");
    Assert.True(graphWidth < 900);
    var panX = "() => d3.zoomTransform(document.getElementById('graph-svg')).x";
    var panBefore = await page.EvaluateAsync<double>(panX);

    await page.ClickAsync("#source-close");

    Assert.True(await page.Locator("#source-panel").IsHiddenAsync());
    Assert.True(await page.Locator("#source-splitter").IsHiddenAsync());
    Assert.Equal("false", await page.GetAttributeAsync("#source-toggle", "aria-pressed"));
    Assert.Equal(1280, await WidthAsync(page, "#view-graph"), tolerance: 2);
    // The graph moves by half the extra width, so what was in the middle stays in the middle.
    await page.WaitForFunctionAsync(FormattableString.Invariant($"() => Math.abs(({panX})() - {panBefore} - {(1280 - graphWidth) / 2}) < 2"));
    await page.ClickAsync("#tab-classes");
    Assert.Equal(1280, await WidthAsync(page, "#view-classes"), tolerance: 2);

    await page.ReloadAsync();
    Assert.True(await page.Locator("#source-panel").IsHiddenAsync());

    await page.ClickAsync("#source-toggle");
    Assert.True(await page.Locator("#source-panel").IsVisibleAsync());
    Assert.True(await WidthAsync(page, "#view-classes") < 900);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Dragging_the_splitter_resizes_the_panel_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var splitter = (await page.Locator("#source-splitter").BoundingBoxAsync())!;
    var y = splitter.Y + splitter.Height / 2;

    await page.Mouse.MoveAsync(splitter.X + 3, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(400, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();

    // The panel now runs from the pointer to the right edge.
    Assert.Equal(1280 - 400, await WidthAsync(page, "#source-panel"), tolerance: 2);
    Assert.Equal("69", await page.GetAttributeAsync("#source-splitter", "aria-valuenow"));

    // It can't be dragged past its largest size.
    splitter = (await page.Locator("#source-splitter").BoundingBoxAsync())!;
    await page.Mouse.MoveAsync(splitter.X + 3, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(50, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();
    Assert.Equal(1280 * 0.7, await WidthAsync(page, "#source-panel"), tolerance: 2);

    await page.ReloadAsync();
    Assert.Equal(1280 * 0.7, await WidthAsync(page, "#source-panel"), tolerance: 2);

    // Double-click puts it back to the default.
    await page.DblClickAsync("#source-splitter");
    Assert.Equal(1280 * 0.42, await WidthAsync(page, "#source-panel"), tolerance: 2);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_splitter_works_from_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.FocusAsync("#source-splitter");
    var before = await WidthAsync(page, "#source-panel");

    await page.Keyboard.PressAsync("ArrowLeft");
    Assert.Equal(before + 1280 * 0.02, await WidthAsync(page, "#source-panel"), tolerance: 2);

    await page.Keyboard.PressAsync("ArrowRight");
    await page.Keyboard.PressAsync("ArrowRight");
    Assert.Equal(before - 1280 * 0.02, await WidthAsync(page, "#source-panel"), tolerance: 2);

    await page.Keyboard.PressAsync("End");
    Assert.Equal("70", await page.GetAttributeAsync("#source-splitter", "aria-valuenow"));
    await page.Keyboard.PressAsync("Home");
    Assert.Equal(1280 * 0.2, await WidthAsync(page, "#source-panel"), tolerance: 2);
    await page.Keyboard.PressAsync("Enter");
    Assert.Equal(before, await WidthAsync(page, "#source-panel"), tolerance: 2);
  }

  [Fact]
  public async Task On_a_narrow_screen_the_splitter_resizes_the_panel_below_the_view()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, width: 700, height: 900);
    var page = opened.Page;
    Assert.Equal("horizontal", await page.GetAttributeAsync("#source-splitter", "aria-orientation"));
    var layout = (await page.Locator("#layout").BoundingBoxAsync())!;
    var splitter = (await page.Locator("#source-splitter").BoundingBoxAsync())!;

    await page.Mouse.MoveAsync(350, splitter.Y + 3);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(350, layout.Y + layout.Height * 0.6f, new() { Steps = 5 });
    await page.Mouse.UpAsync();

    Assert.Equal(layout.Height * 0.4, await HeightAsync(page, "#source-panel"), tolerance: 2);
    Assert.Equal(700, await WidthAsync(page, "#source-panel"), tolerance: 2);

    await page.ClickAsync("#source-toggle");
    Assert.Equal(layout.Height, await HeightAsync(page, "#view-graph"), tolerance: 2);
    Assert.Empty(opened.Errors);
  }
}
