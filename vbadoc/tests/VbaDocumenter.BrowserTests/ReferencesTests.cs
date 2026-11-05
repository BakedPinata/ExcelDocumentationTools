using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Uses / Used by with references (step E1 of the feedback plan): each item once, whatever the kinds of link, and the
/// lines where it's referenced, which select what holds them and show the line in the source panel.
/// </summary>
[Collection("Browser")]
public class ReferencesTests(PageFixture fixture)
{
  static string Entry(string direction, string id) => $"#source-dependencies .dependency-list[data-direction='{direction}'] .ref-item:has(> details > summary > a[data-select='{id}'], > .ref-head > a[data-select='{id}'])";

  static async Task OpenDependencies(IPage page)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('source-dependencies').open")) await page.ClickAsync("#source-dependencies summary");
  }

  static Task<string[]> Lines(IPage page, string entry) =>
      page.Locator(entry + " .ref-line .ref-ln").AllInnerTextsAsync().ContinueWith(t => t.Result.ToArray());

  const string Marked = "#side-panel .code .line.target";

  [Fact]
  public async Task An_item_linked_in_two_ways_is_listed_once_with_both_kinds()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("ProgressReporter");
    await OpenDependencies(page);

    // ProgressReporter both uses OrderService and handles its events.
    Assert.Equal(1, await page.Locator("#source-dependencies [data-direction='uses'] a[data-select='OrderService']").CountAsync());
    Assert.Equal("events", await page.InnerTextAsync(Entry("uses", "OrderService") + " .edge-tag.events"));
    Assert.Equal("uses 2 · used by 1", await page.InnerTextAsync("#source-dependencies summary .muted")); // items, not links
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Opening_an_item_lists_its_lines_and_choosing_one_selects_what_holds_it_and_marks_the_line()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Logger");
    await OpenDependencies(page);
    var appInit = Entry("usedBy", "AppInit");

    await page.ClickAsync(appInit + " > details > summary .ref-count");
    Assert.Equal(["8", "13"], await Lines(page, appInit));
    Assert.Equal("Logger.LogInfo \"Settings loaded\"", await page.InnerTextAsync(appInit + " .ref-line[data-ref-line='13'] code"));

    await page.ClickAsync(appInit + " .ref-line[data-ref-line='13']");

    Assert.Equal("AppInit.LoadSettings", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(["13"], await page.Locator(Marked).EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.line)"));
    Assert.Contains("Logger.LogInfo", await page.InnerTextAsync(Marked));
    Assert.True(await page.EvaluateAsync<bool>(@"() => {
      const r = document.querySelector('#side-panel .code .line.target').getBoundingClientRect();
      return r.top >= 0 && r.bottom <= window.innerHeight;
    }"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_members_own_references_are_its_lines_and_choosing_one_keeps_it_selected()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("AppInit.LoadSettings");
    await OpenDependencies(page);
    var logInfo = Entry("uses", "Logger.LogInfo");

    await page.ClickAsync(logInfo + " > details > summary .ref-count");
    await page.ClickAsync(logInfo + " .ref-line[data-ref-line='13']");

    Assert.Equal("AppInit.LoadSettings", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator(Marked + "[data-line='13']").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Without_source_the_line_numbers_show_and_once_a_folder_is_loaded_their_text_does_too()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleFolder, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    await opened.SelectAsync("Logger");
    await OpenDependencies(page);
    var appInit = Entry("usedBy", "AppInit");
    await page.ClickAsync(appInit + " > details > summary .ref-count");
    Assert.Equal(["8", "13"], await Lines(page, appInit));
    Assert.Equal(0, await page.Locator(appInit + " .ref-line code").CountAsync());

    await page.SetInputFilesAsync("#load-folder", fixture.SampleFolder);
    await page.WaitForFunctionAsync("() => document.querySelector('#side-panel-body .line') !== null");
    await OpenDependencies(page);
    if (!await page.EvaluateAsync<bool>("s => document.querySelector(s).open", appInit + " > details"))
      await page.ClickAsync(appInit + " > details > summary .ref-count");
    Assert.Equal("Logger.LogInfo \"Application started\"", await page.InnerTextAsync(appInit + " .ref-line[data-ref-line='8'] code"));

    await page.ClickAsync(appInit + " .ref-line[data-ref-line='8']");
    Assert.Equal("AppInit.Startup", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator(Marked + "[data-line='8']").CountAsync());
    Assert.Empty(opened.Errors);
  }
}
