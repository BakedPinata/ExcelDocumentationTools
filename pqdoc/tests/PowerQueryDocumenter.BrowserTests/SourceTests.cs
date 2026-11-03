using System.Text;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>A query's M code in the details panel (FR-SRC-1 to FR-SRC-3): embedded, loaded from a folder, or neither.</summary>
[Collection("Browser")]
public sealed class SourceTests(PageFixture fixture)
{
  private static async Task<string[]> Texts(OpenedPage opened, string selector) =>
    (await opened.Page.Locator(selector).AllTextContentsAsync()).ToArray();

  private static Task WaitForStatus(OpenedPage opened, string start) =>
    opened.Page.WaitForFunctionAsync("start => document.getElementById('source-status-text').textContent.startsWith(start)", start);

  private static async Task LoadFolder(OpenedPage opened, string folder, string expectedStatusStart)
  {
    await opened.Page.SetInputFilesAsync("#load-folder", folder);
    await WaitForStatus(opened, expectedStatusStart);
  }

  [Fact]
  public async Task Embedded_source_shows_every_line_numbered_and_highlighted()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;

    await opened.SelectAsync("Orders");

    var expected = File.ReadAllLines(Path.Combine(fixture.SourceFolder, "Orders.m"));
    Assert.Equal(expected, (await Texts(opened, "#details-source .lt")).Select(t => t.TrimEnd()).ToArray());
    Assert.Equal(Enumerable.Range(1, expected.Length).Select(n => n.ToString()).ToArray(), await Texts(opened, "#details-source .ln"));
    Assert.Equal(["let", "type", "in"], await Texts(opened, "#details-source .tok-keyword"));
    Assert.Equal(["#\"Sales Region Map\""], await Texts(opened, "#details-source .tok-name"));
    Assert.Contains(await Texts(opened, "#details-source .tok-string"), s => s == "\"See the Customers query for details\"");
    Assert.Contains("Orders.m", await page.InnerTextAsync("#side-panel-body h3:has-text('Source')"));
    Assert.False(await page.IsVisibleAsync("#load-folder-label"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Embedded_source_is_only_ever_shown_as_text_and_is_found_whatever_the_query_is_called()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.AwkwardSourcePage);
    var page = opened.Page;

    foreach (var name in new[] { PageFixture.HostileQuery, "It's \"quoted\"", "Région & Co", "__proto__" })
    {
      await opened.SelectAsync(name);
      Assert.Equal(4, await page.Locator("#details-source .line").CountAsync());
      Assert.Contains("</pre><script>window.injected = true</script>", await page.InnerTextAsync("#details-source"));
    }

    Assert.False(await page.EvaluateAsync<bool>("() => window.injected === true"));
    Assert.Equal(0, await page.Locator("#details-source script").CountAsync());
    Assert.Empty(opened.Errors);
  }

  // Each case is lines separated by \n, and the HTML expected for each, also separated by \n.
  [Theory]
  [InlineData("let x = 1 in x",
    "<span class=\"tok-keyword\">let</span> x = <span class=\"tok-number\">1</span> <span class=\"tok-keyword\">in</span> x")]
  [InlineData("#date(2024, 1, 31) & #table({}, {}) & #shared & #thing",
    "<span class=\"tok-keyword\">#date</span>(<span class=\"tok-number\">2024</span>, <span class=\"tok-number\">1</span>, " +
    "<span class=\"tok-number\">31</span>) &amp; <span class=\"tok-keyword\">#table</span>({}, {}) &amp; " +
    "<span class=\"tok-keyword\">#shared</span> &amp; #thing")]
  [InlineData("#\"Sales Data\"[Region]",
    "<span class=\"tok-name\">#\"Sales Data\"</span>[Region]")]
  [InlineData("\"say \"\"hi\"\" // not a comment\" // a comment with \"text\"",
    "<span class=\"tok-string\">\"say \"\"hi\"\" // not a comment\"</span> <span class=\"tok-comment\">// a comment with \"text\"</span>")]
  [InlineData("type nullable number, Int64.Type, Table.AddColumn",
    "<span class=\"tok-keyword\">type</span> <span class=\"tok-type\">nullable</span> <span class=\"tok-type\">number</span>, " +
    "<span class=\"tok-type\">Int64.Type</span>, Table.AddColumn")]
  [InlineData("{1..3} & 0xFF & 1.5e-3 & .5 & Q1.Total",
    "{<span class=\"tok-number\">1</span>..<span class=\"tok-number\">3</span>} &amp; <span class=\"tok-number\">0xFF</span> &amp; " +
    "<span class=\"tok-number\">1.5e-3</span> &amp; <span class=\"tok-number\">.5</span> &amp; Q1.Total")]
  [InlineData("a < b and c <> \"<b>\"",
    "a &lt; b <span class=\"tok-keyword\">and</span> c &lt;&gt; <span class=\"tok-string\">\"&lt;b&gt;\"</span>")]
  [InlineData("x = /* one\nstill */ 2\nnext",
    "x = <span class=\"tok-comment\">/* one</span>\n<span class=\"tok-comment\">still */</span> <span class=\"tok-number\">2</span>\nnext")]
  [InlineData("t = \"one\ntwo\" & y\n\"\"\nlet",
    "t = <span class=\"tok-string\">\"one</span>\n<span class=\"tok-string\">two\"</span> &amp; y\n<span class=\"tok-string\">\"\"</span>\n" +
    "<span class=\"tok-keyword\">let</span>")]
  [InlineData("\"unfinished \"\"\n\n\"\"\" in",
    "<span class=\"tok-string\">\"unfinished \"\"</span>\n<span class=\"tok-string\"></span>\n" +
    "<span class=\"tok-string\">\"\"\"</span> <span class=\"tok-keyword\">in</span>")]
  public async Task Highlighting_marks_each_kind_of_token(string lines, string expected)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);

    var html = await opened.Page.EvaluateAsync<string[]>("lines => window.MHighlight.highlightLines(lines)", lines.Split('\n'));

    Assert.Equal(expected.Split('\n'), html);
  }

  [Fact]
  public async Task A_very_long_line_does_not_slow_the_page_down()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.LongLinePage);
    var page = opened.Page;

    // Selecting highlights the query and lays out the panel; reading offsetHeight makes the browser finish the layout.
    var milliseconds = await page.EvaluateAsync<double>(
      "() => { const start = performance.now(); window.pqdoc.select('Enter Data'); document.body.offsetHeight; return performance.now() - start; }");

    Assert.Equal(4, await page.Locator("#details-source .line").CountAsync());
    var longLine = await page.Locator("#details-source .line[data-line='2'] .tok-string").TextContentAsync();
    Assert.Equal(PageFixture.LongLineLength + 2, longLine!.Length);
    Assert.True(milliseconds < 1000, $"Showing the query took {milliseconds:F0} ms.");
  }

  [Fact]
  public async Task Folder_mode_asks_for_the_folder_then_shows_source_from_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    Assert.Equal("Source not loaded", await page.InnerTextAsync("#source-status-text"));
    Assert.Contains("isn't loaded yet", await page.InnerTextAsync("#details-source"));

    await LoadFolder(opened, fixture.SourceFolder, "Loaded");

    Assert.Equal("Loaded 11 file(s)", await page.InnerTextAsync("#source-status-text"));
    Assert.Equal(File.ReadAllLines(Path.Combine(fixture.SourceFolder, "Orders.m")).Length, await page.Locator("#details-source .line").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_panels_Load_folder_button_opens_the_folder_picker()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage);
    var page = opened.Page;
    await opened.SelectAsync("Source");

    var chooser = await page.RunAndWaitForFileChooserAsync(() => page.ClickAsync("#load-folder-prompt"));
    await chooser.SetFilesAsync(fixture.SourceFolder);
    await WaitForStatus(opened, "Loaded");

    Assert.True(await page.Locator("#details-source .line").CountAsync() > 0);
  }

  [Fact]
  public async Task Folder_mode_warns_when_a_file_changed_since_the_page_was_made()
  {
    var folder = fixture.CopyOfSources();
    File.AppendAllText(Path.Combine(folder, "Orders.m"), "\r\n// edited after the page was made\r\n");
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage);
    var page = opened.Page;

    await LoadFolder(opened, folder, "1 file");

    var status = await page.InnerTextAsync("#source-status-text");
    Assert.Contains("changed since this page was made", status);
    Assert.Contains("Run pqdoc again", status);
    Assert.Contains("Orders.m", status);
    Assert.Contains("warning", await page.GetAttributeAsync("#source-status-text", "class") ?? "");
  }

  [Fact]
  public async Task Folder_mode_names_missing_files_and_says_when_none_are_there()
  {
    var folder = fixture.CopyOfSources();
    File.Delete(Path.Combine(folder, "Source.m"));
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderPage);
    var page = opened.Page;

    await LoadFolder(opened, folder, "Loaded");
    Assert.Equal("Loaded 10 file(s); missing: Source.m", await page.InnerTextAsync("#source-status-text"));

    var empty = Path.Combine(folder, "empty");
    Directory.CreateDirectory(empty);
    File.WriteAllText(Path.Combine(empty, "readme.txt"), "not a query");
    await LoadFolder(opened, empty, "No .m files");
  }

  [Fact]
  public async Task Folder_mode_reads_UTF16_files_and_checks_them_byte_for_byte()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EncodingPage);
    var page = opened.Page;

    await LoadFolder(opened, fixture.EncodingFolder, "Loaded");

    // No "changed" warning, so the hash was taken over the same bytes, byte order mark and all.
    Assert.Equal("Loaded 2 file(s)", await page.InnerTextAsync("#source-status-text"));
    foreach (var query in new[] { "Little", "Big" })
    {
      await opened.SelectAsync(query);
      Assert.Equal("\"Zürich\" & \"Köln\"", await page.InnerTextAsync("#details-source .lt"));
    }
  }

  [Fact]
  public async Task Without_source_the_page_offers_none()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await opened.SelectAsync("Orders");

    Assert.False(await page.IsVisibleAsync("#source-status"));
    Assert.Equal(0, await page.Locator("#details-source").CountAsync());
    Assert.DoesNotContain(await Texts(opened, "#side-panel-body h3"), h => h.StartsWith("Source", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("dark")]
  [InlineData("light")]
  public async Task Every_token_has_its_own_colour_in_both_themes(string theme)
  {
    await using var opened = await OpenedPage.OpenAsync(
      fixture, fixture.EmbeddedPage, theme == "light" ? Microsoft.Playwright.ColorScheme.Light : Microsoft.Playwright.ColorScheme.Dark);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    var colours = await page.EvaluateAsync<string[]>(
      "() => ['keyword', 'string', 'comment', 'number', 'name', 'type'].map(kind => { const span = document.createElement('span'); " +
      "span.className = 'tok-' + kind; document.querySelector('#details-source .code').appendChild(span); return getComputedStyle(span).color; })");
    var text = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('#details-source .lt')).color");

    Assert.Equal(colours.Length, colours.Distinct().Count());
    Assert.DoesNotContain(text, colours);
  }
}
