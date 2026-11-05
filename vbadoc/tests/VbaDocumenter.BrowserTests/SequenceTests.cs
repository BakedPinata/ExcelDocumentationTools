using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The sequence diagram (step E5 of the feedback plan): what's open in the Calls tree, drawn with modules as lifelines,
/// calls as messages in order and blocks as frames, sharing what's open with the tree so either narrows both.
/// </summary>
[Collection("Browser")]
public class SequenceTests(PageFixture fixture)
{
  static readonly PageClickOptions CtrlClick = new() { Modifiers = [KeyboardModifier.Control] };

  const string ProcessOrders = "#calls-tree li.group > ul > li[data-id='Main.ProcessOrders']";
  const string Diagram = "#calls-sequence svg.calls-seq";

  static Task<string[]> Lifelines(IPage page) =>
      page.Locator(Diagram + " g.seq-lane").EvaluateAllAsync<string[]>("ls => ls.map(l => l.dataset.lane)");

  static Task<string[]> Messages(IPage page) =>
      page.Locator(Diagram + " g.seq-message .seq-name").EvaluateAllAsync<string[]>("ns => ns.map(n => n.textContent)");

  static Task<string[]> Frames(IPage page) =>
      page.Locator(Diagram + " g.seq-frame").EvaluateAllAsync<string[]>("fs => fs.map(f => f.getAttribute('class').match(/kind-(\\w+)/)[1])");

  static async Task<IPage> OpenProcessOrdersAsSequence(OpenedPage opened)
  {
    var page = opened.Page;
    await page.ClickAsync("#tab-calls");
    await page.ClickAsync(ProcessOrders + " > .tree-row .tree-twisty");
    await page.ClickAsync("[data-calls-show='sequence']");
    await page.WaitForSelectorAsync(Diagram);
    return page;
  }

  [Fact]
  public async Task It_draws_whats_open_in_the_tree_with_modules_as_lifelines_calls_in_order_and_blocks_as_frames()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenProcessOrdersAsSequence(opened);

    Assert.Equal(["Main", "OrderService", "ProgressReporter", "IOrderRepository", "Order", "Logger", "late-bound"], await Lifelines(page));
    Assert.Equal(
        ["Init", "Attach", "LoadAll", "Status", "Process", "LogInfo", "Id", "Summarise", "legacy.ExportAll", "Factorial", "LogError"],
        await Messages(page));
    Assert.Equal(["for", "select", "case", "case", "with", "onError"], await Frames(page));
    Assert.Equal(1, await page.Locator(Diagram + " g.seq-message.late").CountAsync());
    Assert.Equal("Calls from Main.ProcessOrders", await page.TextContentAsync(Diagram + " .seq-title"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Closing_a_frame_narrows_the_diagram_and_the_tree_alike()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenProcessOrdersAsSequence(opened);

    await page.ClickAsync(Diagram + " .seq-frame-label:has(title:text-matches('For Each'))");

    Assert.Equal(["Init", "Attach", "LoadAll", "Summarise", "legacy.ExportAll", "Factorial", "LogError"], await Messages(page));
    Assert.Equal(1, await page.Locator(Diagram + " g.seq-frame.closed.kind-for").CountAsync());
    await page.ClickAsync("[data-calls-show='tree']");
    Assert.Equal("false", await page.GetAttributeAsync(ProcessOrders + " > ul > li.calls-block", "aria-expanded"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_messages_toggle_opens_its_calls_with_their_frames_and_recursion_is_marked()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenProcessOrdersAsSequence(opened);

    await page.ClickAsync(Diagram + " g.seq-message[data-id='OrderService.Process'] .seq-toggle");
    var messages = await Messages(page);
    Assert.Equal(["Process", "IsValid", "Save", "Progress", "Describe", "Status", "Status", "LogError", "Id", "LogInfo"],
        messages.SkipWhile(m => m != "Process").Take(10));
    Assert.Contains("if", await Frames(page));
    Assert.Contains("elseIf", await Frames(page));

    await page.ClickAsync(Diagram + " g.seq-message[data-id='Main.Factorial'] .seq-toggle");
    Assert.Contains("Factorial ↻", await Messages(page));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_click_on_a_message_selects_and_Ctrl_click_starts_from_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenProcessOrdersAsSequence(opened);

    await page.ClickAsync(Diagram + " g.seq-message[data-id='OrderService.Init'] .seq-name");
    Assert.Equal("OrderService.Init", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal(1, await page.Locator(Diagram + " g.seq-message.selected[data-id='OrderService.Init']").CountAsync());

    await page.ClickAsync(Diagram + " g.seq-message[data-id='OrderService.Process'] .seq-name", CtrlClick);
    Assert.Equal("OrderService.Process", await page.EvaluateAsync<string?>("() => window.vbadocCalls.root()"));
    Assert.Equal("Calls from OrderService.Process", await page.TextContentAsync(Diagram + " .seq-title"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task With_nothing_open_it_offers_somewhere_to_start_and_the_choice_is_remembered_but_not_for_Called_by()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    await page.ClickAsync("#tab-calls");
    await page.ClickAsync("[data-calls-show='sequence']");

    await page.ClickAsync("#calls-sequence [data-seq-start]:text-is('Main.ProcessOrders')");
    Assert.Equal(11, (await Messages(page)).Length);

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocCalls");
    Assert.Equal("sequence", await page.EvaluateAsync<string>("() => window.vbadocCalls.showing()"));

    await page.ClickAsync("[data-calls-direction='calledBy']");
    Assert.True(await page.IsDisabledAsync("[data-calls-show='sequence']"));
    Assert.True(await page.IsHiddenAsync("#calls-sequence"));
    Assert.True(await page.IsVisibleAsync("#calls-tree"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Shift_click_opens_everything_below_a_message_and_every_frame_fits_its_whole_guard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = await OpenProcessOrdersAsSequence(opened);

    await page.ClickAsync(Diagram + " g.seq-message[data-id='OrderService.Process'] .seq-toggle", new() { Modifiers = [KeyboardModifier.Shift] });

    Assert.Contains("implemented by: IOrderRepository_Save", await Messages(page));
    Assert.Equal("[ElseIf o.Status = \"DRAFT\" Then]",
        await page.TextContentAsync(Diagram + " .seq-frame-label:has(title:text-matches('ElseIf')) .seq-guard"));
    Assert.True(await page.EvaluateAsync<bool>(@"() => [...document.querySelectorAll('#calls-sequence .seq-frame-label')].every(label => {
      const guard = label.querySelector('.seq-guard');
      if (!guard) return true;
      const frame = document.querySelector(`#calls-sequence .seq-frame[data-key='${CSS.escape(label.dataset.seqFrame)}'] rect`);
      return guard.getBoundingClientRect().right <= frame.getBoundingClientRect().right;
    })"));
    Assert.Empty(opened.Errors);
  }
}
