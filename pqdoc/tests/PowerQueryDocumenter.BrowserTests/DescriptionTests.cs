namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Descriptions in the details panel (FR-HTML-24, step L5 of the generalisation plan): a query's description in the
/// workbook, otherwise the comments at the top of its code.
/// </summary>
[Collection("Browser")]
public sealed class DescriptionTests(PageFixture fixture)
{
  private const string Description = "#side-panel-header .description";

  [Fact]
  public async Task FR_HTML_24_a_query_shows_its_description_from_the_workbook()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    await opened.SelectAsync("Chain_E");
    Assert.Equal("Revenue totals per company for the region in pRegion.", await page.InnerTextAsync(Description));

    await opened.SelectAsync("Chain_A");
    Assert.Equal(0, await page.Locator(Description).CountAsync());
    Assert.Empty(opened.Errors);
  }
}
