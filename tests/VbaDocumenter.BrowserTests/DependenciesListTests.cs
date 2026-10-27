using Microsoft.Playwright;

namespace VbaDocumenter.BrowserTests;

/// <summary>The Dependencies list in the source panel, on SampleVba (see SampleVbaDependencyTests for its edges).</summary>
[Collection("Browser")]
public class DependenciesListTests(PageFixture fixture)
{
    static Task<IReadOnlyList<string>> Entries(IPage page, string direction) =>
        page.Locator($"#source-dependencies .dependency-list[data-direction='{direction}'] li a").AllInnerTextsAsync();

    [Fact]
    public async Task It_is_collapsed_by_default_and_counts_both_directions()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order");

        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('source-dependencies').open"));
        Assert.Equal("Dependencies uses 1 · used by 5", await page.InnerTextAsync("#source-dependencies summary"));
        Assert.Empty(opened.Errors);
    }

    [Fact]
    public async Task A_module_lists_module_dependencies_in_both_directions()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order");
        await page.ClickAsync("#source-dependencies summary");

        Assert.Equal(["Utils"], await Entries(page, "uses"));
        Assert.Equal(["CsvOrderRepository", "IOrderRepository", "Main", "OrderService", "SheetOrderRepository"], await Entries(page, "usedBy"));
        Assert.Contains("Standard module", await page.InnerTextAsync("#source-dependencies .dependency-list[data-direction='uses']"));
    }

    [Fact]
    public async Task A_member_lists_member_dependencies_naming_other_modules()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order.Describe");
        await page.ClickAsync("#source-dependencies summary");

        // Describe = m_Id & " " & Utils.FormatMoney(m_Amount): its own module's members by name, others qualified.
        Assert.Equal(["m_Amount", "m_Id", "Utils.FormatMoney"], await Entries(page, "uses"));
    }

    [Fact]
    public async Task Implements_and_Events_links_are_tagged()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("IOrderRepository.Save");
        await page.ClickAsync("#source-dependencies summary");

        // The two implementations, and OrderService.Process, which calls Save through the interface.
        Assert.Equal(["CsvOrderRepository.IOrderRepository_Save", "OrderService.Process", "SheetOrderRepository.IOrderRepository_Save"],
            await Entries(page, "usedBy"));
        Assert.Equal(2, await page.Locator("#source-dependencies [data-direction='usedBy'] .edge-tag.implements").CountAsync());
        Assert.Equal("implemented by", await page.InnerTextAsync("#source-dependencies [data-direction='usedBy'] .edge-tag >> nth=0"));

        // RaiseEvent Progress links Process to the event declared in its own module.
        await opened.SelectAsync("OrderService.Process");
        var progress = page.Locator("#source-dependencies [data-direction='uses'] li", new() { Has = page.Locator("a[data-select='OrderService.Progress']") });
        Assert.Equal("events", await progress.Locator(".edge-tag.events").InnerTextAsync());
        Assert.Equal("Progress", await progress.Locator("a").InnerTextAsync());
    }

    [Fact]
    public async Task Recursion_is_noted_rather_than_listed()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Main.Factorial");
        await page.ClickAsync("#source-dependencies summary");

        Assert.Contains("Calls itself", await page.InnerTextAsync("#source-dependencies"));
        Assert.DoesNotContain("Factorial", await Entries(page, "uses"));
        Assert.DoesNotContain("Factorial", await Entries(page, "usedBy"));
    }

    [Fact]
    public async Task Clicking_an_entry_selects_it_everywhere()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order");
        await page.ClickAsync("#source-dependencies summary");

        await page.ClickAsync("#source-dependencies a[data-select='Utils']");

        Assert.Equal("Utils", await page.EvaluateAsync<string?>("() => window.vbadoc.selection()"));
        Assert.Equal(1, await page.Locator("#graph-svg g.node.selected[data-id='Utils']").CountAsync());
        Assert.Contains("Utils", await page.InnerTextAsync("#source-header h2"));
        Assert.Equal(["Order"], await Entries(page, "usedBy")); // still open, now for Utils
    }

    [Fact]
    public async Task Whether_it_is_open_is_remembered()
    {
        await using var opened = await OpenedPage.OpenAsync(fixture, fixture.SampleEmbedded);
        var page = opened.Page;
        await opened.SelectAsync("Order");
        await page.ClickAsync("#source-dependencies summary");

        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => !!window.vbadoc");
        await opened.SelectAsync("Main");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('source-dependencies').open"));

        await page.ClickAsync("#source-dependencies summary");
        await opened.SelectAsync("Order");
        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('source-dependencies').open"));
    }
}
