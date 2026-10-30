namespace VbaDocumenter.BrowserTests;

/// <summary>The source panel in both modes, with highlighting.</summary>
[Collection("Browser")]
public class SourcePanelTests(PageFixture fixture)
{
  // In the exported Order.cls the 4-line class header comes first, so Describe (lines 38-40 in the
  // fixture) is at lines 42-44 of the file and of the page.
  [Fact]
  public async Task A_member_shows_only_its_own_lines()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    await opened.SelectAsync("Order.Describe");

    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
    Assert.Contains("Describe = m_Id", await opened.Page.InnerTextAsync("#source-body"));
    Assert.Contains("Public Function Describe() As String", await opened.Page.InnerTextAsync("#source-header .signature"));
  }

  [Fact]
  public async Task A_member_can_be_shown_in_the_whole_module()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");

    await page.ClickAsync("#toggle-context");

    Assert.Equal(Enumerable.Range(1, 44), await opened.ShownLinesAsync());
    var marked = await page.Locator("#source-body .line.in-member .ln").AllInnerTextsAsync();
    Assert.Equal(["42", "43", "44"], marked);

    await page.ClickAsync("#toggle-context");
    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
  }

  [Fact]
  public async Task A_module_shows_its_whole_source_including_the_class_header()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    await opened.SelectAsync("Order");

    Assert.Equal(44, (await opened.ShownLinesAsync()).Length);
    Assert.StartsWith("1VERSION 1.0 CLASS", (await opened.Page.InnerTextAsync("#source-body .line")).Replace("\n", ""));
  }

  [Fact]
  public async Task Member_header_shows_role_unreferenced_and_late_bound_details()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await opened.SelectAsync("Main.ProcessOrders");
    Assert.Contains("Macro", await page.InnerTextAsync("#source-header .badges"));
    Assert.Contains("legacy.ExportAll", await page.InnerTextAsync("#source-header .late-bound"));

    await opened.SelectAsync("Utils.UnusedHelper");
    Assert.Contains("Unreferenced", await page.InnerTextAsync("#source-header .badges"));

    await opened.SelectAsync("Sheet1.btnRun_Click");
    Assert.Contains("not in the VBA project", await page.InnerTextAsync("#source-header .badges"));

    // The module link in the header selects the module.
    await page.ClickAsync("#source-header a[data-select]");
    Assert.Equal("Sheet1", await page.EvaluateAsync<string>("() => window.vbadoc.selection()"));
  }

  [Fact]
  public async Task Highlights_keywords_names_types_strings_comments_and_numbers()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Main");

    async Task<IReadOnlyList<string>> Tokens(string kind) => await page.Locator($"#source-body .tok-{kind}").AllInnerTextsAsync();

    Assert.Contains("Sub", await Tokens("keyword"));
    Assert.Contains("ProcessOrders", await Tokens("name"));
    Assert.Contains("Factorial", await Tokens("name"));
    Assert.Contains("OrderService", await Tokens("type"));
    Assert.Contains("\"Legacy.Exporter\"", await Tokens("string"));
    Assert.Contains("' nothing", await Tokens("comment"));
    Assert.Contains("5", await Tokens("number"));
    // Member names after a dot aren't keywords, even when they're spelled like one.
    Assert.DoesNotContain("Summarise", await Tokens("keyword"));
  }

  [Fact]
  public async Task Highlighting_colours_follow_the_theme()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await opened.SelectAsync("Main");
    var keywordColour = "() => getComputedStyle(document.querySelector('.tok-keyword')).color";

    var dark = await page.EvaluateAsync<string>(keywordColour);
    await page.ClickAsync("#theme-toggle");

    Assert.NotEqual(dark, await page.EvaluateAsync<string>(keywordColour));
  }

  [Fact]
  public async Task Folder_mode_asks_for_the_folder_then_shows_source_from_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleFolder);
    var page = opened.Page;
    await opened.SelectAsync("Order.Describe");

    Assert.Contains("isn't loaded yet", await page.InnerTextAsync("#source-body"));
    Assert.True(await page.IsVisibleAsync("#load-folder-prompt"));

    await page.SetInputFilesAsync("#load-folder", fixture.SampleFolder);
    await page.WaitForFunctionAsync("() => document.querySelector('#source-body .line') !== null");

    Assert.Equal("Loaded 12 file(s)", await page.InnerTextAsync("#source-status-text"));
    Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Folder_mode_warns_when_files_changed_since_the_page_was_made()
  {
    var folder = fixture.CopyOf(fixture.SampleFolder);
    File.AppendAllText(Path.Combine(folder, "Utils.bas"), "' edited after export\r\n");

    await using var opened = await OpenedPage.OpenAsync(fixture, folder);
    var page = opened.Page;
    await page.SetInputFilesAsync("#load-folder", folder);
    await page.WaitForFunctionAsync("() => document.querySelector('#source-status-text').textContent.startsWith('1 file')");

    var status = await page.InnerTextAsync("#source-status-text");
    Assert.Contains("changed since this page was made", status);
    Assert.Contains("Utils.bas", status);
  }

  [Fact]
  public async Task Folder_mode_decodes_files_with_the_project_code_page()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.RealFolder);
    var page = opened.Page;
    await page.SetInputFilesAsync("#load-folder", fixture.RealFolder);
    await page.WaitForFunctionAsync("() => document.querySelector('#source-status-text').textContent.startsWith('Loaded')");

    await opened.SelectAsync("Summary_graph");

    Assert.Contains("Redirigés_Smry_FormPil", await page.InnerTextAsync("#source-body"));
  }
}
