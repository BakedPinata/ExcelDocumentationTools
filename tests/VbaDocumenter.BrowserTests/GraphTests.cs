using System.Diagnostics;
using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The dependency graph view, on SampleVba (see SampleVbaDependencyTests for its 29 module edges).</summary>
[Collection("Browser")]
public class GraphTests(PageFixture fixture)
{
    static Task<int> Count(IPage page, string selector) => page.Locator(selector).CountAsync();

    static Task<string?> Selection(IPage page) => page.EvaluateAsync<string?>("() => window.vbadoc.selection()");

    static async Task ShowMembers(IPage page)
    {
        await page.ClickAsync("[data-level='members']");
        await page.WaitForFunctionAsync("() => window.vbadocGraph.level() === 'members'");
    }

    [Fact]
    public async Task Module_level_draws_every_module_and_dependency()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;

        Assert.Equal(12, await Count(page, "#graph-svg g.node"));
        Assert.Equal(29, await Count(page, "#graph-svg path.edge"));
        Assert.Equal(2, await Count(page, "#graph-svg path.edge.implements"));
        Assert.Equal(1, await Count(page, "#graph-svg path.edge.events"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node.hollow")); // the interface
        Assert.Equal("IOrderRepository", await page.GetAttributeAsync("#graph-svg g.node.hollow", "data-id"));
        Assert.Empty(opened.Errors);
    }

    [Fact]
    public async Task The_legend_explains_kinds_and_all_three_edge_types()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var legend = await opened.Page.InnerTextAsync("#graph-legend");

        foreach (var text in new[] { "Standard module", "Class", "Interface", "UserForm", "Document module", "Uses", "Implements", "Events" })
            Assert.Contains(text, legend);
    }

    [Fact]
    public async Task Clicking_a_node_selects_it_and_highlights_its_direct_neighbours()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;

        await page.ClickAsync("#graph-svg g.node[data-id='Main']");

        Assert.Equal("Main", await Selection(page));
        Assert.Contains("Main", await page.InnerTextAsync("#source-header h2"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node.selected"));
        Assert.Equal(9, await Count(page, "#graph-svg g.node.related")); // 8 it uses + Sheet1, which uses it
        Assert.Equal(9, await Count(page, "#graph-svg path.edge.highlighted"));
        Assert.Equal(2, await Count(page, "#graph-svg g.node.dim")); // Utils and ThisWorkbook
        Assert.Equal(1, await Count(page, "#graph-svg g.node.dim[data-id='Utils']"));
    }

    [Fact]
    public async Task Transitive_highlights_the_whole_chain_in_each_direction()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await page.ClickAsync("#graph-svg g.node[data-id='Main']");

        await page.ClickAsync("[data-reach='transitive']");

        // Main -> Order -> Utils downstream; ThisWorkbook -> AppInit -> Sheet1 -> Main upstream.
        Assert.Equal(0, await Count(page, "#graph-svg g.node.dim"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='Utils']"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='ThisWorkbook']"));
    }

    [Fact]
    public async Task Clicking_the_background_clears_the_selection()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await page.ClickAsync("#graph-svg g.node[data-id='Order']");

        await page.EvaluateAsync("() => document.getElementById('graph-svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))");

        Assert.Null(await Selection(page));
        Assert.Equal(0, await Count(page, "#graph-svg .dim"));
    }

    [Fact]
    public async Task Member_level_groups_members_by_module_and_shows_recursion()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;

        await ShowMembers(page);

        Assert.Equal(12, await Count(page, "#graph-svg g.hull-group"));
        Assert.Equal(1, await Count(page, "#graph-svg g.hull-group[data-module='Order'] text.hull-label"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node[data-id='Order.Status[Let]']"));
        Assert.Equal(1, await Count(page, "#graph-svg path.edge.self-loop[data-from='Main.Factorial']"));
        Assert.Equal(1, await Count(page, "#graph-svg path.edge.events[data-from='ProgressReporter.m_svc_Progress'][data-to='OrderService.Progress']"));
        Assert.Equal(1, await Count(page, "#graph-svg path.edge.implements[data-from='CsvOrderRepository.IOrderRepository_Save']"));
        Assert.Equal(1, await Count(page, "#graph-svg g.node.unreferenced[data-id='Utils.UnusedHelper']"));
        Assert.Empty(opened.Errors);
    }

    [Fact]
    public async Task A_member_node_shows_its_own_source_and_maps_to_its_module_at_module_level()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await ShowMembers(page);

        await page.ClickAsync("#graph-svg g.node[data-id='Order.Describe']", new() { Force = true });

        Assert.Equal(new[] { 42, 43, 44 }, await opened.ShownLinesAsync());
        Assert.Equal(1, await Count(page, "#graph-svg g.node.related[data-id='Utils.FormatMoney']"));

        await page.ClickAsync("[data-level='modules']");
        Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='Order']"));
        Assert.Equal("Order.Describe", await Selection(page)); // the selection itself is unchanged
    }

    [Fact]
    public async Task A_module_selected_at_member_level_highlights_its_whole_cluster()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await ShowMembers(page);

        await opened.SelectAsync("Logger");

        Assert.Equal(3, await Count(page, "#graph-svg g.node.selected")); // LogInfo, LogError, WriteLine
        Assert.Equal(0, await Count(page, "#graph-svg g.hull-group.dim[data-module='Logger']"));
    }

    [Fact]
    public async Task The_selection_survives_switching_tabs()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await page.ClickAsync("#graph-svg g.node[data-id='OrderService']");

        await page.ClickAsync("#tab-classes");
        await page.ClickAsync("#tab-graph");

        Assert.Equal(1, await Count(page, "#graph-svg g.node.selected[data-id='OrderService']"));
    }

    [Fact]
    public async Task Filters_hide_member_kinds_or_show_only_unreferenced_members()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await ShowMembers(page);
        var before = await Count(page, "#graph-svg g.node");

        await page.ClickAsync("#graph-filters summary");
        await page.UncheckAsync("input[data-category='data']");

        Assert.Equal(0, await Count(page, "#graph-svg g.node[data-id='Order.m_Id']"));
        Assert.True(await Count(page, "#graph-svg g.node") < before);

        await page.CheckAsync("input[data-category='data']");
        await page.CheckAsync("#graph-unreferenced-only");
        Assert.Equal(new[] { "Utils.UnusedHelper" }, await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)"));
    }

    [Fact]
    public async Task Focus_shows_only_the_selection_and_its_neighbours_until_Show_all()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        Assert.True(await page.IsDisabledAsync("#graph-focus"));
        await page.ClickAsync("#graph-svg g.node[data-id='Utils']");

        await page.ClickAsync("#graph-focus");

        // Utils uses Logger; Order uses Utils.
        Assert.Equal(new[] { "Logger", "Order", "Utils" },
            (await page.Locator("#graph-svg g.node").EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)")).Order());
        Assert.Equal("Show all", await page.InnerTextAsync("#graph-focus"));

        await page.ClickAsync("#graph-focus");
        Assert.Equal(12, await Count(page, "#graph-svg g.node"));
    }

    [Fact]
    public async Task Zooms_and_fits()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        var transform = "() => document.querySelector('#graph-svg .viewport').getAttribute('transform')";
        var fitted = await page.EvaluateAsync<string>(transform);

        await page.Mouse.MoveAsync(400, 400);
        await page.Mouse.WheelAsync(0, -400);
        await page.WaitForFunctionAsync($"t => ({transform})() !== t", fitted);

        await page.ClickAsync("#graph-fit");
        await page.WaitForFunctionAsync($"t => ({transform})() === t", fitted);
    }

    [Fact]
    public async Task Node_colours_follow_the_theme()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        var fill = "() => getComputedStyle(document.querySelector(\"#graph-svg g.node[data-id='Main'] .shape\")).fill";
        var dark = await page.EvaluateAsync<string>(fill);

        await page.ClickAsync("#theme-toggle");

        Assert.NotEqual(dark, await page.EvaluateAsync<string>(fill));
    }

    [Fact]
    public async Task A_large_project_renders_both_levels_in_reasonable_time()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.LargeEmbedded);
        var page = opened.Page;
        Assert.Equal(150, await Count(page, "#graph-svg g.node"));

        var timer = Stopwatch.StartNew();
        await ShowMembers(page);
        timer.Stop();

        Assert.True(await Count(page, "#graph-svg g.node") > 1300);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"Member level took {timer.Elapsed.TotalSeconds:F1}s");
        await opened.SelectAsync("Mod042.P3");
        Assert.True(await Count(page, "#graph-svg g.node.related") >= 4);
        Assert.Empty(opened.Errors);
    }
}
