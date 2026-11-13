namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Descriptions in the source panel (step L5 of the generalisation plan): what a module or member says about itself,
/// from its VB_Description attribute or the comments above it, shown under its signature as text.
/// </summary>
[Collection("Browser")]
public class DescriptionTests(PageFixture fixture)
{
  const string Description = "#side-panel-header .description";

  [Fact]
  public async Task A_module_and_its_members_show_what_they_say_about_themselves()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ConditionsEmbedded);
    var page = opened.Page;

    await opened.SelectAsync("Described");
    // The comments at its top, on their own lines, and as text: the <shop> isn't markup.
    Assert.Equal("Works out prices for the <shop>,\ntax included.", await page.InnerTextAsync(Description));

    await opened.SelectAsync("Described.WithTax");
    Assert.Equal("The price with tax, to two places.", await page.InnerTextAsync(Description)); // the attribute, not the comment

    await opened.SelectAsync("Described.Plain");
    Assert.Equal(0, await page.Locator(Description).CountAsync());
    Assert.Empty(opened.Errors);
  }
}
