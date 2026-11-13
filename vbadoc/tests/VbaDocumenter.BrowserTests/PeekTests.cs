using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Peek (step L2 of the generalisation plan): a click on a line in Uses / Used by opens a preview of the code around
/// it, under the line, and leaves the selection, the views and the source below as they are. Go to in the preview, or
/// Ctrl+click on the line, goes there. Any number can be open, and Esc closes the last opened.
/// </summary>
[Collection("Browser")]
public class PeekTests(PageFixture fixture)
{
  static string Entry(string direction, string id) => $"#source-dependencies .dependency-list[data-direction='{direction}'] .ref-item:has(> details > summary > a[data-select='{id}'])";

  static string Line(string entry, int line) => $"{entry} .ref-line[data-ref-line='{line}']";

  /// <summary>The preview under a line: its list item's.</summary>
  static string Peek(string entry, int line) => $"{entry} li:has(> .ref-line[data-ref-line='{line}']) > .ref-peek";

  const string Marked = "#side-panel-body .code .line.target";

  static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

  static Task<string[]> PeekLines(IPage page, string peek) =>
      page.Locator(peek + " .peek-code .line").EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.peekLine)");

  /// <summary>Selects Logger and opens the lines where a module that uses it does.</summary>
  static async Task<string> OpenUsedBy(OpenedPage opened, string module)
  {
    var page = opened.Page;
    await opened.SelectAsync("Logger");
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('source-dependencies').open")) await page.ClickAsync("#source-dependencies summary");
    var entry = Entry("usedBy", module);
    await page.ClickAsync(entry + " > details > summary .ref-count");
    return entry;
  }

  [Fact]
  public async Task A_click_on_a_line_opens_a_preview_and_leaves_the_selection_and_the_source()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var appInit = await OpenUsedBy(opened, "AppInit");
    var source = await page.InnerTextAsync("#side-panel-body");
    var shown = await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

    await page.ClickAsync(Line(appInit, 13));

    var peek = Peek(appInit, 13);
    Assert.Equal("true", await page.GetAttributeAsync(Line(appInit, 13), "aria-expanded"));
    // AppInit.LoadSettings is lines 11 to 14: the preview doesn't run on into what's around it.
    Assert.Equal(["11", "12", "13", "14"], await PeekLines(page, peek));
    Assert.Equal("13", await page.GetAttributeAsync(peek + " .line.peek-target", "data-peek-line"));
    Assert.Contains("Logger.LogInfo \"Settings loaded\"", await page.InnerTextAsync(peek + " .line.peek-target"));
    Assert.True(await page.Locator(peek + " .tok-keyword").CountAsync() > 0);   // highlighted
    Assert.Equal("AppInit.LoadSettings", await page.InnerTextAsync(peek + " .ref-peek-where"));
    Assert.Equal(0, await page.Locator(peek + " [data-peek-whole]").CountAsync()); // there's no more of it to show

    Assert.Equal("Logger", await Selection(page));
    Assert.Equal(source, await page.InnerTextAsync("#side-panel-body"));
    Assert.Equal(0, await page.Locator(Marked).CountAsync());
    Assert.Equal(shown, await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()"));

    await page.ClickAsync(Line(appInit, 13));   // its line again closes it
    Assert.Equal(0, await page.Locator(peek).CountAsync());
    Assert.Equal("false", await page.GetAttributeAsync(Line(appInit, 13), "aria-expanded"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Go_to_and_Ctrl_click_select_what_holds_the_line_and_mark_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    var appInit = await OpenUsedBy(opened, "AppInit");

    await page.ClickAsync(Line(appInit, 13));
    await page.ClickAsync(Peek(appInit, 13) + " [data-peek-go]");
    Assert.Equal("AppInit.LoadSettings", await Selection(page));
    Assert.Equal(["13"], await page.Locator(Marked).EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.line)"));

    appInit = await OpenUsedBy(opened, "AppInit");
    await page.Locator(Line(appInit, 8)).ClickAsync(new() { Modifiers = [KeyboardModifier.Control] });
    Assert.Equal("AppInit.Startup", await Selection(page));
    Assert.Equal(["8"], await page.Locator(Marked).EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.line)"));
    Assert.Equal(0, await page.Locator(".ref-peek").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Any_number_can_be_open_and_Esc_closes_the_last_opened_before_it_clears_the_selection()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var appInit = await OpenUsedBy(opened, "AppInit");
    var service = Entry("usedBy", "OrderService");
    await page.ClickAsync(service + " > details > summary .ref-count");

    await page.ClickAsync(Line(appInit, 13));
    await page.ClickAsync(Line(appInit, 8));
    await page.ClickAsync(Line(service, 25));
    Assert.Equal(3, await page.Locator(".ref-peek").CountAsync());

    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(0, await page.Locator(Peek(service, 25)).CountAsync());
    Assert.Equal(2, await page.Locator(".ref-peek").CountAsync());
    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(0, await page.Locator(Peek(appInit, 8)).CountAsync());
    Assert.Equal(1, await page.Locator(Peek(appInit, 13)).CountAsync());
    Assert.Equal("Logger", await Selection(page));

    await page.Keyboard.PressAsync("Escape");
    Assert.Equal(0, await page.Locator(".ref-peek").CountAsync());
    Assert.Equal("Logger", await Selection(page));
    await page.Keyboard.PressAsync("Escape");   // nothing left to close, so it clears the selection
    Assert.Null(await Selection(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_preview_is_five_lines_either_side_and_can_show_the_whole_procedure()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var service = await OpenUsedBy(opened, "OrderService");
    var peek = Peek(service, 25);

    await page.ClickAsync(Line(service, 25));
    // OrderService.Process is lines 17 to 27.
    Assert.Equal(["20", "21", "22", "23", "24", "25", "26", "27"], await PeekLines(page, peek));

    await page.ClickAsync(peek + " [data-peek-whole]");
    Assert.Equal(Enumerable.Range(17, 11).Select(n => n.ToString()).ToArray(), await PeekLines(page, peek));
    Assert.Contains("Public Sub Process", await page.InnerTextAsync(peek + " .line[data-peek-line='17']"));
    Assert.Equal("25", await page.GetAttributeAsync(peek + " .line.peek-target", "data-peek-line"));

    await page.ClickAsync(peek + " [data-peek-whole]");
    Assert.Equal(8, (await PeekLines(page, peek)).Length);
    Assert.Equal("Logger", await Selection(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Without_the_source_the_preview_says_how_to_load_it_and_Go_to_still_works()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleFolder, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    var appInit = await OpenUsedBy(opened, "AppInit");
    var peek = Peek(appInit, 13);

    await page.ClickAsync(Line(appInit, 13));
    Assert.Contains("The source isn't loaded yet", await page.InnerTextAsync(peek));
    Assert.Equal(0, await page.Locator(peek + " .peek-code").CountAsync());
    Assert.Equal("Logger", await Selection(page));

    // Its Load folder opens the same file chooser as the top bar's.
    var chooser = await page.RunAndWaitForFileChooserAsync(() => page.ClickAsync(peek + " [data-load-folder]"));
    await chooser.SetFilesAsync(fixture.SampleFolder);
    await page.WaitForFunctionAsync("() => document.querySelector('#side-panel-body .line') !== null");

    // Loading drew the list again, with the lines' text; a preview now has the code.
    appInit = await OpenUsedBy(opened, "AppInit");
    await page.ClickAsync(Line(appInit, 13));
    Assert.Contains("Logger.LogInfo \"Settings loaded\"", await page.InnerTextAsync(peek + " .line.peek-target"));
    await page.ClickAsync(peek + " [data-peek-go]");
    Assert.Equal("AppInit.LoadSettings", await Selection(page));
    Assert.Empty(opened.Errors);
  }
}
