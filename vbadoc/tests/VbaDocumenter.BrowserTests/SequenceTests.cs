using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// The sequence diagram (step E5 of the feedback plan): what's open in the Calls tree, drawn with modules as lifelines,
/// calls as messages in order and blocks as frames, sharing what's open with the tree so either narrows both. A call
/// made by a frame's own statement is the frame's first message, and each diagram's participants stay in view while
/// it's scrolled through (step L3 of the generalisation plan).
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
        ["Init", "Attach", "loop bounds: LoadAll", "value tested: Status", "Process", "LogInfo", "Id", "Summarise", "legacy.ExportAll", "Factorial", "LogError"],
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

    // The loop's own call, repo.LoadAll, is the frame's, so it goes with it.
    Assert.Equal(["Init", "Attach", "Summarise", "legacy.ExportAll", "Factorial", "LogError"], await Messages(page));
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
    Assert.Equal(["Process", "condition: IsValid", "Save", "Progress", "Describe", "condition: Status", "Status", "LogError", "Id", "LogInfo"],
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

  const string Macros = "#calls-tree li.group > ul > li";

  static async Task<IPage> OpenAsSequence(OpenedPage opened, params string[] procedures)
  {
    var page = opened.Page;
    await page.ClickAsync("#tab-calls");
    foreach (var id in procedures) await page.ClickAsync($"{Macros}[data-id='{id}'] > .tree-row .tree-twisty");
    await page.ClickAsync("[data-calls-show='sequence']");
    await page.WaitForSelectorAsync(Diagram);
    return page;
  }

  [Fact]
  public async Task A_frames_own_calls_are_its_first_messages_even_when_nothing_inside_it_makes_a_call()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ConditionsEmbedded);
    var page = await OpenAsSequence(opened, "Conditions.CheckBoth");

    // If IsA(1) Or IsB(2) Then … Else DoB … Do While IsA(x): before step L3 the If had no frame at all.
    Assert.Equal(["if", "else", "do"], await Frames(page));
    Assert.Equal(["condition: IsA", "condition: IsB", "DoB", "condition: IsA"], await Messages(page));
    Assert.Equal(3, await page.Locator(Diagram + " g.seq-message.condition").CountAsync());
    // Each is drawn inside its frame.
    Assert.True(await page.EvaluateAsync<bool>(@"() => {
      const frame = document.querySelector('#calls-sequence .seq-frame.kind-if rect').getBoundingClientRect();
      return [...document.querySelectorAll('#calls-sequence g.seq-message.condition')].slice(0, 2).every(m => {
        const r = m.querySelector('.seq-name').getBoundingClientRect();
        return r.top >= frame.top && r.bottom <= frame.bottom;
      });
    }"));
    Assert.Empty(opened.Errors);
  }

  // Each diagram's strip (its title and participants again), with where it is against the top of what scrolls.
  const string Strips = @"() => {
    const body = document.querySelector('#view-calls .calls-body').getBoundingClientRect();
    return JSON.stringify([...document.querySelectorAll('#calls-sequence .seq-sticky')].map(s => {
      const r = s.getBoundingClientRect();
      const own = s.parentNode.querySelector('svg.calls-seq').getBoundingClientRect();
      return { title: s.querySelector('.seq-title').textContent, lanes: [...s.querySelectorAll('.seq-lane-head text')].map(t => t.textContent),
        top: Math.round(r.top - body.top), bottom: Math.round(r.bottom - body.top), left: Math.round(r.left - own.left) };
    }));
  }";

  sealed record Strip(string Title, string[] Lanes, int Top, int Bottom, int Left);

  static async Task<Strip[]> StripsOf(IPage page) =>
      System.Text.Json.JsonSerializer.Deserialize<Strip[]>(await page.EvaluateAsync<string>(Strips), System.Text.Json.JsonSerializerOptions.Web)!;

  [Fact]
  public async Task Each_diagrams_participants_stay_in_view_while_it_is_scrolled_through_and_move_sideways_with_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.ConditionsEmbedded, width: 420, height: 500, initScript: OpenedPage.WithoutExplorer);
    var page = await OpenAsSequence(opened, "Conditions.CheckBoth", "Conditions.ManyCalls");
    Assert.Equal(2, await page.Locator(Diagram).CountAsync()); // a diagram for each, with its own participants

    // At the top, each strip sits exactly over its diagram's own title and participants.
    var strips = await StripsOf(page);
    Assert.Equal(["Calls from Conditions.CheckBoth", "Calls from Conditions.ManyCalls"], strips.Select(s => s.Title));
    Assert.Equal(["Conditions", "Logger"], strips[1].Lanes);
    Assert.True(strips[0].Top > 0);

    // Scrolled well into the second diagram: its participants are at the top of the view, and the first's have gone.
    await page.EvaluateAsync("() => { const b = document.querySelector('#view-calls .calls-body'); b.scrollTop = b.scrollHeight - b.clientHeight - 40; }");
    strips = await StripsOf(page);
    Assert.Equal(0, strips[1].Top);
    Assert.True(strips[0].Bottom <= 0, "the first diagram's participants stay with their own diagram");
    Assert.True(await page.EvaluateAsync<bool>(@"() => {
      const body = document.querySelector('#view-calls .calls-body').getBoundingClientRect();
      const own = [...document.querySelectorAll('#calls-sequence svg.calls-seq')][1].querySelector('.seq-title').getBoundingClientRect();
      return own.bottom < body.top;   // the diagram's own title is scrolled away: it's the strip that shows
    }"));

    // Sideways, the strip goes with the diagram.
    await page.EvaluateAsync("() => { document.querySelector('#view-calls .calls-body').scrollLeft = 60; }");
    Assert.True(await page.EvaluateAsync<int>("() => document.querySelector('#view-calls .calls-body').scrollLeft") > 0, "the diagram should be wider than the view");
    strips = await StripsOf(page);
    Assert.Equal(0, strips[1].Left);
    Assert.Equal(0, strips[1].Top);

    // A participant in the strip selects its module, as the diagram's own does.
    await page.EvaluateAsync("() => { document.querySelector('#view-calls .calls-body').scrollLeft = 0; }");
    await page.Locator("#calls-sequence .seq-sticky").Nth(1).Locator(".seq-lane-head[data-seq-module='Conditions']").ClickAsync();
    Assert.Equal("Conditions", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
    Assert.Empty(opened.Errors);
  }
}
