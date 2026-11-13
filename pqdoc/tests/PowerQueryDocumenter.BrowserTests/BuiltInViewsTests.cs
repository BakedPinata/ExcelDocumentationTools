using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Views built into the page (FR-HTML-23, step L4 of the generalisation plan), which are the shell's, as vbadoc's
/// are: here, that a page made in a folder holding pqdoc-views.json lists the file's views, that one opens, and that
/// saving over it makes the person's own version.
/// </summary>
[Collection("Browser")]
public sealed class BuiltInViewsTests(PageFixture fixture)
{
  private const string Orders = "#views-list li:has(.views-name:text-is('Orders and its source'))";

  private static async Task OpenMenu(IPage page)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('views').open"))
    {
      await page.ClickAsync("#views > summary");
    }
  }

  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  [Fact]
  public async Task FR_HTML_23_the_pages_own_views_are_listed_and_open()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltInPage);
    var page = opened.Page;
    await OpenMenu(page);

    Assert.Equal(["Orders and its source"], await page.Locator("#views-list .views-name").AllInnerTextsAsync());
    Assert.Equal("in the page", await page.InnerTextAsync(Orders + " .views-tag"));
    Assert.Contains("pqdoc-views.json", await page.GetAttributeAsync(Orders + " .views-tag", "title"));
    Assert.Equal(0, await page.Locator("#views-list [data-view-edit], #views-list [data-view-delete]").CountAsync());

    await page.ClickAsync(Orders + " .views-open");
    Assert.Equal(["Orders", "Source"], await Shown(opened));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_23_saving_over_one_makes_your_own_version_until_it_is_deleted()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltInPage);
    var page = opened.Page;
    await OpenMenu(page);

    await page.ClickAsync(Orders + " [data-view-save]");   // the page as it opens: every query
    Assert.Equal("yours", await page.InnerTextAsync(Orders + " .views-tag"));
    Assert.StartsWith("Saved your own version of “Orders and its source”.", await page.InnerTextAsync("#views-status"));

    await page.ClickAsync(Orders + " [data-view-delete]");
    Assert.Equal("in the page", await page.InnerTextAsync(Orders + " .views-tag"));
    await page.ClickAsync(Orders + " .views-open");
    Assert.Equal(["Orders", "Source"], await Shown(opened));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_23_a_page_made_without_a_views_file_has_none()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await OpenMenu(page);

    Assert.Equal(0, await page.Locator("#views-list li").CountAsync());
    Assert.True(await page.IsVisibleAsync("#views-none"));
    Assert.True(await page.IsDisabledAsync("#views-export"));
    Assert.Equal(0, await page.Locator("#pqdoc-views").CountAsync());
    Assert.Empty(opened.Errors);
  }
}
