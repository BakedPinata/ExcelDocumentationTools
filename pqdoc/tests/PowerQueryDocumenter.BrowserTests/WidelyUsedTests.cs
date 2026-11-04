using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Widely used queries (FR-HTML-18, step D4 of the feedback plan): those used directly by at least a share of the other
/// queries are listed in a Widely used group and marked, and Hide all hides them; the share is in the Show… menu.
/// </summary>
[Collection("Browser")]
public sealed class WidelyUsedTests(PageFixture fixture)
{
  private const string Group = "#explorer-tree > li.widely-used-items";

  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  [Fact]
  public async Task FR_HTML_18_the_share_decides_what_is_listed_and_Hide_all_hides_it_from_the_graph()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;

    // A fifth of the 13 other queries is 3 or more, and the most used, fnClean, is used by 2.
    Assert.Equal(0, await page.Locator(Group).CountAsync());
    Assert.Empty(await page.EvaluateAsync<string[]>("() => window.pqdoc.hidden()")); // nothing is hidden to begin with

    await page.ClickAsync("#explorer-show summary");
    await page.FillAsync("#explorer-widely-used-share", "15");
    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(["fnClean"], await page.Locator(Group + " > ul > li > .tree-row .tree-label").AllTextContentsAsync());
    Assert.Equal("Widely used: used by 2 of the 13 other queries",
        await page.GetAttributeAsync("#explorer-tree li.item:not(.widely-used-items li)[data-id='fnClean'] > .tree-row .tree-wide", "title"));

    await page.ClickAsync(Group + " [data-hide-widely-used]");
    Assert.DoesNotContain("fnClean", await Shown(opened));
    Assert.Equal("Show all", await page.TextContentAsync(Group + " [data-hide-widely-used]"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal("15", await page.InputValueAsync("#explorer-widely-used-share"));
    Assert.DoesNotContain("fnClean", await Shown(opened));
    await page.ClickAsync(Group + " [data-hide-widely-used]");
    Assert.Contains("fnClean", await Shown(opened));
    Assert.Empty(opened.Errors);
  }
}
