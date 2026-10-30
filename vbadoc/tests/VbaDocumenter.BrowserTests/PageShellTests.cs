using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The page itself: loading, tabs, themes, layout and search.</summary>
[Collection("Browser")]
public class PageShellTests(PageFixture fixture)
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Loads_without_errors_or_any_network_request(bool embedded)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, embedded ? fixture.SampleEmbedded : fixture.SampleFolder);

    Assert.Empty(opened.Errors);
    Assert.Empty(opened.NetworkRequests);
    Assert.Equal("SampleVba.xlsm", await opened.Page.TitleAsync());
  }

  [Fact]
  public async Task Tabs_switch_views_and_keep_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");

    await page.ClickAsync("#tab-classes");

    Assert.True(await page.IsVisibleAsync("#view-classes"));
    Assert.False(await page.IsVisibleAsync("#view-graph"));
    Assert.Equal("true", await page.GetAttributeAsync("#tab-classes", "aria-selected"));
    Assert.Equal("Order.Describe", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));

    await page.FocusAsync("#tab-classes");
    await page.Keyboard.PressAsync("ArrowLeft"); // keyboard navigation between tabs
    Assert.True(await page.IsVisibleAsync("#view-graph"));
  }

  [Theory]
  [InlineData(ColorScheme.Dark, "dark")]
  [InlineData(ColorScheme.Light, "light")]
  public async Task Follows_the_system_theme_by_default(ColorScheme system, string expected)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, system);

    Assert.Equal(expected, await opened.Page.GetAttributeAsync("html", "data-theme"));
  }

  // Chromium reports "light" when the user has no preference, so the only case a page can detect as
  // "can't report a preference" is a browser without the feature. Simulate one by removing matchMedia.
  [Fact]
  public async Task Defaults_to_dark_when_the_browser_cannot_report_a_preference()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, ColorScheme.Light,
        initScript: "window.matchMedia = undefined;");

    Assert.Equal("dark", await opened.Page.GetAttributeAsync("html", "data-theme"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_theme_switch_wins_over_the_system_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, ColorScheme.Dark);
    var page = opened.Page;
    var background = "() => getComputedStyle(document.body).backgroundColor";
    var darkBackground = await page.EvaluateAsync<string>(background);

    await page.ClickAsync("#theme-toggle");

    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
    Assert.NotEqual(darkBackground, await page.EvaluateAsync<string>(background));
    Assert.Equal("Light", await page.InnerTextAsync("#theme-toggle .theme-label"));

    // Links follow the theme's accent colour, not the browser's default blue.
    await opened.SelectAsync("Order.Describe");
    var linkColour = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('#source-header a')).color");
    Assert.Equal("rgb(47, 111, 222)", linkColour);

    await page.ReloadAsync();
    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
  }

  [Fact]
  public async Task Search_selects_a_member_by_id_or_partial_name()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await page.FillAsync("#search", "isvalid");
    await page.PressAsync("#search", "Enter");

    Assert.Equal("Order.IsValid", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
    Assert.Contains("IsValid", await page.InnerTextAsync("#source-header h2"));
  }

  [Fact]
  public async Task The_version_links_to_the_repository_with_a_GitHub_mark()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var link = opened.Page.Locator("#tool-version");

    Assert.Equal("https://github.com/BakedPinata/ExcelDocumentationTools", await link.GetAttributeAsync("href"));
    Assert.Equal("_blank", await link.GetAttributeAsync("target"));
    Assert.StartsWith("vbadoc ", await link.InnerTextAsync());
    Assert.True(await link.Locator("svg.github-mark").IsVisibleAsync());
  }

  [Fact]
  public async Task Fits_a_phone_screen_without_horizontal_scrolling()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, width: 390, height: 800);
    await opened.SelectAsync("Main.ProcessOrders");

    var overflow = await opened.Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
    Assert.True(overflow <= 0, $"The page is {overflow}px wider than the screen");
  }
}
