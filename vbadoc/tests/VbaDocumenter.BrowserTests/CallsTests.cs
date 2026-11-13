using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The Calls tab (steps E2 and E3 of the feedback plan): the procedures an entry point calls, in the order of its code,
/// opened one level at a time, inside the blocks around them, with recursion, calls through interfaces and events,
/// hidden items counted, late-bound calls, Calls / Called by, and links to procedures and to the lines of their calls.
/// A call made by a block's own statement, such as an If's condition, is that block's (step L3 of the generalisation
/// plan).
/// </summary>
[Collection("Browser")]
public class CallsTests(PageFixture fixture)
{
  static readonly PageClickOptions CtrlClick = new() { Modifiers = [KeyboardModifier.Control] };

  static string Row(string id) => $"#calls-tree li[data-id='{id}']";

  static string Child(string parent, string id) => $"{parent} > ul > li[data-id='{id}']";

  // A call anywhere under a row, whatever blocks it's in.
  static string Under(string parent, string id) => $"{parent} li[data-id='{id}']";

  static string Block(string parent, string text) => $"{parent} > ul > li.calls-block:has(> .tree-row code[title='{text.Replace("'", "\\'")}'])";

  static Task<string[]> ChildNames(IPage page, string parent) =>
      page.Locator(parent + " > ul > li > .tree-row").EvaluateAllAsync<string[]>(
          "rs => rs.map(r => { const n = r.querySelector('.calls-name, code'); const l = r.querySelector('.calls-line'); return n ? (n.title || n.textContent) + (l ? ' ' + l.textContent : '') : null; }).filter(n => n !== null)");

  static async Task<IPage> OpenCalls(OpenedPage opened)
  {
    await opened.Page.ClickAsync("#tab-calls");
    await opened.Page.WaitForSelectorAsync("#calls-tree li");
    return opened.Page;
  }

  static Task Open(IPage page, string row) => page.ClickAsync(row + " > .tree-row .tree-twisty");

  const string ProcessOrders = "#calls-tree li.group > ul > li[data-id='Main.ProcessOrders']";

  [Fact]
  public async Task It_starts_from_the_entry_points_and_a_procedure_opens_to_its_calls_in_order()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);

    Assert.Equal(["Macros", "Event handlers", "Worksheet functions"],
        await page.Locator("#calls-tree > li.group > .tree-row .tree-label").AllInnerTextsAsync());
    await Open(page, ProcessOrders);

    // Calls inside a block are in its row (step E3), with what its own statement calls first (step L3): repo.LoadAll is
    // the loop's. The If around the repository's choice has no calls, so it isn't there.
    Assert.Equal(
        ["OrderService.Init line 18", "ProgressReporter.Attach line 19", "For Each o In repo.LoadAll line 21",
         "With svc line 32", "legacy.ExportAll line 37", "Main.Factorial line 38", "On error: ErrHandler line 40"],
        await ChildNames(page, ProcessOrders));
    Assert.Equal("late-bound", await page.InnerTextAsync(ProcessOrders + " > ul > li.calls-late .tree-detail"));
    Assert.Contains("Application.Run", await page.InnerTextAsync("#view-calls .calls-note"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Recursion_is_marked_and_doesnt_open_and_interfaces_and_events_open_to_what_runs()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await Open(page, ProcessOrders);

    var factorial = Child(ProcessOrders, "Main.Factorial");
    await Open(page, factorial);
    var again = Under(factorial, "Main.Factorial"); // in the Else of its single-line If
    Assert.Equal(1, await page.Locator(again + " .calls-recursion").CountAsync());
    Assert.Null(await page.GetAttributeAsync(again, "aria-expanded"));

    var loadAll = Under(ProcessOrders, "IOrderRepository.LoadAll"); // in the loop's row, as its bounds
    await Open(page, loadAll);
    Assert.Equal(["CsvOrderRepository.IOrderRepository_LoadAll", "SheetOrderRepository.IOrderRepository_LoadAll"],
        await page.Locator(loadAll + " > ul > li .calls-name").AllInnerTextsAsync());
    Assert.Equal("implemented by", await page.InnerTextAsync(loadAll + " > ul > li .calls-relation >> nth=0"));

    var process = Under(ProcessOrders, "OrderService.Process");
    await Open(page, process);
    var progress = Under(process, "OrderService.Progress");
    await Open(page, progress);
    Assert.Equal("handled by ProgressReporter.m_svc_Progress", await page.InnerTextAsync(progress + " > ul > li .tree-row .calls-relation") + " " +
        await page.InnerTextAsync(progress + " > ul > li .calls-name"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Calls_to_hidden_items_are_left_out_and_counted()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await page.EvaluateAsync("() => window.vbadoc.toggleHidden('Logger')");
    await Open(page, ProcessOrders);

    Assert.DoesNotContain(await ChildNames(page, ProcessOrders), name => name.StartsWith("Logger.", StringComparison.Ordinal));
    Assert.StartsWith("+ 2 calls to items the views don't show", await page.InnerTextAsync(ProcessOrders + " > ul > li.calls-left-out"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_click_only_selects_Ctrl_click_starts_the_tree_there_and_Entry_points_goes_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await Open(page, ProcessOrders);

    await page.ClickAsync(Child(ProcessOrders, "OrderService.Init") + " .calls-name");
    Assert.Equal("OrderService.Init", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal("true", await page.GetAttributeAsync(ProcessOrders, "aria-expanded")); // the opened path stays
    Assert.Equal("true", await page.GetAttributeAsync(Child(ProcessOrders, "OrderService.Init"), "aria-selected"));

    await page.ClickAsync(Under(ProcessOrders, "OrderService.Process") + " > .tree-row .calls-name", CtrlClick);
    Assert.Equal("OrderService.Process", await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal("Calls from OrderService.Process", await page.InnerTextAsync("#calls-root-note > span"));
    Assert.Equal("true", await page.GetAttributeAsync("#calls-tree > li[data-id='OrderService.Process']", "aria-expanded"));

    await page.ClickAsync("#calls-entry-points");
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.True(await page.IsHiddenAsync("#calls-root-note"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_focused_selection_in_another_view_starts_the_tree_and_clearing_it_goes_back()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    await opened.SelectAsync("Main.ProcessOrders"); // Focus on click is on to begin with
    var page = await OpenCalls(opened);

    Assert.Equal("Main.ProcessOrders", await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal(10, await page.Locator("#calls-tree > li[data-id='Main.ProcessOrders'] li[data-id]").CountAsync()); // and one late-bound

    await page.Keyboard.PressAsync("Escape");
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal(3, await page.Locator("#calls-tree > li.group").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Called_by_turns_the_tree_round_through_interfaces_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    await opened.SelectAsync("Logger.LogError");
    var page = await OpenCalls(opened);
    await page.ClickAsync("[data-calls-direction='calledBy']");

    const string root = "#calls-tree > li[data-id='Logger.LogError']";
    Assert.Equal(["CsvOrderRepository.IOrderRepository_Save line 23", "Main.ProcessOrders line 41", "OrderService.Process line 25"],
        await ChildNames(page, root));
    // Each caller says which block its call is in.
    Assert.Equal("in On error: ErrHandler", await page.InnerTextAsync(Child(root, "Main.ProcessOrders") + " > .tree-row .calls-within"));
    Assert.Equal("in Else", await page.InnerTextAsync(Child(root, "OrderService.Process") + " > .tree-row .calls-within"));

    // An implementation is reached through the interface member, so turned round it opens to what calls that.
    var save = Child(root, "CsvOrderRepository.IOrderRepository_Save");
    await Open(page, save);
    var member = Child(save, "IOrderRepository.Save");
    Assert.Equal("implements", await page.InnerTextAsync(member + " > .tree-row .calls-relation"));
    await Open(page, member);
    Assert.Equal(["OrderService.Process line 19"], await ChildNames(page, member)); // the exported class file has header lines

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocCalls");
    Assert.Equal("calledBy", await page.EvaluateAsync<string>("() => window.vbadocCalls.direction()"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_calls_line_selects_the_procedure_making_it_and_marks_the_line()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await Open(page, ProcessOrders);

    await page.ClickAsync(Under(ProcessOrders, "Logger.LogError") + " [data-call-line='41']");

    Assert.Equal("Main.ProcessOrders", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator("#side-panel .code .line.target[data-line='41']").CountAsync());
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()")); // still from the entry points
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Blocks_nest_as_the_code_does_and_hold_their_calls_in_order()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await Open(page, ProcessOrders);

    // What a block's own statement calls comes first in its row, marked: the loop's bounds, the Select's value.
    var loop = Block(ProcessOrders, "For Each o In repo.LoadAll");
    Assert.Equal(["IOrderRepository.LoadAll line 21", "Select Case o.Status line 22"], await ChildNames(page, loop));
    Assert.Equal("loop bounds", await page.InnerTextAsync(Child(loop, "IOrderRepository.LoadAll") + " > .tree-row .calls-condition"));
    var select = Block(loop, "Select Case o.Status");
    Assert.Equal(["Order.Status line 22", "Case \"NEW\" line 23", "Case \"HOLD\" line 25"], await ChildNames(page, select)); // Case Else has no calls
    Assert.Equal("value tested", await page.InnerTextAsync(Child(select, "Order.Status[Get]") + " > .tree-row .calls-condition"));
    Assert.Equal(["OrderService.Process line 24"], await ChildNames(page, Block(select, "Case \"NEW\"")));
    Assert.Equal(["Logger.LogInfo line 26", "Order.Id line 26"], await ChildNames(page, Block(select, "Case \"HOLD\"")));
    Assert.Equal(["Logger.LogError line 41"], await ChildNames(page, Block(ProcessOrders, "On error: ErrHandler")));

    // A called procedure's If, ElseIf and Else are branches side by side, each with its own calls.
    var process = Under(ProcessOrders, "OrderService.Process");
    await Open(page, process);
    Assert.Equal(
        ["If o.IsValid Then line 18", "ElseIf o.Status = \"DRAFT\" Then line 22", "Else line 24"],
        await ChildNames(page, process));
    var branch = Block(process, "If o.IsValid Then");
    Assert.Equal("Order.IsValid line 18", (await ChildNames(page, branch))[0]);
    Assert.Equal("condition", await page.InnerTextAsync(Child(branch, "Order.IsValid") + " > .tree-row .calls-condition"));
    // The ElseIf's condition reads Status, and its body sets it.
    Assert.Equal(["Order.Status line 22", "Order.Status line 23"], await ChildNames(page, Block(process, "ElseIf o.Status = \"DRAFT\" Then")));
    Assert.Equal(1, await page.Locator(Block(process, "ElseIf o.Status = \"DRAFT\" Then") + " > ul > li .calls-condition").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task An_If_whose_condition_makes_calls_shows_even_when_nothing_inside_it_does()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ConditionsEmbedded);
    var page = await OpenCalls(opened);
    const string check = "#calls-tree li.group > ul > li[data-id='Conditions.CheckBoth']";
    await Open(page, check);

    // Before step L3, IsA and IsB showed loose above the Else, and there was no If row.
    Assert.Equal(["If IsA(1) Or IsB(2) Then line 6", "Else line 8", "Do While IsA(x) line 11"], await ChildNames(page, check));
    var branch = Block(check, "If IsA(1) Or IsB(2) Then");
    Assert.Equal(["Conditions.IsA line 6", "Conditions.IsB line 6"], await ChildNames(page, branch));
    Assert.Equal(["condition", "condition"], await page.Locator(branch + " > ul > li > .tree-row .calls-condition").AllInnerTextsAsync());
    Assert.Equal(["Conditions.DoB line 9"], await ChildNames(page, Block(check, "Else")));
    Assert.Equal(["Conditions.IsA line 11"], await ChildNames(page, Block(check, "Do While IsA(x)")));

    // Turned round, the caller says the call is in the block's condition.
    await opened.SelectAsync("Conditions.IsB");
    await page.ClickAsync("[data-calls-direction='calledBy']");
    Assert.Equal("in the condition of If IsA(1) Or IsB(2) Then",
        await page.InnerTextAsync("#calls-tree > li[data-id='Conditions.IsB'] > ul > li[data-id='Conditions.CheckBoth'] > .tree-row .calls-within"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_block_closes_and_opens_and_its_line_selects_its_procedure()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    await Open(page, ProcessOrders);
    var with = Block(ProcessOrders, "With svc");
    Assert.Equal("true", await page.GetAttributeAsync(with, "aria-expanded")); // open to begin with

    await Open(page, with);
    Assert.Equal("false", await page.GetAttributeAsync(with, "aria-expanded"));
    Assert.False(await page.IsVisibleAsync(Under(with, "OrderService.Summarise")));
    await Open(page, Under(ProcessOrders, "Main.Factorial")); // drawing the tree again keeps it closed
    Assert.Equal("false", await page.GetAttributeAsync(with, "aria-expanded"));

    await page.ClickAsync(with + " > .tree-row [data-call-line='32']");
    Assert.Equal("Main.ProcessOrders", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Equal(1, await page.Locator("#side-panel .code .line.target[data-line='32']").CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task The_keyboard_moves_through_the_tree_opens_and_selects()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    const string focused = "() => document.activeElement.closest('li').dataset.id || document.activeElement.closest('li').querySelector('.tree-label').textContent";

    await page.FocusAsync("#calls-tree > li:first-child");
    Assert.Equal("Macros", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("ArrowDown");
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("Main.ProcessOrders", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("ArrowRight");
    Assert.Equal("true", await page.GetAttributeAsync(ProcessOrders, "aria-expanded"));
    await page.Keyboard.PressAsync("ArrowDown");
    Assert.Equal("OrderService.Init", await page.EvaluateAsync<string>(focused));
    await page.Keyboard.PressAsync("Enter");
    Assert.Equal("OrderService.Init", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Open_all_below_opens_every_call_under_a_procedure_as_far_as_recursion_allows()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);

    await page.HoverAsync(ProcessOrders + " > .tree-row");
    await page.ClickAsync(ProcessOrders + " > .tree-row .calls-open-all");

    Assert.Equal(0, await page.Locator(ProcessOrders + " li[aria-expanded='false']").CountAsync());
    var save = Under(Under(ProcessOrders, "OrderService.Process"), "IOrderRepository.Save");
    Assert.Equal(2, await page.Locator(save + " > ul > li .calls-relation").CountAsync()); // its implementations, open too
    var recursion = Under(Under(ProcessOrders, "Main.Factorial"), "Main.Factorial");
    Assert.Equal(1, await page.Locator(recursion + " .calls-recursion").CountAsync());
    Assert.Null(await page.GetAttributeAsync(recursion, "aria-expanded"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Open_all_below_a_group_opens_each_of_its_procedures_and_the_star_key_does_it_from_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenCalls(opened);
    const string macros = "#calls-tree > li.group:has(> .tree-row .tree-label:text-is('Macros'))";

    await page.HoverAsync(macros + " > .tree-row");
    await page.ClickAsync(macros + " > .tree-row .calls-open-all");
    Assert.Equal("true", await page.GetAttributeAsync(macros + " > ul > li[data-id='AppInit.Startup']", "aria-expanded"));
    Assert.Equal("true", await page.GetAttributeAsync(ProcessOrders, "aria-expanded"));

    await page.ClickAsync("#calls-collapse");
    await page.FocusAsync(ProcessOrders);
    await page.Keyboard.PressAsync("*");
    Assert.Equal(0, await page.Locator(ProcessOrders + " li[aria-expanded='false']").CountAsync());
    Assert.Equal("true", await page.GetAttributeAsync(ProcessOrders, "aria-expanded"));
    Assert.Empty(opened.Errors);
  }
}
