using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// A query's steps (FR-HTML-20, step E4 of the feedback plan): in the details panel, above the source, each with a link to
/// its line and the queries it uses, then the in expression.
/// </summary>
[Collection("Browser")]
public sealed class StepsTests(PageFixture fixture)
{
  private static Task<string[]> Steps(IPage page) =>
    page.Locator("#details-steps > li").EvaluateAllAsync<string[]>(
      "ls => ls.map(l => l.querySelector('.ref-ln').textContent + ' ' + l.querySelector('code').textContent + " +
      "[...l.querySelectorAll('.step-uses a')].map(a => ' ' + a.textContent).join(''))");

  [Fact]
  public async Task FR_HTML_20_the_steps_are_listed_above_the_source_with_their_lines_and_what_they_use()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Sales Report");

    Assert.Equal(["2 Merged Orders Customers", "3 WithQuarter Q1.Summary", "4 in"], await Steps(page));
    Assert.Equal("Steps (2)", await page.TextContentAsync("#side-panel-body h3:has(+ #details-steps)"));
    Assert.True(await page.EvaluateAsync<bool>(
      "() => !!(document.getElementById('details-steps').compareDocumentPosition(document.getElementById('details-source')) & Node.DOCUMENT_POSITION_FOLLOWING)"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_20_a_steps_line_is_marked_in_the_source_and_a_query_it_uses_can_be_chosen()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Sales Report");

    await page.ClickAsync("#details-steps [data-ref-line='3']");
    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Contains("WithQuarter", await page.InnerTextAsync("#side-panel .code .line.target[data-line='3']"));

    await page.ClickAsync("#details-steps a[data-select='Q1.Summary']");
    Assert.Equal("Q1.Summary", await opened.SelectionAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_20_a_query_without_a_let_of_its_own_has_no_steps()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;
    await opened.SelectAsync("Unused Parameter");

    Assert.Equal(0, await page.Locator("#details-steps").CountAsync());
    Assert.Empty(opened.Errors);
  }
}
