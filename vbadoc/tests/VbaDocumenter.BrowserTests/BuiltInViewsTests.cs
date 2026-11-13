using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Views built into the page (step L4 of the generalisation plan): a page made in a folder that holds
/// vbadoc-views.json lists the file's views in its Views menu, marked "in the page", for everyone who opens it. They
/// can't be changed there: Save over makes the person's own version, kept in the browser, which shows in its place
/// until it's deleted. Export writes them all, so the file can take the place of the one the page was made with.
/// </summary>
[Collection("Browser")]
public sealed class BuiltInViewsTests(PageFixture fixture) : IDisposable
{
  const string StoredViews = "vbadoc-views|SampleVba.xlsm";

  readonly string _folder = Directory.CreateTempSubdirectory("vbadoc-built-in-views-").FullName;

  public void Dispose() => Directory.Delete(_folder, recursive: true);

  static string ViewRow(string name) => $"#views-list li:has(.views-name:text-is('{name}'))";

  static Task<string[]> ViewNames(IPage page) =>
      page.Locator("#views-list .views-name").EvaluateAllAsync<string[]>("ns => ns.map(n => n.textContent)");

  static async Task OpenMenu(IPage page)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('views').open")) await page.ClickAsync("#views > summary");
  }

  static async Task OpenView(IPage page, string name)
  {
    await OpenMenu(page);
    await page.ClickAsync(ViewRow(name) + " .views-open");
  }

  static async Task<string[]> ClassBoxes(IPage page)
  {
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    return await page.Locator("#classes-svg g.class-box").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.module).sort()");
  }

  [Fact]
  public async Task The_pages_own_views_are_listed_for_anyone_who_opens_it_and_cant_be_edited_or_deleted()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;
    await OpenMenu(page);

    Assert.Equal(["Orders", "Old logging"], await ViewNames(page));
    Assert.True(await page.IsHiddenAsync("#views-none"));
    Assert.Equal(["in the page", "in the page"], await page.Locator("#views-list .views-tag").AllInnerTextsAsync());
    Assert.Contains("vbadoc-views.json", await page.GetAttributeAsync(ViewRow("Orders") + " .views-tag", "title"));
    Assert.Equal("The classes that handle an order", await page.InnerTextAsync(ViewRow("Orders") + " .views-description"));
    Assert.Equal(0, await page.Locator("#views-list [data-view-edit], #views-list [data-view-delete]").CountAsync());
    Assert.Equal(2, await page.Locator("#views-list [data-view-save]").CountAsync());
    Assert.False(await page.IsDisabledAsync("#views-export"));
    Assert.Null(await page.EvaluateAsync<string?>("key => localStorage.getItem(key)", StoredViews)); // nothing of the person's yet

    await OpenView(page, "Orders");
    Assert.Equal("true", await page.GetAttributeAsync("#tab-classes", "aria-selected"));
    Assert.Equal(["Order", "OrderService"], await ClassBoxes(page));
    Assert.Equal("· Orders", (await page.InnerTextAsync("#views-current")).Trim());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_that_mentions_items_the_workbook_no_longer_has_still_opens_and_lists_them()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;

    await OpenView(page, "Old logging");

    Assert.Contains("Retired", await page.InnerTextAsync("#page-notice-text"));
    Assert.StartsWith("Filtered: 1 of ", await page.InnerTextAsync("#view-graph .view-filter-note span"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Saving_over_one_makes_your_own_version_in_its_place_and_deleting_that_goes_back_to_the_pages()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;
    await OpenView(page, "Orders");
    await page.ClickAsync("#explorer-tree li.item:not(.favourites li):not(.widely-used-items li)[data-id='Logger'] > .tree-row > .tree-check");

    await OpenMenu(page);
    Assert.Equal("Save changes to “Orders”", await page.InnerTextAsync("#views-update"));
    Assert.Contains("your own version", await page.GetAttributeAsync("#views-update", "title"));
    await page.ClickAsync("#views-update");

    Assert.Equal(["Orders", "Old logging"], await ViewNames(page));   // in the page's view's place, not as well as it
    Assert.Equal("yours", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));
    Assert.Equal(1, await page.Locator(ViewRow("Orders") + " [data-view-edit]").CountAsync());

    // It's kept in the browser, and the page's view isn't.
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadoc");
    Assert.Equal(["Orders"], await page.EvaluateAsync<string[]>("key => JSON.parse(localStorage.getItem(key)).map(v => v.name)", StoredViews));
    await OpenView(page, "Orders");
    Assert.Equal(["Logger", "Order", "OrderService"], await ClassBoxes(page));

    await OpenMenu(page);
    Assert.Contains("go back to the one built into the page", await page.GetAttributeAsync(ViewRow("Orders") + " [data-view-delete]", "title"));
    await page.ClickAsync(ViewRow("Orders") + " [data-view-delete]");
    Assert.StartsWith("Deleted your version of “Orders”. The page's own shows again.", await page.InnerTextAsync("#views-status"));
    Assert.Equal("in the page", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));

    // Save over makes a version of your own again, and Undo takes it back.
    await page.ClickAsync(ViewRow("Orders") + " [data-view-save]");
    Assert.Equal("yours", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));
    await page.ClickAsync("#views-undo");
    Assert.Equal("in the page", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));

    await OpenView(page, "Orders");
    Assert.Equal(["Order", "OrderService"], await ClassBoxes(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_new_view_cant_take_the_name_of_one_in_the_page_but_can_be_saved_over_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn);
    var page = opened.Page;
    await page.ClickAsync("#tab-calls");
    await OpenMenu(page);
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", "orders");   // whatever its case
    await page.ClickAsync("#views-form-save");

    Assert.StartsWith("There's already a view called “orders”.", await page.InnerTextAsync("#views-error"));
    await page.ClickAsync("#views-save-over");
    Assert.Equal(["Orders", "Old logging"], await ViewNames(page));
    Assert.Equal("yours", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));
    Assert.Equal("The classes that handle an order", await page.InnerTextAsync(ViewRow("Orders") + " .views-description")); // kept
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_browsers_copy_that_is_the_same_as_the_pages_gives_way_to_it()
  {
    // As after exporting your views and running vbadoc again with the file in the folder: the browser still has them.
    var stored = "[" + PageFixture.OrdersView + ", { \"name\": \"Mine\", \"filter\": [\"Utils\"], \"hidden\": [], \"show\": { \"off\": [], \"radios\": {} } }]";
    var script = $"localStorage.setItem({System.Text.Json.JsonSerializer.Serialize(StoredViews)}, {System.Text.Json.JsonSerializer.Serialize(stored)});";
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn, initScript: script);
    var page = opened.Page;
    await OpenMenu(page);

    Assert.Equal(["Orders", "Old logging", "Mine"], await ViewNames(page));
    Assert.Equal("in the page", await page.InnerTextAsync(ViewRow("Orders") + " .views-tag"));
    Assert.Equal(0, await page.Locator(ViewRow("Mine") + " .views-tag").CountAsync());
    Assert.Equal(1, await page.Locator(ViewRow("Mine") + " [data-view-delete]").CountAsync());
    // And it's gone from the browser, so it can't come back over a newer page's view of that name.
    Assert.Equal(["Mine"], await page.EvaluateAsync<string[]>("key => JSON.parse(localStorage.getItem(key)).map(v => v.name)", StoredViews));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Export_writes_the_pages_views_with_your_own_so_the_file_can_replace_the_one_in_the_folder()
  {
    var stored = "[{ \"name\": \"Mine\", \"filter\": [\"Utils\"], \"hidden\": [], \"show\": { \"off\": [], \"radios\": {} } }]";
    var script = $"localStorage.setItem({System.Text.Json.JsonSerializer.Serialize(StoredViews)}, {System.Text.Json.JsonSerializer.Serialize(stored)});";
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ViewsBuiltIn, initScript: script);
    var page = opened.Page;
    await OpenMenu(page);

    var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#views-export"));
    var file = Path.Combine(_folder, "vbadoc-views.json");
    await download.SaveAsAsync(file);

    Assert.Equal("Exported 3 views as vbadoc-views.json. In the page's folder, vbadoc builds them into the page the next time it runs.",
        await page.InnerTextAsync("#views-status"));
    var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    Assert.Equal(["Orders", "Old logging", "Mine"], json["views"]!.AsArray().Select(v => (string)v!["name"]!));
    Assert.Equal("SampleVba.xlsm", (string)json["workbook"]!);

    // The tool reads what the page wrote.
    var read = VbaDocumenter.Core.Output.HtmlPageWriter.ReadViews(_folder, "SampleVba.xlsm");
    Assert.Equal(3, read.Count);
    Assert.Null(read.Warning);
    Assert.Empty(opened.Errors);
  }
}
