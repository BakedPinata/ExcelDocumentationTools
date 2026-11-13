using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Uses / Used by with references (FR-HTML-19, step E1 of the feedback plan): each query's lines that name another.
/// Going to one (Ctrl+click, since a click peeks: see <see cref="PeekTests"/>) selects the query holding it and shows
/// the line in the details panel's source.
/// </summary>
[Collection("Browser")]
public sealed class ReferencesTests(PageFixture fixture)
{
  private static string Entry(string list, string id) =>
    $"#details-{list} .ref-item:has(> details > summary > a[data-select='{id}'])";

  private const string Marked = "#side-panel .code .line.target";

  private static readonly LocatorClickOptions CtrlClick = new() { Modifiers = [KeyboardModifier.Control] };

  [Fact]
  public async Task FR_HTML_19_a_used_querys_lines_are_in_the_selection_and_going_to_one_marks_it_there()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Sales Report");
    var orders = Entry("uses", "Orders");

    await page.ClickAsync(orders + " .ref-count");
    Assert.Equal(["2"], await page.Locator(orders + " .ref-ln").AllInnerTextsAsync());
    Assert.Contains("Orders", await page.InnerTextAsync(orders + " .ref-line code"));

    await page.Locator(orders + " .ref-line").ClickAsync(CtrlClick);
    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Equal(1, await page.Locator(Marked + "[data-line='2']").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_19_a_using_querys_lines_are_in_that_query_and_going_to_one_selects_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Q1.Summary");
    var report = Entry("used-by", "Sales Report");

    await page.ClickAsync(report + " .ref-count");
    await page.Locator(report + " .ref-line[data-ref-line='3']").ClickAsync(CtrlClick);

    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Contains("Q1.Summary", await page.InnerTextAsync(Marked + "[data-line='3']"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task FR_HTML_19_from_a_loaded_folder_the_lines_have_their_text_and_mark_the_source()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Sales Report");
    var orders = Entry("uses", "Orders");
    await page.ClickAsync(orders + " .ref-count");
    Assert.Equal(0, await page.Locator(orders + " .ref-line code").CountAsync()); // only the number, until the source is loaded

    await page.SetInputFilesAsync("#load-folder", fixture.SourceFolder);
    await page.WaitForFunctionAsync("() => document.getElementById('source-status-text').textContent.startsWith('Loaded')");
    await page.WaitForFunctionAsync("() => document.querySelector('#details-source .line') !== null");
    if (!await page.EvaluateAsync<bool>("s => document.querySelector(s).open", orders + " > details"))
      await page.ClickAsync(orders + " .ref-count");
    Assert.Contains("Orders", await page.InnerTextAsync(orders + " .ref-line code"));

    await page.Locator(orders + " .ref-line").ClickAsync(CtrlClick);
    Assert.Equal(1, await page.Locator(Marked + "[data-line='2']").CountAsync());
    Assert.Empty(opened.Errors);
  }
}
