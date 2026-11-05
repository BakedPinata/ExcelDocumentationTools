namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// The details panel: what it lists, walking the graph from it, and hiding and resizing it. Tests of its size start
/// with it locked open, as it always was before it followed the selection.
/// </summary>
[Collection("Browser")]
public sealed class DetailsPanelTests(PageFixture fixture)
{
  private static async Task<string[]> Listed(OpenedPage opened, string list) =>
    (await opened.Page.Locator($"#details-{list} a[data-select]").AllInnerTextsAsync()).ToArray();

  [Fact]
  public async Task Invites_a_selection_until_there_is_one()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    Assert.Contains("Select a query", await opened.Page.InnerTextAsync("#side-panel-body"));
  }

  [Fact]
  public async Task Lists_what_a_query_uses_and_what_uses_it_in_name_order()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await opened.SelectAsync("Orders");

    Assert.Equal("Orders", await page.InnerTextAsync("#side-panel-header h2"));
    Assert.Equal("Intermediate query.", await page.InnerTextAsync("#details-role"));
    Assert.Equal(["fnCleanText", "Sales Region Map", "Source"], await Listed(opened, "uses"));
    Assert.Equal(["Q1.Summary", "Sales Report"], await Listed(opened, "used-by"));
    Assert.Equal(
      "Including indirect links, 3 queries feed this query and 3 queries depend on it.",
      await page.InnerTextAsync("#details-reach"));
  }

  [Theory]
  [InlineData("Source", "Root query: it uses no other query.")]
  [InlineData("Revenue & Margin (2024)", "Leaf query: no other query uses it.")]
  [InlineData("Unused Parameter", "Isolated query: not connected to any other query.")]
  [InlineData("Cycle A", "Intermediate query. Part of a circular dependency.")]
  public async Task Says_what_role_a_query_has(string query, string expected)
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);

    await opened.SelectAsync(query);

    Assert.Equal(expected, await opened.Page.InnerTextAsync("#details-role"));
  }

  [Fact]
  public async Task Says_None_and_reads_naturally_for_single_queries()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;

    await opened.SelectAsync("Cycle A");
    Assert.Equal("Including indirect links, 1 query feeds this query and 1 query depends on it.", await page.InnerTextAsync("#details-reach"));

    await opened.SelectAsync("Unused Parameter");
    Assert.Equal("None", await page.InnerTextAsync("#details-uses"));
    Assert.Equal("None", await page.InnerTextAsync("#details-used-by"));
  }

  [Fact]
  public async Task Clicking_an_entry_walks_to_that_query()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    await page.ClickAsync("#details-used-by a[data-select='Sales Report']");

    Assert.Equal("Sales Report", await opened.SelectionAsync());
    Assert.Equal("Sales Report", await page.InnerTextAsync("#side-panel-header h2"));
    Assert.Equal(["Sales Report"], await opened.DrawnIdsAsync("#graph-svg g.node.selected"));
  }

  [Fact]
  public async Task Clear_selection_empties_the_panel()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    await opened.SelectAsync("Orders");

    await page.ClickAsync("#details-clear");

    Assert.Null(await opened.SelectionAsync());
    Assert.Contains("Select a query", await page.InnerTextAsync("#side-panel-body"));
  }

  [Fact]
  public async Task Closing_it_by_hand_unlocks_it_so_it_stays_closed_until_something_is_selected()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    const string graphWidth = "() => document.getElementById('graph-svg').clientWidth";
    var narrow = await page.EvaluateAsync<int>(graphWidth);

    await page.ClickAsync("#side-panel-close");

    Assert.False(await page.IsVisibleAsync("#side-panel"));
    Assert.False(await page.IsVisibleAsync("#side-panel-splitter"));
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-toggle", "aria-pressed"));
    Assert.Equal("side-panel-toggle", await page.EvaluateAsync<string>("() => document.activeElement.id"));
    Assert.True(await page.EvaluateAsync<int>(graphWidth) > narrow);
    Assert.Equal("false", await page.GetAttributeAsync("#side-panel-lock", "aria-pressed"));

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.False(await page.IsVisibleAsync("#side-panel"));

    await page.ClickAsync("#side-panel-toggle");
    Assert.True(await page.IsVisibleAsync("#side-panel"));
  }

  [Fact]
  public async Task The_splitter_resizes_the_panel_and_the_size_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    const string panelWidth = "() => document.getElementById('side-panel').getBoundingClientRect().width";
    var before = await page.EvaluateAsync<double>(panelWidth);
    var splitter = (await page.Locator("#side-panel-splitter").BoundingBoxAsync())!;
    var x = splitter.X + splitter.Width / 2;
    var y = splitter.Y + splitter.Height / 2;

    await page.Mouse.MoveAsync(x, y);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(x - 100, y, new() { Steps = 5 });
    await page.Mouse.UpAsync();

    var after = await page.EvaluateAsync<double>(panelWidth);
    Assert.Equal(before + 100, after, 0);

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    Assert.Equal(after, await page.EvaluateAsync<double>(panelWidth), 0);

    await page.DblClickAsync("#side-panel-splitter"); // back to the default
    Assert.Equal(before, await page.EvaluateAsync<double>(panelWidth), 0);
  }

  [Fact]
  public async Task A_click_on_the_splitter_does_not_move_it()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    const string panelWidth = "() => document.getElementById('side-panel').getBoundingClientRect().width";
    var before = await page.EvaluateAsync<double>(panelWidth);

    await page.ClickAsync("#side-panel-splitter");

    Assert.Equal(before, await page.EvaluateAsync<double>(panelWidth), 0);
  }

  [Fact]
  public async Task The_splitter_works_from_the_keyboard()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage, initScript: OpenedPage.WithPanelLocked);
    var page = opened.Page;
    await page.FocusAsync("#side-panel-splitter");

    await page.Keyboard.PressAsync("End");
    Assert.Equal("70", await page.GetAttributeAsync("#side-panel-splitter", "aria-valuenow"));

    await page.Keyboard.PressAsync("Home");
    Assert.Equal("20", await page.GetAttributeAsync("#side-panel-splitter", "aria-valuenow"));

    await page.Keyboard.PressAsync("ArrowLeft");
    Assert.Equal("22", await page.GetAttributeAsync("#side-panel-splitter", "aria-valuenow"));
  }

  [Fact]
  public async Task The_graph_refits_when_the_panel_changes_unless_the_view_is_the_users_own()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SamplePage);
    var page = opened.Page;
    const string transform = "() => document.querySelector('#graph-svg .viewport').getAttribute('transform')";
    var fitted = await page.EvaluateAsync<string>(transform);

    await page.ClickAsync("#side-panel-toggle");
    await page.WaitForFunctionAsync($"t => ({transform})() !== t", fitted);

    await page.Mouse.MoveAsync(300, 400);
    await page.Mouse.WheelAsync(0, -200);
    var zoomed = await page.EvaluateAsync<string>(transform);
    await page.WaitForFunctionAsync($"t => ({transform})() !== t", fitted);

    await page.ClickAsync("#side-panel-toggle");

    // Same zoom: the view shifted to keep its middle, but it wasn't refitted.
    await page.WaitForFunctionAsync($"t => ({transform})() !== t", zoomed);
    Assert.Equal(Scale(zoomed), Scale(await page.EvaluateAsync<string>(transform)));
  }

  private static string Scale(string transform) => transform[transform.IndexOf("scale", StringComparison.Ordinal)..];
}
