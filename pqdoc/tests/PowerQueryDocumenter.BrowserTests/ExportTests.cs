using ExcelDocumenter.Shared.Importing;
using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The Export tab (step H2 of the feedback plan), which is the shell's, as vbadoc's is: here, that it works from pqdoc's
/// queries, and that the graph carries on beside it.
/// </summary>
[Collection("Browser")]
public sealed class ExportTests(PageFixture fixture) : IDisposable
{
  private readonly string folder = Directory.CreateTempSubdirectory("pqdoc-export-").FullName;

  public void Dispose() => Directory.Delete(this.folder, recursive: true);

  private static async Task<string> Box(IPage page, string id) =>
    "#export-tree input[data-export-item='" + await page.EvaluateAsync<string>("id => CSS.escape(id)", id) + "']";

  private static Task<string[]> Listed(IPage page, string heading) => page.EvaluateAsync<string[]>(@"heading => {
    const title = [...document.querySelectorAll('#export-plan h2')].find(h => h.textContent.startsWith(heading));
    return title ? [...title.nextElementSibling.querySelectorAll('li')].map(li => li.querySelector('strong, .link-button').textContent) : [];
  }", heading);

  [Fact]
  public async Task A_query_brings_what_it_uses_and_the_manifest_is_pqdocs_own()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    await page.ClickAsync("#tab-export");
    Assert.Contains("Tick the queries", await page.InnerTextAsync("#export-plan"));

    await page.CheckAsync(await Box(page, "Chain_E"));

    Assert.Equal(["Chain_E"], await Listed(page, "Chosen"));
    Assert.Equal(["Chain_A", "Chain_B", "Chain_C", "Chain_D"], (await Listed(page, "Comes along")).Where(id => id.StartsWith("Chain", StringComparison.Ordinal)));
    Assert.Equal(0, await page.Locator("#export-plan [data-export-reference]").CountAsync());

    var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#export-download"));
    Assert.Equal("pqdoc-export.json", download.SuggestedFilename);
    var path = Path.Combine(this.folder, "pqdoc-export.json");
    await download.SaveAsAsync(path);

    var manifest = ExportManifest.Parse(File.ReadAllText(path), "pqdoc-export.json", "pqdoc");
    Assert.Equal(["Chain_E"], manifest.Items);
    Assert.Equal("Queries.xlsx", manifest.Workbook);
    Assert.Equal("pqdoc import \"Queries.xlsx\" --manifest pqdoc-export.json --bundle Shared", await page.InnerTextAsync("#export-command"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task What_a_query_reads_from_the_workbook_is_listed_to_check()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.EmbeddedPage);
    var page = opened.Page;
    var withNeeds = await page.EvaluateAsync<string?>("() => (window.pqdoc.data.queries.find(q => q.needs.length > 0) || {}).id || null");
    Assert.NotNull(withNeeds);
    var expected = await page.EvaluateAsync<string[]>("id => window.pqdoc.data.queries.find(q => q.id === id).needs.map(n => n.what)", withNeeds);

    await page.ClickAsync("#tab-export");
    await page.CheckAsync(await Box(page, withNeeds));

    Assert.Superset(expected.ToHashSet(), (await Listed(page, "To check in the target")).ToHashSet());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_groups_are_the_explorers_and_a_group_picks_its_queries()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    await page.ClickAsync("#tab-export");

    await page.CheckAsync("#export-tree li.export-group:has(> label .export-name:text-is('Reference')) > label input");

    Assert.NotEmpty(await Listed(page, "Chosen"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_graph_carries_on_beside_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.WorkbookPage);
    var page = opened.Page;
    await page.WaitForFunctionAsync("() => !window.pqdocGraph.moving()", null, new() { Timeout = 20000 });
    var nodes = await page.EvaluateAsync<int>("() => window.pqdocGraph.nodeCount()");

    await page.ClickAsync("#tab-export");
    Assert.True(await page.IsHiddenAsync("#view-graph"));
    await opened.SelectAsync("Sales Data"); // made while the graph is hidden

    await page.ClickAsync("#tab-graph");
    Assert.True(await page.IsVisibleAsync("#graph-svg"));
    Assert.Equal(nodes, await page.EvaluateAsync<int>("() => window.pqdocGraph.nodeCount()"));
    Assert.Equal(["Sales Data"], await opened.DrawnIdsAsync("#graph-svg g.node.selected"));
    Assert.Empty(opened.Errors);
  }
}
