using System.Text;
using Microsoft.Playwright;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.BrowserTests;

/// <summary>
/// Shared by all browser tests: installs Playwright's Chromium on first use (a one-off download), starts it
/// headless, and generates the pages under test the same way the tool does.
/// </summary>
public sealed class PageFixture : IAsyncLifetime
{
  private readonly string root = Path.Combine(Path.GetTempPath(), $"pqdoc-browser-{Guid.NewGuid():N}");

  /// <summary>A query named after markup, to check a name is only ever shown as text.</summary>
  public const string HostileQuery = "</script><img src=x onerror=alert(1)>";

  /// <summary>A query whose name is too long for its label in the graph.</summary>
  public const string LongQuery = "A query name much longer than the graph has room for";

  public IPlaywright Playwright { get; private set; } = null!;

  public IBrowser Browser { get; private set; } = null!;

  /// <summary>The sample queries: a cycle, an isolated query and awkward names.</summary>
  public string SamplePage { get; private set; } = string.Empty;

  /// <summary>Names that mean something to HTML, a selector or the page's own code.</summary>
  public string AwkwardPage { get; private set; } = string.Empty;

  /// <summary>A generated model of 300 queries, to check the view stays usable.</summary>
  public string LargePage { get; private set; } = string.Empty;

  public async ValueTask InitializeAsync()
  {
    var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
    if (exitCode != 0)
    {
      throw new InvalidOperationException($"Installing Chromium for Playwright failed (exit code {exitCode}).");
    }

    Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    Browser = await Playwright.Chromium.LaunchAsync();

    Directory.CreateDirectory(root);
    SamplePage = Generate(await Fixtures.GraphAsync(), "sales-model");
    AwkwardPage = Generate(AwkwardGraph(), "R&D <draft>");
    LargePage = Generate(LargeGraph(300), "large-model");
  }

  public async ValueTask DisposeAsync()
  {
    await Browser.DisposeAsync();
    Playwright.Dispose();
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static DependencyGraph AwkwardGraph()
  {
    string[] names = [HostileQuery, "It's \"quoted\"", "Région & Co", "A", "B>C", "B", "C>A", "__proto__", LongQuery];
    return DependencyGraph.Create(
      names,
      [
        new DependencyEdge(HostileQuery, "It's \"quoted\""),
        new DependencyEdge("Région & Co", "It's \"quoted\""),
        // Joined with ">", each of these reads as the reverse of the other: "A>B>C" and "B>C>A" backwards.
        new DependencyEdge("A", "B>C"),
        new DependencyEdge("B", "C>A"),
        new DependencyEdge("__proto__", LongQuery),
      ]);
  }

  /// <summary>Queries Q000..QNNN in chains of ten, each also feeding a query in the next chain along.</summary>
  private static DependencyGraph LargeGraph(int count)
  {
    var names = Enumerable.Range(0, count).Select(i => $"Q{i:000}").ToArray();
    var edges = new List<DependencyEdge>();
    for (var i = 0; i < count; i++)
    {
      if (i % 10 != 9)
      {
        edges.Add(new DependencyEdge(names[i], names[i + 1]));
      }

      if (i + 11 < count && i % 10 != 9)
      {
        edges.Add(new DependencyEdge(names[i], names[i + 11]));
      }
    }

    return DependencyGraph.Create(names, edges);
  }

  /// <summary>Writes the page as the HTML exporter does and returns its path.</summary>
  private string Generate(DependencyGraph graph, string outputName)
  {
    var context = new ExportContext(outputName, new DateTimeOffset(2026, 10, 2, 10, 3, 0, TimeSpan.FromHours(8)));
    var path = Path.Combine(root, $"{Guid.NewGuid():N}.html");
    File.WriteAllText(path, HtmlExporter.Render(graph, context), new UTF8Encoding(false));
    return path;
  }
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

  /// <param name="fixture">The shared browser.</param>
  /// <param name="path">The page to open.</param>
  /// <param name="colourScheme">The system theme the browser reports.</param>
  /// <param name="width">The window width.</param>
  /// <param name="height">The window height.</param>
  /// <param name="initScript">JavaScript run before the page's own scripts, e.g. to simulate an older browser.</param>
  public static async Task<OpenedPage> OpenAsync(
    PageFixture fixture,
    string path,
    ColorScheme colourScheme = ColorScheme.Dark,
    int width = 1280,
    int height = 800,
    string? initScript = null)
  {
    var context = await fixture.Browser.NewContextAsync(new()
    {
      ColorScheme = colourScheme,
      ViewportSize = new() { Width = width, Height = height },
    });
    if (initScript != null)
    {
      await context.AddInitScriptAsync(initScript);
    }

    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var requests = new List<string>();
    page.Console += (_, message) =>
    {
      if (message.Type == "error")
      {
        errors.Add(message.Text);
      }
    };
    page.PageError += (_, error) => errors.Add(error);
    await page.RouteAsync("**/*", async route =>
    {
      if (route.Request.Url.StartsWith("file:", StringComparison.Ordinal))
      {
        await route.ContinueAsync();
        return;
      }

      requests.Add(route.Request.Url);
      await route.AbortAsync();
    });
    await page.GotoAsync(new Uri(path).AbsoluteUri);
    await page.WaitForFunctionAsync("() => !!window.pqdocGraph");
    return new OpenedPage(context, page, errors, requests);
  }

  public Task SelectAsync(string? id) => Page.EvaluateAsync("id => window.pqdoc.select(id)", id);

  public Task<string?> SelectionAsync() => Page.EvaluateAsync<string?>("() => window.pqdoc.selection()");

  /// <summary>A selector for the node of a query, whatever its name contains.</summary>
  public async Task<ILocator> NodeAsync(string id)
  {
    var escaped = await Page.EvaluateAsync<string>("id => CSS.escape(id)", id);
    return Page.Locator($"#graph-svg g.node[data-id='{escaped}']");
  }

  /// <summary>The ids of the drawn queries that match a selector, in name order.</summary>
  public async Task<string[]> DrawnIdsAsync(string selector) =>
    (await Page.Locator(selector).EvaluateAllAsync<string[]>("ns => ns.map(n => n.dataset.id)"))
      .Order(StringComparer.OrdinalIgnoreCase)
      .ToArray();

  public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
