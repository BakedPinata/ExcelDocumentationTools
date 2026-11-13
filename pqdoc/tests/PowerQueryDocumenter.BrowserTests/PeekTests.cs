using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Peek (FR-HTML-22, step L2 of the generalisation plan): a click on a line in Uses or Used by opens a preview of the
/// code around it, under the line, and leaves the selection, the graph and the details panel's source as they are.
/// Go to in the preview, or Ctrl+click on the line, goes there.
/// </summary>
[Collection("Browser")]
public sealed class PeekTests(PageFixture fixture)
{
  private static string Entry(string list, string id) =>
    $"#details-{list} .ref-item:has(> details > summary > a[data-select='{id}'])";

  private const string Marked = "#details-source .code .line.target";

  // Showing all of a longer item is the shell's, and is tested with vbadoc's page: no fixture query is long enough.
  private static Task<string[]> PeekLines(IPage page, string peek) =>
    page.Locator(peek + " .peek-code .line").EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.peekLine)");

  [Fact]
  public async Task FR_HTML_22_a_click_on_a_line_opens_a_preview_and_leaves_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;
    await opened.SelectAsync("Q1.Summary");
    var report = Entry("used-by", "Sales Report");
    var peek = report + " .ref-peek";
    var source = await page.InnerTextAsync("#details-source");

    await page.ClickAsync(report + " .ref-count");
    await page.ClickAsync(report + " .ref-line[data-ref-line='3']");

    // Sales Report.m has five lines: all of it is within five of line 3, so there's no more to show.
    Assert.Equal(["1", "2", "3", "4", "5"], await PeekLines(page, peek));
    Assert.Contains("Q1.Summary", await page.InnerTextAsync(peek + " .line.peek-target"));
    Assert.Equal("3", await page.GetAttributeAsync(peek + " .line.peek-target", "data-peek-line"));
    Assert.Equal(0, await page.Locator(peek + " [data-peek-whole]").CountAsync());
    Assert.Equal("Q1.Summary", await opened.SelectionAsync());
    Assert.Equal(source, await page.InnerTextAsync("#details-source"));
    Assert.Equal(0, await page.Locator(Marked).CountAsync());

    await page.Keyboard.PressAsync("Escape");   // closes the preview, not the selection
    Assert.Equal(0, await page.Locator(peek).CountAsync());
    Assert.Equal("Q1.Summary", await opened.SelectionAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_22_Go_to_and_Ctrl_click_select_the_query_that_holds_the_line()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Q1.Summary");
    var report = Entry("used-by", "Sales Report");
    await page.ClickAsync(report + " .ref-count");
    await page.ClickAsync(report + " .ref-line[data-ref-line='3']");

    await page.ClickAsync(report + " .ref-peek [data-peek-go]");
    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Contains("Q1.Summary", await page.InnerTextAsync(Marked + "[data-line='3']"));

    await opened.SelectAsync("Q1.Summary");
    await page.ClickAsync(report + " .ref-count");
    await page.Locator(report + " .ref-line[data-ref-line='3']").ClickAsync(new() { Modifiers = [KeyboardModifier.Control] });
    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Equal(1, await page.Locator(Marked + "[data-line='3']").CountAsync());
    Assert.Equal(0, await page.Locator(".ref-peek").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_22_without_the_source_the_preview_says_how_to_load_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage);
    var page = opened.Page;
    await opened.SelectAsync("Sales Report");
    var orders = Entry("uses", "Orders");
    await page.ClickAsync(orders + " .ref-count");

    await page.ClickAsync(orders + " .ref-line");
    Assert.Contains("The source isn't loaded yet", await page.InnerTextAsync(orders + " .ref-peek"));
    Assert.Equal(1, await page.Locator(orders + " .ref-peek [data-load-folder]").CountAsync());
    Assert.Equal(1, await page.Locator(orders + " .ref-peek [data-peek-go]").CountAsync());
    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Empty(opened.Errors);
  }
}
