using Microsoft.Playwright;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.BrowserTests;

/// <summary>
/// Shared by all browser tests: installs Playwright's Chromium on first use (a one-off download), starts it
/// headless, and generates the pages under test the same way the tool does.
/// </summary>
public sealed class PageFixture : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"vbadoc-browser-{Guid.NewGuid():N}");

    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>SampleVba with the source embedded in the page.</summary>
    public string SampleEmbedded { get; private set; } = "";

    /// <summary>SampleVba in "Load folder" mode: the folder holds the page and the exported files.</summary>
    public string SampleFolder { get; private set; } = "";

    /// <summary>A real workbook in code page 1252 with non-ASCII source, in "Load folder" mode.</summary>
    public string RealFolder { get; private set; } = "";

    public async Task InitializeAsync()
    {
        int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0) throw new InvalidOperationException($"Installing Chromium for Playwright failed (exit code {exitCode}).");

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync();

        var sample = SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules());
        SampleEmbedded = Generate(sample, "SampleVba.xlsm", "sample-embedded", embedSource: true);
        SampleFolder = Generate(sample, "SampleVba.xlsm", "sample-folder", embedSource: false);
        RealFolder = Generate(File.ReadAllBytes(Fixtures.Workbook("57181.xlsm")), "57181.xlsm", "real-folder", embedSource: false);
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Runs the same steps as the vbadoc console app and returns the output folder.</summary>
    string Generate(byte[] workbook, string title, string folderName, bool embedSource)
    {
        var folder = Path.Combine(_root, folderName);
        var project = WorkbookReader.Read(workbook);
        var model = DependencyAnalyzer.Analyze(ProjectParser.Parse(project));
        ProjectExporter.Export(project, folder);
        HtmlPageWriter.Write(folder, HtmlPageWriter.Build(model, project, title, embedSource));
        return folder;
    }

    /// <summary>A copy of an output folder, for tests that change files.</summary>
    public string CopyOf(string folder)
    {
        var copy = Path.Combine(_root, $"copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.GetFiles(folder)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        return copy;
    }

    public static string PageUrl(string folder) => new Uri(Path.Combine(folder, HtmlPageWriter.FileName)).AbsoluteUri;
}

[CollectionDefinition("Browser")]
public sealed class BrowserCollection : ICollectionFixture<PageFixture>;

/// <summary>An open page plus everything it did that a test may want to check.</summary>
public sealed class OpenedPage(IBrowserContext context, IPage page, List<string> errors, List<string> networkRequests) : IAsyncDisposable
{
    public IBrowserContext Context { get; } = context;
    public IPage Page { get; } = page;

    /// <summary>Console errors and uncaught exceptions.</summary>
    public List<string> Errors { get; } = errors;

    /// <summary>Requests for anything other than the local page itself. These are blocked.</summary>
    public List<string> NetworkRequests { get; } = networkRequests;

    /// <param name="initScript">JavaScript run before the page's own scripts, e.g. to simulate an older browser.</param>
    public static async Task<OpenedPage> OpenAsync(PageFixture fixture, string folder, ColorScheme colorScheme = ColorScheme.Dark,
        int width = 1280, int height = 800, string? initScript = null)
    {
        var context = await fixture.Browser.NewContextAsync(new()
        {
            ColorScheme = colorScheme,
            ViewportSize = new() { Width = width, Height = height },
        });
        if (initScript != null) await context.AddInitScriptAsync(initScript);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var requests = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url.StartsWith("file:", StringComparison.Ordinal)) { await route.ContinueAsync(); return; }
            requests.Add(route.Request.Url);
            await route.AbortAsync();
        });
        await page.GotoAsync(PageFixture.PageUrl(folder));
        await page.WaitForFunctionAsync("() => !!window.vbadoc");
        return new OpenedPage(context, page, errors, requests);
    }

    public Task SelectAsync(string id) => Page.EvaluateAsync("id => window.vbadoc.select(id)", id);

    /// <summary>Line numbers shown in the source panel.</summary>
    public async Task<int[]> ShownLinesAsync() =>
        (await Page.Locator("#source-body .line .ln").AllInnerTextsAsync()).Select(int.Parse).ToArray();

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
