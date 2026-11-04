using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Widely used items and predeclared classes (step D4 of the feedback plan): what's used by at least a share of the
/// other modules is listed in a Widely used group, with its widely used members, marked where it is, and hidden all at
/// once with Hide all; the share is a setting in the Show… menu.
/// </summary>
[Collection("Browser")]
public class WidelyUsedTests(PageFixture fixture)
{
  const string Group = "#explorer-tree > li.widely-used-items";

  static Task<string[]> Texts(IPage page, string selector) => page.Locator(selector).AllTextContentsAsync().ContinueWith(t => t.Result.ToArray());

  static Task<string[]> Listed(IPage page) => Texts(page, Group + " > ul > li > .tree-row .tree-label");

  static Task<string[]> GraphNodes(IPage page) =>
      page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id).sort()");

  static string LayoutRow(string id) => $"#explorer-tree li.item:not(.widely-used-items li)[data-id='{id}'] > .tree-row";

  static async Task SetShare(IPage page, string percent)
  {
    if (!await page.EvaluateAsync<bool>("() => document.getElementById('explorer-show').open")) await page.ClickAsync("#explorer-show summary");
    await page.FillAsync("#explorer-widely-used-share", percent);
  }

  [Fact]
  public async Task The_group_lists_modules_used_by_at_least_the_share_most_used_first_with_their_widely_used_members()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    // A fifth of the 11 other modules is 3 or more: Logger is used by 8, Order by 5 and IOrderRepository by 4.
    Assert.Equal(["Logger", "Order", "IOrderRepository"], await Listed(page));
    Assert.Equal("used by 8", await page.TextContentAsync(Group + " li[data-id='Logger'] > .tree-row .tree-detail"));
    Assert.Equal(["LogInfo", "LogError"], await Texts(page, Group + " li[data-id='Logger'] > ul > li > .tree-row .tree-label"));
    Assert.Equal("Widely used: used by 8 of the 11 other modules",
        await page.GetAttributeAsync(LayoutRow("Logger") + " .tree-wide", "title"));
    Assert.Equal(0, await page.Locator(LayoutRow("Utils") + " .tree-wide").CountAsync());
    Assert.Empty(await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()")); // nothing is hidden to begin with
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Hide_all_hides_them_from_every_view_and_then_shows_them_again()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;
    var all = await GraphNodes(page);

    await page.ClickAsync(Group + " [data-hide-widely-used]");

    Assert.Equal(all.Except(["Logger", "Order", "IOrderRepository"]), await GraphNodes(page));
    Assert.Equal(["IOrderRepository", "Logger", "Order"], (await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()")).Order());
    Assert.Equal("Show all", await page.TextContentAsync(Group + " [data-hide-widely-used]"));
    await page.ClickAsync("#tab-classes");
    await page.WaitForFunctionAsync("() => window.vbadocClasses.drawn()");
    Assert.Equal(0, await page.Locator("#classes-svg g.class-box[data-module='Logger']").CountAsync());

    await page.ClickAsync(Group + " [data-hide-widely-used]");
    Assert.Empty(await page.EvaluateAsync<string[]>("() => window.vbadoc.hidden()"));
    Assert.Equal("Hide all", await page.TextContentAsync(Group + " [data-hide-widely-used]"));
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task Changing_the_share_updates_the_group_and_the_markers_and_is_remembered()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
    var page = opened.Page;

    await SetShare(page, "50");
    Assert.Equal(["Logger"], await Listed(page));
    Assert.Equal(0, await page.Locator(LayoutRow("Order") + " .tree-wide").CountAsync());

    await SetShare(page, "100");
    Assert.Equal(0, await page.Locator(Group).CountAsync());
    Assert.Equal(0, await page.Locator("#explorer-tree .tree-wide").CountAsync());
    Assert.Equal("Show…", await page.InnerTextAsync("#explorer-show summary")); // the share isn't a Show… choice

    await page.ReloadAsync();
    await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
    Assert.Equal("100", await page.InputValueAsync("#explorer-widely-used-share"));
    Assert.Equal(0, await page.Locator(Group).CountAsync());
    Assert.Empty(opened.Errors);
  }

  [Fact]
  public async Task A_predeclared_class_is_labelled_so_in_the_explorer_and_the_source_panel()
  {
    await using var opened = await OpenedPage.OpenAsync(fixture, fixture.PredeclaredEmbedded);
    var page = opened.Page;

    Assert.Equal("Predeclared", await page.TextContentAsync(LayoutRow("Settings") + " .tree-detail"));
    Assert.Equal(0, await page.Locator(LayoutRow("Order") + " .tree-detail").CountAsync()); // an ordinary class
    await opened.SelectAsync("Settings");
    Assert.Contains("Predeclared class", await page.TextContentAsync("#side-panel-header .kind"));
    Assert.Empty(opened.Errors);
  }
}
