using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>The page itself: loading, themes, the top bar and the window.pqdoc object.</summary>
[Collection("Browser")]
public sealed partial class PageShellTests(PageFixture fixture)
{
  [Fact]
  public async Task Loads_without_errors_or_any_network_request()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    Assert.Empty(opened.Errors);
    Assert.Empty(opened.NetworkRequests);
    Assert.StartsWith("sales-model: 11 queries", await opened.Page.TitleAsync());
  }

  [Fact]
  public async Task The_policy_stops_the_page_reaching_the_network()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    var blocked = await opened.Page.EvaluateAsync<bool>(
      "() => fetch('https://example.com/').then(() => false, () => true)");

    Assert.True(blocked);
    Assert.Empty(opened.NetworkRequests); // refused by the page itself, before it ever became a request
  }

  [Fact]
  public async Task The_top_bar_shows_the_name_counts_and_generation_time()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    Assert.Equal("sales-model", await page.InnerTextAsync("#page-title"));
    Assert.Equal(
      "11 queries, 11 dependencies, 1 circular dependency. Generated 2026-10-02 10:03 (UTC+08:00).",
      await page.InnerTextAsync("#page-summary"));
    Assert.Equal("true", await page.GetAttributeAsync("#tab-graph", "aria-selected"));
  }

  // The generation time is at the end of the summary, so cutting it off would lose it.
  [Theory]
  [InlineData(1280)]
  [InlineData(1000)]
  [InlineData(390)]
  public async Task The_summary_is_never_cut_off(int width)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, width: width);

    var clipped = await opened.Page.EvaluateAsync<bool>(
      @"() => {
          const summary = document.getElementById('page-summary');
          return summary.scrollWidth > summary.clientWidth || summary.scrollHeight > summary.clientHeight
            || summary.getBoundingClientRect().right > window.innerWidth;
        }");

    Assert.False(clipped);
  }

  [Theory]
  [InlineData(ColorScheme.Dark, "dark")]
  [InlineData(ColorScheme.Light, "light")]
  public async Task Follows_the_system_theme_by_default(ColorScheme system, string expected)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, system);

    Assert.Equal(expected, await opened.Page.GetAttributeAsync("html", "data-theme"));
  }

  // Chromium reports "light" when the user has no preference, so the only case a page can detect as
  // "can't report a preference" is a browser without the feature. Simulate one by removing matchMedia.
  [Fact]
  public async Task Defaults_to_dark_when_the_browser_cannot_report_a_preference()
  {
    await using var opened = await OpenedPage.OpenAsync(
      fixture, fixture.SamplePage, ColorScheme.Light, initScript: "window.matchMedia = undefined;");

    Assert.Equal("dark", await opened.Page.GetAttributeAsync("html", "data-theme"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_theme_switch_wins_over_the_system_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, ColorScheme.Dark);
    var page = opened.Page;
    const string background = "() => getComputedStyle(document.body).backgroundColor";
    var darkBackground = await page.EvaluateAsync<string>(background);

    await page.ClickAsync("#theme-toggle");

    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
    Assert.NotEqual(darkBackground, await page.EvaluateAsync<string>(background));
    Assert.Equal("Light", await page.InnerTextAsync("#theme-toggle .theme-label"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal("light", await page.GetAttributeAsync("html", "data-theme"));
  }

  [Fact]
  public async Task The_theme_is_not_shared_with_vbadoc_pages()
  {
    await using var opened = await OpenedPage.OpenAsync(
      fixture, fixture.SamplePage, ColorScheme.Dark, initScript: "localStorage.setItem('vbadoc-theme', 'light');");

    Assert.Equal("dark", await opened.Page.GetAttributeAsync("html", "data-theme"));
  }

  [Fact]
  public async Task The_version_links_to_the_repository_with_a_GitHub_mark()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var link = opened.Page.Locator("#tool-version");

    Assert.Equal("https://github.com/BakedPinata/ExcelDocumentationTools", await link.GetAttributeAsync("href"));
    Assert.Equal("_blank", await link.GetAttributeAsync("target"));
    Assert.Matches(BuildStamp(), await link.InnerTextAsync());
    Assert.True(await link.Locator("svg.github-mark").IsVisibleAsync());
  }

  [Fact]
  public async Task The_page_object_has_the_same_shape_as_vbadocs()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    var missing = await opened.Page.EvaluateAsync<string[]>(
      @"() => ['select', 'selection', 'onSelectionChange', 'theme', 'onThemeChange', 'activeView', 'onViewChange']
            .filter(name => typeof window.pqdoc[name] !== 'function')");

    Assert.Empty(missing);
    Assert.Equal("graph", await opened.Page.EvaluateAsync<string>("() => window.pqdoc.activeView()"));
  }

  [Fact]
  public async Task Changes_reach_the_listeners_a_view_registers()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, ColorScheme.Dark);
    var page = opened.Page;
    await page.EvaluateAsync(
      @"() => {
          window.heard = [];
          window.pqdoc.onSelectionChange(id => window.heard.push('selection ' + id));
          window.pqdoc.onThemeChange(theme => window.heard.push('theme ' + theme));
        }");

    await opened.SelectAsync("Orders");
    await opened.SelectAsync("Orders"); // no change, so no notification
    await page.ClickAsync("#theme-toggle");
    await opened.SelectAsync(null);

    Assert.Equal(
      ["selection Orders", "theme light", "selection null"],
      await page.EvaluateAsync<string[]>("() => window.heard"));
    Assert.Equal("light", await page.EvaluateAsync<string>("() => window.pqdoc.theme()"));
  }

  [Fact]
  public async Task Selecting_an_unknown_query_changes_nothing()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    await opened.SelectAsync("Orders");

    var accepted = await opened.Page.EvaluateAsync<bool>("() => window.pqdoc.select('No such query')");

    Assert.False(accepted);
    Assert.Equal("Orders", await opened.SelectionAsync());
  }

  [Fact]
  public async Task Fits_a_phone_screen_without_horizontal_scrolling()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, width: 390, height: 800);
    await opened.SelectAsync("Sales Report");

    var overflow = await opened.Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");

    Assert.True(overflow <= 0, $"The page is {overflow}px wider than the screen");
  }

  [Theory]
  [InlineData(1280, 800, true)]
  [InlineData(390, 800, false)]
  public async Task The_legend_starts_closed_where_it_would_cover_the_graph(int width, int height, bool open)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, width: width, height: height);

    Assert.Equal(open, await opened.Page.EvaluateAsync<bool>("() => document.getElementById('graph-legend').open"));
  }

  [GeneratedRegex(@"^pqdoc \d+\.\d+\.\d+( \([0-9a-f]{7}(-dirty)?\))?$")]
  private static partial Regex BuildStamp();
}
