using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Saved views (step G1 of the feedback plan), which are the shell's, as vbadoc's are: here, that a view keeps the
/// graph's filter, hidden queries and dragged queries, and that the views file is pqdoc's own.
/// </summary>
[Collection("Browser")]
public sealed class ViewsTests(PageFixture fixture) : IDisposable
{
  private readonly string folder = Directory.CreateTempSubdirectory("pqdoc-views-").FullName;

  public void Dispose() => Directory.Delete(this.folder, recursive: true);

  private static Task<double[]> Position(IPage page, string id) =>
    page.EvaluateAsync<double[]>("id => { const p = window.pqdocGraph.position(id); return [Math.round(p.x), Math.round(p.y)]; }", id);

  private static Task WaitUntilStill(IPage page) =>
    page.WaitForFunctionAsync("() => !window.pqdocGraph.moving()", null, new() { Timeout = 20000 });

  private static Task<string[]> Shown(OpenedPage opened) => opened.DrawnIdsAsync("#graph-svg g.node:not(.out-of-focus)");

  private static async Task<ILocator> Row(OpenedPage opened, string id)
  {
    var escaped = await opened.Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return opened.Page.Locator($"#explorer-tree li.item:not(.favourites li):not(.hidden-items li):not(.widely-used-items li)[data-id='{escaped}'] > .tree-row");
  }

  private static async Task OpenMenu(IPage page)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('views').open"))
    {
      await page.ClickAsync("#views > summary");
    }
  }

  private static async Task SaveAs(IPage page, string name)
  {
    await OpenMenu(page);
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", name);
    await page.ClickAsync("#views-form-save");
  }

  private static async Task OpenView(IPage page, string name)
  {
    await OpenMenu(page);
    await page.ClickAsync($"#views-list li:has(.views-name:text-is('{name}')) .views-open");
  }

  /// <summary>Drags a query by (dx, dy) screen pixels in small steps, like a person would.</summary>
  private static async Task Drag(IPage page, ILocator target, float dx, float dy)
  {
    var box = (await target.BoundingBoxAsync())!;
    var x = box.X + box.Width / 2;
    var y = box.Y + box.Height / 2;
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (var i = 1; i <= 10; i++)
    {
      await page.Mouse.MoveAsync(x + dx * i / 10, y + dy * i / 10);
    }

    await page.Mouse.UpAsync();
  }

  [Fact]
  public async Task A_view_keeps_the_filter_hidden_queries_and_where_queries_were_dragged()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    foreach (var id in new[] { "Source", "Orders", "Customers", "Sales Report" })
    {
      await (await Row(opened, id)).Locator(".tree-check").ClickAsync();
    }

    var customers = await Row(opened, "Customers");
    await customers.HoverAsync();
    await customers.Locator(".hide-toggle").ClickAsync();
    await WaitUntilStill(page);
    await Drag(page, (await opened.NodeAsync("Orders")).Locator(".shape"), 60, 90);
    await WaitUntilStill(page);
    var orders = await Position(page, "Orders");
    var source = await Position(page, "Source");
    await SaveAs(page, "Orders flow");
    await page.Keyboard.PressAsync("Escape");

    await page.ClickAsync("#explorer-filter-bar [data-filter-clear]");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await page.ClickAsync("#graph-reset");
    await WaitUntilStill(page);
    Assert.Equal(0, await page.Locator("#graph-svg g.node.pinned").CountAsync());

    await OpenView(page, "Orders flow");
    await WaitUntilStill(page);
    Assert.Equal(["Orders", "Sales Report", "Source"], await Shown(opened));
    Assert.Equal(["Customers"], await page.EvaluateAsync<string[]>("() => window.pqdoc.hidden()"));
    Assert.Equal(orders, await Position(page, "Orders"));
    Assert.Equal(source, await Position(page, "Source"));
    Assert.Equal(["Orders"], await opened.DrawnIdsAsync("#graph-svg g.node.pinned"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_saved_before_anything_was_moved_opens_with_the_starting_layout()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    var start = await Position(page, "Customers");
    await SaveAs(page, "Start");
    await page.Keyboard.PressAsync("Escape");
    await Drag(page, (await opened.NodeAsync("Customers")).Locator(".shape"), 60, 90);
    await WaitUntilStill(page);
    Assert.NotEqual(start, await Position(page, "Customers"));

    await OpenView(page, "Start");
    await WaitUntilStill(page);
    Assert.Equal(start, await Position(page, "Customers"));
    Assert.Equal(0, await page.Locator("#graph-svg g.node.pinned").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Views_export_as_pqdocs_file_import_into_another_browser_and_vbadocs_file_is_refused()
  {
    var file = Path.Combine(this.folder, "pqdoc-views.json");
    double[] moved;
    await using (var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage))
    {
      var page = opened.Page;
      await Drag(page, (await opened.NodeAsync("Customers")).Locator(".shape"), 60, 90);
      await WaitUntilStill(page);
      moved = await Position(page, "Customers");
      await SaveAs(page, "Moved");
      var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#views-export"));
      Assert.Equal("pqdoc-views.json", download.SuggestedFilename);
      await download.SaveAsAsync(file);
      Assert.Empty(opened.Errors);
    }

    var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    Assert.Equal("pqdoc", (string)json["tool"]!);
    Assert.Equal("Moved", (string)json["views"]![0]!["name"]!);
    Assert.Equal(["Customers"], json["views"]![0]!["graph"]!["pinned"]!.AsArray().Select(n => (string)n!));

    await using var other = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var fresh = other.Page;
    await OpenMenu(fresh);
    await fresh.SetInputFilesAsync("#views-import", file);
    await fresh.WaitForFunctionAsync("() => document.getElementById('views-status').textContent !== ''");
    Assert.Equal("Imported 1 view from pqdoc-views.json.", await fresh.InnerTextAsync("#views-status"));
    await OpenView(fresh, "Moved");
    await WaitUntilStill(fresh);
    Assert.Equal(moved, await Position(fresh, "Customers"));

    var vbadocFile = Path.Combine(this.folder, "vbadoc-views.json");
    File.WriteAllText(vbadocFile, """{ "formatVersion": 1, "tool": "vbadoc", "views": [{ "name": "Modules" }] }""");
    await OpenMenu(fresh);
    await fresh.SetInputFilesAsync("#views-import", vbadocFile);
    await fresh.WaitForFunctionAsync("() => document.getElementById('views-status').textContent.includes('vbadoc')");
    Assert.Equal("vbadoc-views.json holds vbadoc's views, not pqdoc's.", await fresh.InnerTextAsync("#views-status"));
    Assert.Equal(["Moved"], await fresh.Locator("#views-list .views-name").AllTextContentsAsync());
    Assert.Empty(other.Errors);
  }
}
