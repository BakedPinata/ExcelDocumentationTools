using System.Text.Json.Nodes;
using ExcelDocumenter.Shared.Importing;
using Microsoft.Playwright;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The Export tab (step H2 of the feedback plan): choosing what to take out of the workbook, seeing what that comes to,
/// and the manifest the import command reads.
/// </summary>
[Collection("Browser")]
public sealed class ExportTests(PageFixture fixture) : IDisposable
{
  readonly string _folder = Directory.CreateTempSubdirectory("vbadoc-export-").FullName;

  public void Dispose() => Directory.Delete(_folder, recursive: true);

  static string Box(string id) => $"#export-tree input[data-export-item='{id}']";

  static string Row(string id) => $"#explorer-tree li.item:not(.favourites li):not(.hidden-items li)[data-id='{id}'] > .tree-row";

  static Task OpenTab(IPage page) => page.ClickAsync("#tab-export");

  // The names under a heading of the plan.
  static Task<string[]> Listed(IPage page, string heading) => page.EvaluateAsync<string[]>(@"heading => {
    const title = [...document.querySelectorAll('#export-plan h2')].find(h => h.textContent.startsWith(heading));
    return title ? [...title.nextElementSibling.querySelectorAll('li')].map(li => li.querySelector('strong, .link-button').textContent) : [];
  }", heading);

  async Task<string> Download(IPage page)
  {
    var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#export-download"));
    Assert.Equal("vbadoc-export.json", download.SuggestedFilename);
    var path = Path.Combine(_folder, "vbadoc-export.json");
    await download.SaveAsAsync(path);
    return path;
  }

  string WriteFile(string name, string text)
  {
    var path = Path.Combine(_folder, name);
    File.WriteAllText(path, text);
    return path;
  }

  // Two implementations of one idea: this keeps them saying the same thing.
  [Fact]
  public async Task For_every_module_the_page_shows_the_plan_the_command_works_out()
  {
    var import = new ModuleImport(DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules())))));
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);

    var ids = await page.EvaluateAsync<string[]>("() => window.vbadoc.data.modules.map(m => m.id)");
    var compared = 0;
    foreach (var id in ids.Where(id => import.WhyNot(id) is null))
    {
      await page.CheckAsync(Box(id));
      var plan = import.Plan([id]);
      Assert.Equal([id], await Listed(page, "Chosen"));
      Assert.Equal(plan.Brought.Select(b => b.Id), await Listed(page, "Comes along"));
      Assert.Equal(plan.Needs.Select(n => n.Need.What), await Listed(page, "To check in the target"));
      Assert.Equal(plan.Offers.Select(o => o.Id), await Listed(page, "You may want these too"));
      await page.UncheckAsync(Box(id));
      compared++;
    }

    Assert.True(compared > 5);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_module_that_comes_along_is_marked_and_a_document_module_cant_be_ticked_and_says_why()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    Assert.Contains("Tick the modules and classes", await page.InnerTextAsync("#export-plan"));
    Assert.True(await page.IsDisabledAsync("#export-download"));

    await page.CheckAsync(Box("OrderService"));

    var brought = await Listed(page, "Comes along");
    Assert.NotEmpty(brought);
    Assert.True(await page.IsVisibleAsync($"#export-tree li[data-export-id='{brought[0]}'] .export-tag"));
    Assert.True(await page.IsHiddenAsync("#export-tree li[data-export-id='OrderService'] .export-tag"));
    Assert.False(await page.IsCheckedAsync(Box(brought[0])));

    Assert.True(await page.IsDisabledAsync(Box("ThisWorkbook")));
    Assert.Equal("A document module belongs to its workbook", await page.GetAttributeAsync("#export-tree li[data-export-id='ThisWorkbook'] label", "title"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Ticking_a_folder_picks_all_of_it_and_it_shows_when_only_some_is_picked()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.FolderedEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    const string Folder = "#export-tree li.export-group:has(> label .export-name:text-is('Startup')) > label input";

    await page.CheckAsync(Folder);
    Assert.Equal(["AppInit", "Main", "Settings"], await Listed(page, "Chosen"));

    await page.UncheckAsync(Box("Main"));
    Assert.True(await page.Locator(Folder).EvaluateAsync<bool>("box => box.indeterminate"));
    Assert.False(await page.IsCheckedAsync(Folder));

    await page.UncheckAsync(Box("AppInit"));
    await page.UncheckAsync(Box("Settings"));
    Assert.False(await page.Locator(Folder).EvaluateAsync<bool>("box => box.indeterminate"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Whats_ticked_in_the_explorer_can_be_added_and_a_member_stands_for_its_module()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    Assert.True(await page.IsDisabledAsync("#export-from-filter"));

    await page.ClickAsync(Row("Logger") + " > .tree-check");
    await page.ClickAsync(Row("Sheet1") + " > .tree-check"); // can't be taken, so it's left out
    await page.ClickAsync(Row("Main") + " > .tree-twisty");
    await page.ClickAsync(Row("Main.ProcessOrders") + " > .tree-check");
    await page.ClickAsync("#export-from-filter");

    Assert.Equal(["Logger", "Main"], await Listed(page, "Chosen"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task An_interfaces_implementations_are_offered_and_can_be_added()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    await page.CheckAsync(Box("IOrderRepository"));

    Assert.DoesNotContain("CsvOrderRepository", await Listed(page, "Comes along")); // nothing uses it
    Assert.Equal(["CsvOrderRepository", "SheetOrderRepository"], await Listed(page, "You may want these too"));

    await page.ClickAsync("[data-export-add='CsvOrderRepository']");

    Assert.Contains("CsvOrderRepository", await Listed(page, "Chosen"));
    Assert.Equal(["SheetOrderRepository"], await Listed(page, "You may want these too"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task It_lists_everything_whatever_the_views_are_filtered_to()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.HoverAsync(Row("Logger"));
    await page.ClickAsync(Row("Logger") + " .hide-toggle");
    await page.ClickAsync(Row("Utils") + " > .tree-check");
    await OpenTab(page);

    Assert.True(await page.IsVisibleAsync(Box("Logger")));
    Assert.True(await page.IsVisibleAsync(Box("Order")));
    await page.CheckAsync(Box("Logger"));
    Assert.Equal(["Logger"], await Listed(page, "Chosen"));

    // Ticking here isn't the filter.
    Assert.Equal("Utils", await page.EvaluateAsync<string>("() => window.vbadoc.filterKey()"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_list_can_be_filtered_and_a_name_in_the_plan_selects_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);

    await page.FillAsync("#export-filter", "repository");
    Assert.True(await page.IsVisibleAsync(Box("IOrderRepository")));
    Assert.True(await page.IsHiddenAsync(Box("Logger")));
    await page.FillAsync("#export-filter", "");
    Assert.True(await page.IsVisibleAsync(Box("Logger")));

    await page.CheckAsync(Box("Logger"));
    await page.ClickAsync("#export-plan [data-export-select='Logger']");
    Assert.Equal("Logger", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.True(await page.IsVisibleAsync("#side-panel"));
    Assert.Equal("true", await page.GetAttributeAsync("#tab-export", "aria-selected"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_choice_is_remembered_and_Clear_has_Undo()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    await page.CheckAsync(Box("Logger"));
    await page.CheckAsync(Box("Utils"));

    await page.ClickAsync("#export-clear");
    Assert.Empty(await Listed(page, "Chosen"));
    Assert.Equal("Cleared the choice. Undo", await page.InnerTextAsync("#export-status"));
    await page.ClickAsync("#export-undo");
    Assert.Equal(["Logger", "Utils"], await Listed(page, "Chosen"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    await OpenTab(page);
    Assert.Equal(["Logger", "Utils"], await Listed(page, "Chosen"));
    Assert.True(await page.IsCheckedAsync(Box("Logger")));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_manifest_holds_the_picks_not_what_they_bring_and_loads_into_another_browser()
  {
    string path;
    await using (var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded))
    {
      var page = opened.Page;
      await OpenTab(page);
      await page.CheckAsync(Box("OrderService"));
      await page.CheckAsync(Box("Logger"));
      Assert.NotEmpty(await Listed(page, "Comes along"));
      path = await Download(page);
      Assert.Equal("Downloaded vbadoc-export.json.", await page.InnerTextAsync("#export-status"));
      Assert.Equal("vbadoc import \"SampleVba.xlsm\" --manifest vbadoc-export.json --new Shared.xlsm", await page.InnerTextAsync("#export-command"));
      Assert.Empty(opened.Errors);
    }

    var text = File.ReadAllText(path);
    Assert.Equal(["formatVersion", "items", "leaveOutReferences", "tool", "workbook"], JsonNode.Parse(text)!.AsObject().Select(p => p.Key));
    Assert.EndsWith("}\n", text);

    // The file the page writes is the file the command reads.
    var manifest = ExportManifest.Parse(text, "vbadoc-export.json", "vbadoc");
    Assert.Equal(["Logger", "OrderService"], manifest.Items);
    Assert.Equal("SampleVba.xlsm", manifest.Workbook);

    await using var other = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var fresh = other.Page;
    await OpenTab(fresh);
    await fresh.SetInputFilesAsync("#export-file", path);
    await fresh.WaitForFunctionAsync("() => document.getElementById('export-status').textContent !== ''");
    Assert.Equal("Loaded 2 items from vbadoc-export.json. Undo", await fresh.InnerTextAsync("#export-status"));
    Assert.Equal(["Logger", "OrderService"], await Listed(fresh, "Chosen"));
    await fresh.ClickAsync("#export-undo");
    Assert.Empty(await Listed(fresh, "Chosen"));
    Assert.Empty(other.Errors);
  }

  [Fact]
  public async Task A_manifest_from_elsewhere_says_so_and_what_this_workbook_cant_give_is_left_out_and_listed()
  {
    var path = WriteFile("old.json", """{ "formatVersion": 1, "tool": "vbadoc", "workbook": "Other.xlsm", "items": ["Logger", "Billing", "Sheet1"] }""");
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);

    await page.SetInputFilesAsync("#export-file", path);
    await page.WaitForFunctionAsync("() => document.getElementById('export-status').textContent !== ''");

    Assert.Equal(["Logger"], await Listed(page, "Chosen"));
    var status = await page.InnerTextAsync("#export-status");
    Assert.Contains("It was made from Other.xlsm.", status);
    Assert.Contains("2 items this workbook can't give were left out: Billing, Sheet1.", status);
    Assert.Contains("warning", await page.GetAttributeAsync("#export-status", "class"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_file_that_isnt_vbadocs_manifest_is_refused_with_the_reason()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    await page.CheckAsync(Box("Logger"));

    async Task<string> Load(string name, string text)
    {
      await page.EvaluateAsync("() => document.getElementById('export-status').textContent = ''");
      await page.SetInputFilesAsync("#export-file", WriteFile(name, text));
      await page.WaitForFunctionAsync("() => document.getElementById('export-status').textContent !== ''");
      return await page.InnerTextAsync("#export-status");
    }

    Assert.Equal("notes.json isn't an export manifest.", await Load("notes.json", "not JSON"));
    Assert.Equal("pq.json holds pqdoc's choices, not vbadoc's.", await Load("pq.json", """{ "formatVersion": 1, "tool": "pqdoc", "items": [] }"""));
    Assert.Equal("new.json was written by a newer vbadoc than the one that made this page. Run vbadoc again to read it.",
        await Load("new.json", """{ "formatVersion": 2, "tool": "vbadoc", "items": [] }"""));
    Assert.Equal(["Logger"], await Listed(page, "Chosen"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Library_references_are_offered_apart_from_those_every_project_has_and_can_be_left_out()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ReferencesEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    await page.CheckAsync(Box("Logger"));

    var names = await page.Locator("#export-plan [data-export-reference]").EvaluateAllAsync<string[]>("boxes => boxes.map(b => b.dataset.exportReference)");
    Assert.Equal(["Scripting", "ADODB"], names);
    Assert.Contains("Microsoft Scripting Runtime", await page.InnerTextAsync("#export-plan"));
    Assert.True(await page.IsCheckedAsync("[data-export-reference='ADODB']"));

    await page.UncheckAsync("[data-export-reference='ADODB']");
    var manifest = ExportManifest.Parse(File.ReadAllText(await Download(page)), "m.json", "vbadoc");

    Assert.Equal(["ADODB"], manifest.LeaveOutReferences);
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_other_tabs_carry_on_as_they_were()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await OpenTab(page);
    Assert.True(await page.IsHiddenAsync("#view-graph"));

    await page.ClickAsync("#tab-graph");
    await page.WaitForFunctionAsync("() => window.vbadocGraph.nodeCount() > 0");
    Assert.True(await page.IsVisibleAsync("#graph-svg"));
    Assert.True(await page.IsHiddenAsync("#view-export"));

    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    await page.ClickAsync("#tab-export");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    Assert.Equal("true", await page.GetAttributeAsync("#tab-export", "aria-selected")); // the tab is remembered, as the others are
    Assert.True(await page.IsVisibleAsync(Box("Logger")));
    Assert.Empty(opened.Errors);
  }
}
