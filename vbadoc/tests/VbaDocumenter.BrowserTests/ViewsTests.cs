using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Saved views (step G1 of the feedback plan): the Views menu saves the page's arrangement as a named, described view,
/// opens it again, renames and deletes it, and carries views through a file with Export and Import. A file that
/// mentions items the workbook no longer has still loads, and lists them.
/// </summary>
[Collection("Browser")]
public sealed class ViewsTests(PageFixture fixture) : IDisposable
{
  readonly string _folder = Directory.CreateTempSubdirectory("vbadoc-views-").FullName;

  public void Dispose() => Directory.Delete(_folder, recursive: true);

  static string Row(string id) => $"#explorer-tree li.item:not(.favourites li):not(.hidden-items li)[data-id='{id}'] > .tree-row";

  static string ViewRow(string name) => $"#views-list li:has(.views-name:text-is('{name}'))";

  static Task<string[]> GraphNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static Task<string[]> ViewNames(IPage page) =>
      page.Locator("#views-list .views-name").EvaluateAllAsync<string[]>("ns => ns.map(n => n.textContent)");

  static Task<double[]> Position(IPage page, string id) =>
      page.EvaluateAsync<double[]>("id => { const p = window.vbadocGraph.position(id); return [Math.round(p.x), Math.round(p.y)]; }", id);

  static Task WaitUntilStill(IPage page) =>
      page.WaitForFunctionAsync("() => window.vbadocGraph.nodeCount() > 0 && !window.vbadocGraph.moving()", null, new() { Timeout = 30000 });

  static async Task OpenMenu(IPage page)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('views').open")) await page.ClickAsync("#views > summary");
  }

  static async Task SaveAs(IPage page, string name, string description = "")
  {
    await OpenMenu(page);
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", name);
    await page.FillAsync("#views-description", description);
    await page.ClickAsync("#views-form-save");
  }

  static async Task OpenView(IPage page, string name)
  {
    await OpenMenu(page);
    await page.ClickAsync(ViewRow(name) + " .views-open");
  }

  static async Task HideFromRow(IPage page, string id)
  {
    await page.HoverAsync(Row(id));
    await page.ClickAsync(Row(id) + " .hide-toggle");
  }

  /// <summary>Drags from the middle of an element by (dx, dy) screen pixels in small steps, like a person would.</summary>
  static async Task Drag(IPage page, string selector, float dx, float dy)
  {
    var box = (await page.Locator(selector).BoundingBoxAsync())!;
    float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    for (int i = 1; i <= 10; i++) await page.Mouse.MoveAsync(x + dx * i / 10, y + dy * i / 10);
    await page.Mouse.UpAsync();
  }

  string WriteFile(string name, string text)
  {
    var path = Path.Combine(_folder, name);
    File.WriteAllText(path, text);
    return path;
  }

  [Fact]
  public async Task A_view_keeps_the_filter_hidden_items_Show_choices_tab_and_graph_and_opening_it_brings_them_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    Assert.Equal("Views", await page.TextContentAsync("#views > summary"));
    await OpenMenu(page);
    Assert.True(await page.IsVisibleAsync("#views-none"));
    Assert.True(await page.IsDisabledAsync("#views-export"));
    await page.Keyboard.PressAsync("Escape");

    foreach (var id in new[] { "Main", "Order", "OrderService", "Logger" }) await page.ClickAsync(Row(id) + " > .tree-check");
    await HideFromRow(page, "Logger");
    await page.ClickAsync("#explorer-show summary");
    await page.UncheckAsync("#explorer-show [data-choice='private']");
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("[data-level='members']");
    await WaitUntilStill(page);
    await Drag(page, "#graph-svg g.node[data-id='Main.ProcessOrders'] path.shape", 120, 60);
    await WaitUntilStill(page);
    var nodes = await GraphNodes(page);
    var moved = await Position(page, "Main.ProcessOrders");
    var other = await Position(page, nodes.First(id => id != "Main.ProcessOrders"));

    await SaveAs(page, "Orders", "How an order is processed");
    Assert.Equal("Saved “Orders”.", await page.InnerTextAsync("#views-status"));
    Assert.Equal(["Orders"], await ViewNames(page));
    Assert.Equal("How an order is processed", await page.InnerTextAsync(ViewRow("Orders") + " .views-description"));
    Assert.Equal("true", await page.GetAttributeAsync(ViewRow("Orders") + " .views-open", "aria-current"));
    await page.Keyboard.PressAsync("Escape");
    Assert.Equal("Views · Orders", await page.TextContentAsync("#views > summary"));

    // Arrange the page differently, and select something.
    await page.ClickAsync("#explorer-filter-bar [data-filter-clear]");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await page.ClickAsync("#view-graph .view-show-note [data-show-reset]");
    await page.ClickAsync("[data-level='modules']");
    await page.ClickAsync("#tab-classes");
    await opened.SelectAsync("Utils");

    await OpenView(page, "Orders");
    await WaitUntilStill(page);

    Assert.Equal("true", await page.GetAttributeAsync("#tab-graph", "aria-selected"));
    Assert.Equal("members", await page.EvaluateAsync<string>("() => window.vbadocGraph.level()"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal("Logger|Main|Order|OrderService", await page.EvaluateAsync<string>("() => window.vbadoc.filterKey()"));
    Assert.Equal(["Logger"], await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()"));
    Assert.False(await page.EvaluateAsync<bool>("() => window.vbadoc.choice('private')"));
    Assert.True(await page.IsVisibleAsync("#explorer-filter-bar"));
    Assert.True(await page.IsVisibleAsync("#view-graph .view-show-note"));
    Assert.Equal(nodes, await GraphNodes(page));
    Assert.Equal(moved, await Position(page, "Main.ProcessOrders"));
    Assert.Equal(other, await Position(page, nodes.First(id => id != "Main.ProcessOrders")));
    Assert.True(await page.IsHiddenAsync("#page-notice"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_changed_since_it_was_opened_says_so_and_Save_changes_keeps_the_new_arrangement()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await SaveAs(page, "Everything");
    Assert.True(await page.IsHiddenAsync("#views-update"));
    await page.Keyboard.PressAsync("Escape");

    await HideFromRow(page, "Logger");
    await OpenMenu(page);
    Assert.True(await page.IsVisibleAsync(ViewRow("Everything") + " .views-changed"));
    Assert.Equal("Save changes to “Everything”", await page.InnerTextAsync("#views-update"));
    await page.ClickAsync("#views-update");
    Assert.Equal("Saved the changes to “Everything”. Undo", await page.InnerTextAsync("#views-status"));
    Assert.True(await page.IsHiddenAsync(ViewRow("Everything") + " .views-changed"));
    await page.Keyboard.PressAsync("Escape");

    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    Assert.Contains("Logger", await GraphNodes(page));
    await OpenView(page, "Everything");
    Assert.DoesNotContain("Logger", await GraphNodes(page));
    Assert.Empty(opened.Errors);
  }

  // After G1's review: saving over another view used to mean opening it first, which lost what had been arranged.
  [Fact]
  public async Task The_page_can_be_saved_over_another_view_without_opening_it_with_Undo()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await HideFromRow(page, "Logger");
    await SaveAs(page, "Quiet", "Logger hidden");
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await SaveAs(page, "Everything");
    await page.Keyboard.PressAsync("Escape");
    await HideFromRow(page, "Utils");

    await OpenMenu(page);
    Assert.Equal(0, await page.Locator(ViewRow("Everything") + " [data-view-save]").CountAsync()); // it has Save changes
    await page.ClickAsync(ViewRow("Quiet") + " [data-view-save]");
    Assert.Equal("Saved over “Quiet”. Undo", await page.InnerTextAsync("#views-status"));
    Assert.Equal("Views · Quiet", await page.TextContentAsync("#views > summary"));
    Assert.Equal("Logger hidden", await page.InnerTextAsync(ViewRow("Quiet") + " .views-description"));
    Assert.True(await page.IsHiddenAsync(ViewRow("Quiet") + " .views-changed"));
    await page.ClickAsync("#views-undo");
    Assert.Equal("Put “Quiet” back as it was.", await page.InnerTextAsync("#views-status"));
    Assert.Equal("Views · Everything", await page.TextContentAsync("#views > summary"));
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await OpenView(page, "Quiet");
    Assert.Equal(["Logger"], await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()"));

    // From the form: a name that's taken offers to save over that view, with the description typed.
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await HideFromRow(page, "Utils");
    await OpenMenu(page);
    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", "everything");
    await page.FillAsync("#views-description", "Without Utils");
    await page.ClickAsync("#views-form-save");
    await page.ClickAsync("#views-save-over");
    Assert.Equal("Saved over “Everything”. Undo", await page.InnerTextAsync("#views-status"));
    Assert.Equal(["Quiet", "Everything"], await ViewNames(page));
    Assert.Equal("Without Utils", await page.InnerTextAsync(ViewRow("Everything") + " .views-description"));
    Assert.True(await page.IsHiddenAsync("#views-form"));
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
    await OpenView(page, "Everything");
    Assert.Equal(["Utils"], await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Importing_another_workbooks_views_asks_first_and_can_be_cancelled_or_undone()
  {
    var file = WriteFile("views.json", """
      {
        "formatVersion": 1,
        "tool": "vbadoc",
        "workbook": "Budget.xlsm",
        "views": [
          { "name": "Orders", "filter": ["Order"] },
          { "name": "Budget lines", "filter": ["Order"] }
        ]
      }
      """);
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await SaveAs(page, "Orders", "Mine");

    async Task Import()
    {
      await page.SetInputFilesAsync("#views-import", file);
      await page.WaitForSelectorAsync("#views-confirm:not([hidden])");
    }

    await Import();
    Assert.Equal("views.json holds views of Budget.xlsm, not SampleVba.xlsm. Importing replaces the view “Orders” here.",
        await page.InnerTextAsync("#views-confirm-text"));
    Assert.Equal("Import anyway", await page.InnerTextAsync("#views-confirm-import"));
    await page.ClickAsync("#views-confirm-cancel");
    Assert.Equal("Nothing was imported from views.json.", await page.InnerTextAsync("#views-status"));
    Assert.Equal(["Orders"], await ViewNames(page));
    Assert.Equal("Mine", await page.InnerTextAsync(ViewRow("Orders") + " .views-description"));
    Assert.True(await page.IsVisibleAsync("#views-actions"));

    await Import();
    await page.ClickAsync("#views-confirm-import");
    Assert.Equal("Imported 2 views from views.json, replacing 1 of the same name. Undo", await page.InnerTextAsync("#views-status"));
    Assert.Equal(["Orders", "Budget lines"], await ViewNames(page));
    await page.ClickAsync("#views-undo");
    Assert.Equal("Put the views back as they were before importing views.json.", await page.InnerTextAsync("#views-status"));
    Assert.Equal(["Orders"], await ViewNames(page));
    Assert.Equal("Mine", await page.InnerTextAsync(ViewRow("Orders") + " .views-description"));

    // Another file chosen while one waits takes its place.
    var matching = WriteFile("matching.json", """{ "formatVersion": 1, "tool": "vbadoc", "views": [{ "name": "Mine too" }] }""");
    await Import();
    await page.SetInputFilesAsync("#views-import", matching);
    await page.WaitForFunctionAsync("() => document.getElementById('views-status').textContent.includes('matching.json')");
    Assert.True(await page.IsHiddenAsync("#views-confirm"));
    Assert.Equal(["Orders", "Mine too"], await ViewNames(page));
    await page.ClickAsync("#views-undo");

    // Closing the menu drops an import waiting to be confirmed.
    await Import();
    await page.Keyboard.PressAsync("Escape");
    await OpenMenu(page);
    Assert.True(await page.IsHiddenAsync("#views-confirm"));
    Assert.Equal(["Orders"], await ViewNames(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_can_be_renamed_described_and_deleted_with_Undo_names_are_unique_and_views_are_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await SaveAs(page, "One");
    await SaveAs(page, "Two");

    await page.ClickAsync("#views-new");
    await page.FillAsync("#views-name", "one");
    await page.ClickAsync("#views-form-save");
    Assert.Equal("There's already a view called “one”. Save over it", await page.InnerTextAsync("#views-error"));
    await page.FillAsync("#views-name", " ");
    await page.ClickAsync("#views-form-save");
    Assert.Equal("Give the view a name.", await page.InnerTextAsync("#views-error"));
    await page.ClickAsync("#views-form-cancel");
    Assert.True(await page.IsHiddenAsync("#views-form"));

    await page.ClickAsync(ViewRow("Two") + " [data-view-edit]");
    Assert.Equal("Two", await page.InputValueAsync("#views-name"));
    await page.FillAsync("#views-name", "one");
    await page.ClickAsync("#views-form-save");
    Assert.Equal("There's already a view called “one”.", await page.InnerTextAsync("#views-error")); // a rename can't save over
    await page.FillAsync("#views-name", "Three");
    await page.FillAsync("#views-description", "The third");
    await page.ClickAsync("#views-form-save");
    Assert.Equal(["One", "Three"], await ViewNames(page));
    Assert.Equal("The third", await page.InnerTextAsync(ViewRow("Three") + " .views-description"));
    Assert.Equal("Views · Three", await page.TextContentAsync("#views > summary")); // it was the current view

    await page.ClickAsync(ViewRow("One") + " [data-view-delete]");
    Assert.Equal(["Three"], await ViewNames(page));
    Assert.StartsWith("Deleted “One”.", await page.InnerTextAsync("#views-status"));
    await page.ClickAsync("#views-undo");
    Assert.Equal(["One", "Three"], await ViewNames(page));
    Assert.Equal("Brought back “One”.", await page.InnerTextAsync("#views-status"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    await OpenMenu(page);
    Assert.Equal(["One", "Three"], await ViewNames(page));
    Assert.Equal("Views", await page.TextContentAsync("#views > summary")); // which one was open isn't remembered
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Views_export_to_a_file_with_sorted_keys_and_import_into_another_browser()
  {
    string file;
    double[] moved;
    await using (var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick))
    {
      var page = opened.Page;
      await HideFromRow(page, "Logger"); // first, as hiding lays the graph out afresh
      await WaitUntilStill(page);
      await Drag(page, "#graph-svg g.node[data-id='Utils'] path.shape", 90, 50);
      await WaitUntilStill(page);
      moved = await Position(page, "Utils");
      await SaveAs(page, "Without logging", "Logger hidden");
      await page.ClickAsync("#explorer-tree > li.hidden-items [data-unhide-all]");
      await SaveAs(page, "All");

      var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#views-export"));
      Assert.Equal("vbadoc-views.json", download.SuggestedFilename);
      file = Path.Combine(_folder, "vbadoc-views.json");
      await download.SaveAsAsync(file);
      Assert.Equal("Exported 2 views as vbadoc-views.json.", await page.InnerTextAsync("#views-status"));
      Assert.Empty(opened.Errors);
    }

    var text = File.ReadAllText(file);
    var json = JsonNode.Parse(text)!.AsObject();
    Assert.Equal(["formatVersion", "tool", "views", "workbook"], json.Select(p => p.Key));
    Assert.Equal(1, (int)json["formatVersion"]!);
    Assert.Equal("vbadoc", (string)json["tool"]!);
    Assert.Equal("SampleVba.xlsm", (string)json["workbook"]!);
    var first = json["views"]![0]!.AsObject();
    Assert.Equal(first.Select(p => p.Key).Order(StringComparer.Ordinal), first.Select(p => p.Key));
    Assert.Equal("Without logging", (string)first["name"]!);
    Assert.Equal(["Logger"], first["hidden"]!.AsArray().Select(n => (string)n!));
    Assert.StartsWith("{\n  \"formatVersion\": 1,\n  \"tool\": \"vbadoc\",\n  \"views\": [\n    {\n", text);
    Assert.EndsWith("}\n", text);

    // A new browser has none of this one's storage.
    await using var other = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var fresh = other.Page;
    await OpenMenu(fresh);
    await fresh.SetInputFilesAsync("#views-import", file);
    await fresh.WaitForFunctionAsync("() => document.getElementById('views-status').textContent !== ''");
    Assert.Equal("Imported 2 views from vbadoc-views.json. Undo", await fresh.InnerTextAsync("#views-status"));
    Assert.Equal(["Without logging", "All"], await ViewNames(fresh));
    await OpenView(fresh, "Without logging");
    await WaitUntilStill(fresh);
    Assert.DoesNotContain("Logger", await GraphNodes(fresh));
    Assert.Equal(moved, await Position(fresh, "Utils"));

    // Importing again replaces the views of the same name rather than adding them twice.
    await OpenMenu(fresh);
    await fresh.SetInputFilesAsync("#views-import", file);
    await fresh.WaitForFunctionAsync("() => document.getElementById('views-status').textContent.includes('replacing')");
    Assert.Equal("Imported 2 views from vbadoc-views.json, replacing 2 of the same name. Undo", await fresh.InnerTextAsync("#views-status"));
    Assert.Equal(["Without logging", "All"], await ViewNames(fresh));
    Assert.Empty(other.Errors);
  }

  [Fact]
  public async Task A_file_mentioning_items_the_workbook_doesnt_have_still_loads_and_lists_them()
  {
    var file = WriteFile("old-views.json", """
      {
        "formatVersion": 1,
        "tool": "vbadoc",
        "views": [
          {
            "name": "Before the rename",
            "description": "Made when there was a Billing module",
            "tab": "graph",
            "filter": ["Order", "Billing"],
            "hidden": ["Billing.Charge"],
            "graph": { "level": "modules", "layouts": { "modules": { "positions": { "Order": [10, 20], "Billing": [5, 5] }, "pinned": ["Order"] } } },
            "calls": { "root": "Billing.Run", "open": ["Billing.Run", "Billing.Run/0:Billing.Charge"] }
          },
          {
            "name": "Everything, moved",
            "graph": { "level": "modules", "layouts": { "modules": { "positions": { "Logger": [400, -300], "Utils": [-400, 300] } } } }
          },
          { "description": "No name, so left out" }
        ]
      }
      """);
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded, initScript: OpenedPage.WithoutFocusOnClick);
    var page = opened.Page;
    var warnings = new List<string>();
    page.Console += (_, message) => { if (message.Type == "warning") warnings.Add(message.Text); };

    // It says so before anything is imported.
    await OpenMenu(page);
    await page.SetInputFilesAsync("#views-import", file);
    await page.WaitForSelectorAsync("#views-confirm:not([hidden])");
    Assert.Equal("Its views mention 3 items that this workbook doesn't have, which would be left out: Billing, Billing.Charge, " +
        "Billing.Run.", await page.InnerTextAsync("#views-confirm-text"));
    Assert.True(await page.IsVisibleAsync("#views-none"));
    Assert.True(await page.IsHiddenAsync("#views-actions"));
    await page.ClickAsync("#views-confirm-import");
    Assert.Equal("Imported 2 views from old-views.json. 1 without a name was left out. Undo", await page.InnerTextAsync("#views-status"));
    Assert.True(await page.IsHiddenAsync("#views-confirm"));
    Assert.Contains(warnings, w => w.Contains("\"Before the rename\"") && w.Contains("Billing.Run"));

    await OpenView(page, "Before the rename");
    await WaitUntilStill(page);
    Assert.Equal(["Order"], await GraphNodes(page));
    Assert.Equal(new double[] { 10, 20 }, await Position(page, "Order"));
    Assert.Equal(1, await page.Locator("#graph-svg g.node.pinned").CountAsync());
    Assert.Equal("Filtered: 1 of " + await page.EvaluateAsync<int>("() => window.vbadoc.data.modules.length") + " modules",
        await page.InnerTextAsync("#explorer-filter-text"));
    Assert.Equal("The view “Before the rename” mentions 3 items that this workbook doesn't have, so they're left out: Billing, " +
        "Billing.Charge, Billing.Run.",
        await page.InnerTextAsync("#page-notice-text"));
    await page.ClickAsync("#page-notice-close");
    Assert.True(await page.IsHiddenAsync("#page-notice"));

    // Nodes the view doesn't place are laid out around those it does, which stay put.
    await OpenView(page, "Everything, moved");
    await WaitUntilStill(page);
    Assert.Equal(new double[] { 400, -300 }, await Position(page, "Logger"));
    Assert.Equal(new double[] { -400, 300 }, await Position(page, "Utils"));
    Assert.True(await page.EvaluateAsync<bool>(@"() => [...document.querySelectorAll('#graph-svg g.node')]
        .every(n => { const p = window.vbadocGraph.position(n.dataset.id); return isFinite(p.x) && isFinite(p.y); })"));
    Assert.True(await page.IsHiddenAsync("#page-notice"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_file_that_isnt_vbadocs_views_is_refused_with_the_reason()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await SaveAs(page, "Kept");

    async Task<string> Import(string name, string text)
    {
      await page.EvaluateAsync("() => document.getElementById('views-status').textContent = ''");
      await page.SetInputFilesAsync("#views-import", WriteFile(name, text));
      await page.WaitForFunctionAsync("() => document.getElementById('views-status').textContent !== ''");
      return await page.InnerTextAsync("#views-status");
    }

    Assert.Equal("notes.json isn't a views file.", await Import("notes.json", "not JSON"));
    Assert.Equal("list.json isn't a views file.", await Import("list.json", "[1, 2]"));
    Assert.Equal("pqdoc-views.json holds pqdoc's views, not vbadoc's.",
        await Import("pqdoc-views.json", """{ "formatVersion": 1, "tool": "pqdoc", "views": [] }"""));
    Assert.Equal("new.json was written by a newer vbadoc than the one that made this page. Run vbadoc again to read it.",
        await Import("new.json", """{ "formatVersion": 2, "tool": "vbadoc", "views": [{ "name": "Later" }] }"""));
    Assert.Contains("warning", await page.GetAttributeAsync("#views-status", "class"));
    Assert.Equal(["Kept"], await ViewNames(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_keeps_the_Calls_tree_and_where_class_diagram_boxes_were_moved()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    const string processOrders = "#calls-tree li.group > ul > li[data-id='Main.ProcessOrders']";

    await page.ClickAsync("#tab-calls");
    await page.ClickAsync(processOrders + " > .tree-row .tree-twisty");
    await page.ClickAsync("#view-calls [data-calls-show='sequence']");
    await SaveAs(page, "Processing orders");
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#view-calls [data-calls-show='tree']");
    await page.ClickAsync("#calls-collapse");

    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    await page.ClickAsync("#classes-members"); // first, as moves are kept for each setting of the switch
    Assert.Equal("false", await page.GetAttributeAsync("#classes-members", "aria-pressed"));
    await Drag(page, "#classes-svg g.class-box[data-module='Order'] .box-outline", 80, 40);
    var box = await page.GetAttributeAsync("#classes-svg g.class-box[data-module='Order']", "transform");
    await SaveAs(page, "Boxes");
    await page.Keyboard.PressAsync("Escape");
    await page.ClickAsync("#classes-relayout");
    await page.ClickAsync("#classes-members");
    Assert.NotEqual(box, await page.GetAttributeAsync("#classes-svg g.class-box[data-module='Order']", "transform"));

    await OpenView(page, "Processing orders");
    Assert.Equal("true", await page.GetAttributeAsync("#tab-calls", "aria-selected"));
    Assert.Equal("sequence", await page.EvaluateAsync<string>("() => window.vbadocCalls.showing()"));
    Assert.True(await page.IsVisibleAsync("#calls-sequence svg"));
    Assert.Equal("true", await page.GetAttributeAsync(processOrders, "aria-expanded"));

    await OpenView(page, "Boxes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal("true", await page.GetAttributeAsync("#tab-classes", "aria-selected"));
    Assert.Equal("false", await page.GetAttributeAsync("#classes-members", "aria-pressed"));
    Assert.Equal(box, await page.GetAttributeAsync("#classes-svg g.class-box[data-module='Order']", "transform"));
    Assert.Empty(opened.Errors);
  }

  static Task<string> PanelSize(IPage page, string variable) =>
      page.EvaluateAsync<string>("v => document.getElementById('layout').style.getPropertyValue(v)", variable);

  [Fact]
  public async Task A_view_keeps_the_selection_its_focus_Focus_on_click_the_panels_and_what_the_source_panel_shows()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await WaitUntilStill(page);
    await page.ClickAsync("#graph-svg g.node[data-id='OrderService'] path.shape");
    await WaitUntilStill(page);
    var focusedNodes = await GraphNodes(page);
    await page.ClickAsync("#explorer-toggle");
    await page.FocusAsync("#side-panel-splitter");
    await page.Keyboard.PressAsync("Home");
    Assert.Equal("20.0%", await PanelSize(page, "--side-panel-size"));
    await SaveAs(page, "Order service");
    await page.Keyboard.PressAsync("Escape");

    // Focus on click off, a member shown in its whole module, unfocused, with the explorer back.
    await page.ClickAsync("#graph-focus");
    await opened.SelectAsync("Main.ProcessOrders");
    await page.ClickAsync("#toggle-context");
    await page.ClickAsync("#explorer-toggle");
    await WaitUntilStill(page);
    var allNodes = await GraphNodes(page);
    await SaveAs(page, "Processing orders");
    await page.Keyboard.PressAsync("Escape");
    await page.Keyboard.PressAsync("Escape"); // clears the selection, and the side panel closes with it
    Assert.True(await page.IsHiddenAsync("#side-panel"));

    await OpenView(page, "Order service");
    await WaitUntilStill(page);
    Assert.Equal("OrderService", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.True(await page.EvaluateAsync<bool>("() => window.vbadoc.focused()"));
    Assert.Equal("true", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));
    Assert.Equal(focusedNodes, await GraphNodes(page));
    Assert.True(await page.IsHiddenAsync("#explorer"));
    Assert.True(await page.IsVisibleAsync("#side-panel"));
    Assert.Equal("20.0%", await PanelSize(page, "--side-panel-size"));
    Assert.Contains("OrderService", await page.InnerTextAsync("#side-panel-header h2"));

    await OpenView(page, "Processing orders");
    await WaitUntilStill(page);
    Assert.Equal("Main.ProcessOrders", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.False(await page.EvaluateAsync<bool>("() => window.vbadoc.focused()"));
    Assert.Equal("false", await page.GetAttributeAsync("#graph-focus", "aria-pressed"));
    Assert.Equal(allNodes, await GraphNodes(page));
    Assert.True(await page.IsVisibleAsync("#explorer"));
    Assert.Equal("true", await page.GetAttributeAsync(Row("Main.ProcessOrders").Replace(" > .tree-row", ""), "aria-selected"));
    Assert.Equal("Show this member only", await page.InnerTextAsync("#toggle-context"));
    Assert.Equal("#Main.ProcessOrders", await page.EvaluateAsync<string>("() => location.hash"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_keeps_the_graphs_direction_settings_legend_and_nodes_moved_while_focused()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await WaitUntilStill(page);
    await page.ClickAsync("#graph-svg g.node[data-id='OrderService'] path.shape");
    await page.ClickAsync("[data-direction='dependencies']");
    await WaitUntilStill(page);
    var other = (await GraphNodes(page)).First(id => id != "OrderService");
    await Drag(page, $"#graph-svg g.node[data-id='{other}'] path.shape", 70, 40);
    await WaitUntilStill(page);
    var moved = await Position(page, other);
    await page.ClickAsync("#graph-settings > summary");
    await page.UncheckAsync("#graph-settings-menu [data-setting='curvedEdges']");
    await page.Keyboard.PressAsync("Escape");
    var legendKey = await page.GetAttributeAsync("#graph-legend [data-legend]", "data-legend");
    await page.ClickAsync($"#graph-legend [data-legend='{legendKey}']");
    await SaveAs(page, "Focused");
    await page.Keyboard.PressAsync("Escape");

    // Another focus, then back to this one: the moved node is where it was left.
    await page.ClickAsync("#graph-svg g.node[data-id='" + other + "'] path.shape");
    await WaitUntilStill(page);
    await opened.SelectAsync("OrderService");
    await WaitUntilStill(page);
    Assert.Equal(moved, await Position(page, other));

    await page.ClickAsync("[data-direction='all']");
    await page.ClickAsync("#graph-settings > summary");
    await page.CheckAsync("#graph-settings-menu [data-setting='curvedEdges']");
    await page.Keyboard.PressAsync("Escape");
    await page.Keyboard.PressAsync("Escape"); // clears the selection
    await page.EvaluateAsync("() => document.getElementById('graph-relayout').click()");
    await WaitUntilStill(page);

    await OpenView(page, "Focused");
    await WaitUntilStill(page);
    Assert.Equal("OrderService", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal("true", await page.GetAttributeAsync("[data-direction='dependencies']", "aria-checked"));
    Assert.False(await page.IsCheckedAsync("#graph-settings-menu [data-setting='curvedEdges']"));
    Assert.Equal("true", await page.GetAttributeAsync($"#graph-legend [data-legend='{legendKey}']", "aria-pressed"));
    Assert.Equal(moved, await Position(page, other));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_view_keeps_boxes_moved_in_a_focused_class_diagram()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    await opened.SelectAsync("OrderService"); // focused, as focus on click is on
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn() && !window.vbadocClasses.moving()");
    var boxes = await page.Locator("#classes-svg g.class-box").CountAsync();
    Assert.True(boxes < await page.EvaluateAsync<int>("() => window.vbadoc.data.modules.length"));
    await Drag(page, "#classes-svg g.class-box[data-module='OrderService'] .box-outline", 80, 40);
    var box = await page.GetAttributeAsync("#classes-svg g.class-box[data-module='OrderService']", "transform");
    await SaveAs(page, "Order service classes");
    await page.Keyboard.PressAsync("Escape");

    await page.Keyboard.PressAsync("Escape"); // clears the selection, and the focus
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.True(await page.Locator("#classes-svg g.class-box").CountAsync() > boxes);
    await OpenView(page, "Order service classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal(boxes, await page.Locator("#classes-svg g.class-box").CountAsync());
    Assert.Equal(box, await page.GetAttributeAsync("#classes-svg g.class-box[data-module='OrderService']", "transform"));
    Assert.Empty(opened.Errors);
  }

  // The user's report after G1: moved nodes weren't put back by a view once the graph had been laid out again, or
  // focused and cleared, with the same filter and choices. The graph kept the layout on screen and put that back.
  [Fact]
  public async Task Opening_a_view_puts_moved_nodes_back_after_Re_run_layout_or_a_focus()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await WaitUntilStill(page);
    await Drag(page, "#graph-svg g.node[data-id='Utils'] path.shape", 90, 50);
    await WaitUntilStill(page);
    var moved = await Position(page, "Utils");
    await SaveAs(page, "Moved");
    await page.Keyboard.PressAsync("Escape");

    await page.EvaluateAsync("() => document.getElementById('graph-relayout').click()");
    await WaitUntilStill(page);
    Assert.NotEqual(moved, await Position(page, "Utils"));
    await OpenView(page, "Moved");
    await WaitUntilStill(page);
    Assert.Equal(moved, await Position(page, "Utils"));

    // Focused on a selection, then the view, which has none.
    await page.ClickAsync("#graph-svg g.node[data-id='Order'] path.shape");
    await WaitUntilStill(page);
    Assert.True(await page.EvaluateAsync<bool>("() => window.vbadoc.focused()"));
    await OpenView(page, "Moved");
    await WaitUntilStill(page);
    Assert.Equal(moved, await Position(page, "Utils"));
    Assert.Empty(opened.Errors);
  }
}
