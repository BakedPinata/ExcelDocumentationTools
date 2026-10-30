using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>Home, the trail, Path from Home and Back/Forward, on SampleVba (see SampleVbaDependencyTests for its edges).</summary>
[Collection("Browser")]
public class NavigationTests(PageFixture fixture)
{
    static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

    static Task<IReadOnlyList<string>> TrailSteps(IPage page) => page.Locator("#trail-steps li button").AllInnerTextsAsync();

    static Task<string[]> DrawnIds(IPage page, string selector) =>
        page.Locator(selector).EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)");

    static Task SetHome(OpenedPage opened, string id) => opened.Page.EvaluateAsync("id => window.vbadoc.setHome(id)", id);

    [Fact]
    public async Task The_star_in_the_source_panel_sets_Home_which_is_remembered_and_starred_in_the_graph()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order.Describe");
        Assert.Equal("false", await page.GetAttributeAsync("#home-toggle", "aria-pressed"));

        await page.ClickAsync("#home-toggle");

        Assert.Equal("true", await page.GetAttributeAsync("#home-toggle", "aria-pressed"));
        Assert.Equal(["Order"], await DrawnIds(page, "#graph-svg g.node.home")); // a member's module at module level
        Assert.Equal(["★ Order.Describe"], await TrailSteps(page));

        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
        Assert.Equal("Order.Describe", await page.EvaluateAsync<string?>("() => window.vbadoc.home()"));
        Assert.Equal(["★ Order.Describe"], await TrailSteps(page)); // Home shows even with nothing selected

        await page.ClickAsync("[data-level='members']");
        Assert.Equal(["Order.Describe"], await DrawnIds(page, "#graph-svg g.node.home"));

        // Clicking the star again clears Home.
        await opened.SelectAsync("Order.Describe");
        await page.ClickAsync("#home-toggle");
        Assert.Null(await page.EvaluateAsync<string?>("() => window.vbadoc.home()"));
        Assert.Equal(0, await page.Locator("#graph-svg g.node.home").CountAsync());
        Assert.Empty(opened.Errors);
    }

    [Fact]
    public async Task A_Home_module_at_member_level_stars_its_outline()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await SetHome(opened, "Utils");
        await page.ClickAsync("[data-level='members']");

        Assert.Equal("★ Utils", await page.TextContentAsync("#graph-svg g.hull-group.home text.hull-label"));
        Assert.Equal(0, await page.Locator("#graph-svg g.node.home:not(.module-node)").CountAsync());
    }

    [Fact]
    public async Task The_trail_records_the_route_and_collapses_loops()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Main");
        Assert.True(await page.IsHiddenAsync("#trail")); // a single step says nothing new

        await opened.SelectAsync("Order");
        await opened.SelectAsync("Order.Describe");
        await opened.SelectAsync("Utils");
        Assert.Equal(["Main", "Order", "Order.Describe", "Utils"], await TrailSteps(page));
        Assert.Equal("Utils", await page.InnerTextAsync("#trail-steps [aria-current='step']"));

        // Coming back to a step cuts the trail back to it.
        await opened.SelectAsync("Order");
        Assert.Equal(["Main", "Order"], await TrailSteps(page));

        // Clicking a step goes back to it; clearing the selection keeps the trail.
        await opened.SelectAsync("Logger");
        await page.ClickAsync("#trail-steps button[data-step='0']");
        Assert.Equal("Main", await Selection(page));
        Assert.True(await page.IsHiddenAsync("#trail")); // back to a single step
        await opened.SelectAsync("Order");
        await opened.SelectAsync(null!);
        Assert.Equal(["Main", "Order"], await TrailSteps(page));
    }

    [Fact]
    public async Task With_a_Home_the_trail_starts_there_and_clearing_returns_to_Home()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Logger");
        await SetHome(opened, "Main");
        Assert.Equal(["★ Main", "Logger"], await TrailSteps(page)); // a new Home starts a new trail, from where you are

        await opened.SelectAsync("Order");
        await page.ClickAsync("#trail-clear");
        Assert.Equal(["★ Main", "Order"], await TrailSteps(page));

        await page.ClickAsync("#trail-steps button.home");
        Assert.Equal("Main", await Selection(page));
        Assert.Equal(["★ Main"], await TrailSteps(page));
        Assert.True(await page.IsHiddenAsync("#trail-clear"));
    }

    [Fact]
    public async Task A_long_trail_shows_its_start_and_end_until_expanded()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        string[] route = ["Main", "Order", "Utils", "Logger", "OrderService", "ProgressReporter", "AppInit", "Sheet1", "ThisWorkbook"];
        foreach (var id in route) await opened.SelectAsync(id);

        Assert.Equal(["Main", "…", "OrderService", "ProgressReporter", "AppInit", "Sheet1", "ThisWorkbook"], await TrailSteps(page));

        await page.ClickAsync("#trail-more");
        Assert.Equal(route, await TrailSteps(page));
    }

    [Fact]
    public async Task Path_from_Home_highlights_the_shortest_chain_in_either_direction()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        Assert.True(await page.IsDisabledAsync("#graph-path")); // no Home yet
        await SetHome(opened, "Main");
        await opened.SelectAsync("Main");
        Assert.True(await page.IsDisabledAsync("#graph-path")); // Home is the selection

        // Main uses Order, which uses Utils.
        await opened.SelectAsync("Utils");
        await page.ClickAsync("#graph-path");
        Assert.Equal("true", await page.GetAttributeAsync("#graph-path", "aria-pressed"));
        Assert.Equal(["Main", "Order", "Utils"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());
        Assert.Equal(2, await page.Locator("#graph-svg path.edge.highlighted").CountAsync());
        Assert.Equal(1, await page.Locator("#graph-svg path.edge.highlighted[data-from='Main'][data-to='Order']").CountAsync());
        Assert.Equal("Home uses the selection through 1 other node.", await page.InnerTextAsync("#graph-path-note"));

        // The path follows the selection, and takes the shorter direction: Sheet1 uses Main directly, while
        // Main reaches Sheet1 only through another module.
        await opened.SelectAsync("Sheet1");
        Assert.Equal("The selection uses Home directly.", await page.InnerTextAsync("#graph-path-note"));
        Assert.Equal(["Main", "Sheet1"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());

        // Clicking the background turns it off along with the selection.
        await page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        Assert.Equal("false", await page.GetAttributeAsync("#graph-path", "aria-pressed"));
        Assert.True(await page.IsHiddenAsync("#graph-path-note"));
        Assert.Equal(0, await page.Locator("#graph-svg .dim").CountAsync());
        Assert.Empty(opened.Errors);
    }

    [Fact]
    public async Task Path_from_Home_says_when_nothing_links_them_and_works_at_member_level()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await SetHome(opened, "Main");
        await opened.SelectAsync("Utils");
        await page.ClickAsync("#graph-filters summary");
        await page.UncheckAsync("input[data-edge-kind='uses']");
        await page.Keyboard.PressAsync("Escape");

        await page.ClickAsync("#graph-path");
        Assert.Equal("No chain of dependencies links Home and the selection with the current Show… choices.",
            await page.InnerTextAsync("#graph-path-note"));
        Assert.Equal(["Main", "Utils"], (await DrawnIds(page, "#graph-svg g.node:not(.dim)")).Order());

        await page.ClickAsync("#graph-filters summary");
        await page.ClickAsync("#graph-show-everything");
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("[data-level='members']");
        await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
        await opened.SelectAsync("Utils.FormatMoney");
        if (await page.GetAttributeAsync("#graph-path", "aria-pressed") != "true") await page.ClickAsync("#graph-path");

        // At member level the chain runs from one of Main's members to FormatMoney, through Order.Describe.
        var lit = await DrawnIds(page, "#graph-svg g.node:not(.dim)");
        Assert.Contains("Utils.FormatMoney", lit);
        Assert.Contains("Order.Describe", lit);
        Assert.StartsWith("Home uses the selection", await page.InnerTextAsync("#graph-path-note"));
    }

    [Fact]
    public async Task Back_and_Forward_step_through_selections()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await page.ClickAsync("#graph-svg g.node[data-id='Order']");
        await opened.SelectAsync("Order.Status[Let]");
        Assert.EndsWith("#Order.Status%5BLet%5D", page.Url);

        await page.GoBackAsync();
        Assert.Equal("Order", await Selection(page));
        Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='Order']").CountAsync());

        await page.GoBackAsync();
        Assert.Null(await Selection(page));

        await page.GoForwardAsync();
        await page.GoForwardAsync();
        Assert.Equal("Order.Status[Let]", await Selection(page));
    }

    [Fact]
    public async Task An_address_with_a_fragment_opens_with_that_selection()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;

        await page.GotoAsync(PageFixture.PageUrl(fixture.SampleEmbedded) + "#Order.Describe");
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => !!window.vbadocGraph");

        Assert.Equal("Order.Describe", await Selection(page));
        Assert.Contains("Describe", await page.InnerTextAsync("#source-header h2"));
        Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='Order']").CountAsync());

        // An unknown name is ignored.
        await page.GotoAsync(PageFixture.PageUrl(fixture.SampleEmbedded) + "#NoSuchThing");
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => !!window.vbadocGraph");
        Assert.Null(await Selection(page));
        Assert.Empty(opened.Errors);
    }
}
