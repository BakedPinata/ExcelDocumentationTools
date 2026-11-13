using Microsoft.Playwright;
using VbaDocumenter.Core.Analysis;
using VbaModuleKind = VbaDocumenter.Core.Extraction.VbaModuleKind;
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

  public async ValueTask InitializeAsync()
  {
    int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
    if (exitCode != 0) throw new InvalidOperationException($"Installing Chromium for Playwright failed (exit code {exitCode}).");

    Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    Browser = await Playwright.Chromium.LaunchAsync();

    var sample = SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules());
    SampleEmbedded = Generate(sample, "SampleVba.xlsm", "sample-embedded", embedSource: true);
    SampleFolder = Generate(sample, "SampleVba.xlsm", "sample-folder", embedSource: false);
    RealFolder = Generate(File.ReadAllBytes(Fixtures.Workbook("57181.xlsm")), "57181.xlsm", "real-folder", embedSource: false);
    LargeEmbedded = Generate(SyntheticWorkbook.Xlsm(LargeProject(300)), "Large.xlsm", "large-embedded", embedSource: true);
    AwkwardEmbedded = Generate(SyntheticWorkbook.Xlsm(AwkwardProject()), "Awkward.xlsm", "awkward-embedded", embedSource: true);
    FolderedEmbedded = Generate(SyntheticWorkbook.Xlsm(FolderedProject()), "Foldered.xlsm", "foldered-embedded", embedSource: true);
    UserSizedEmbedded = Generate(SyntheticWorkbook.Xlsm(UserSizedProject()), "UserSized.xlsm", "user-sized-embedded", embedSource: true);
    PredeclaredEmbedded = Generate(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules().Append(PredeclaredSettings)), "Predeclared.xlsm",
        "predeclared-embedded", embedSource: true);
    ConditionsEmbedded = Generate(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules().Append(Conditions).Append(Described)), "Conditions.xlsm",
        "conditions-embedded", embedSource: true);
    ViewsBuiltIn = Generate(sample, "SampleVba.xlsm", "views-built-in", embedSource: true, viewsFile: BuiltInViewsFile);
    ReferencesEmbedded = Generate(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules(), references:
        [
          ("stdole", @"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation"),
          ("Scripting", @"*\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime"),
          ("ADODB", @"*\G{B691E011-1797-432E-907A-4D8C69339129}#6.1#0#C:\Program Files\Common Files\System\ado\msado15.dll#Microsoft ActiveX Data Objects 6.1 Library"),
        ]), "References.xlsm", "references-embedded", embedSource: true);
  }

  /// <summary>The sample with library references: one every project has, and two that aren't.</summary>
  public string ReferencesEmbedded { get; private set; } = "";

  /// <summary>
  /// A generated project the size and shape of the user's large one (step D1d of the feedback plan): 70 modules in
  /// folders, about 1,500 procedures, and about 320 module and 1,540 member dependencies, for checking the page stays
  /// quick to pan, hover and scroll.
  /// </summary>
  public string UserSizedEmbedded { get; private set; } = "";

  /// <summary>
  /// Modules M00..M69 in eight folders, every third a class, each with 22 procedures. Each procedure calls one in one of
  /// five other modules, so each module uses about five others.
  /// </summary>
  static List<SyntheticModule> UserSizedProject()
  {
    const int Count = 70, Procedures = 22;
    var modules = new List<SyntheticModule>();
    for (int i = 0; i < Count; i++)
    {
      var name = $"M{i:00}";
      int[] uses = [(i + 1) % Count, (i + 3) % Count, (i * 7 + 5) % Count, (i * 11 + 2) % Count, (i * 13 + 9) % Count];
      var code = new System.Text.StringBuilder($"Attribute VB_Name = \"{name}\"\r\n'@Folder(\"Area{i % 8}.Part{i % 3}\")\r\nOption Explicit\r\n");
      for (int p = 0; p < Procedures; p++)
      {
        var callee = uses[p % uses.Length] == i ? (i + 2) % Count : uses[p % uses.Length];
        code.Append($"Public Sub Procedure{p:00}(ByVal value As Long)\r\n")
            .Append($"    M{callee:00}.Procedure{(p * 3) % Procedures:00} value + 1\r\n")
            .Append("End Sub\r\n");
      }
      modules.Add(new SyntheticModule(name, i % 3 == 0 ? VbaModuleKind.Class : VbaModuleKind.Standard, code.ToString()));
    }
    return modules;
  }

  /// <summary>SampleVba with '@Folder comments on most modules, and a Settings module with an Enum.</summary>
  public string FolderedEmbedded { get; private set; } = "";

  /// <summary>SampleVba with a class that has a default instance (VB_PredeclaredId = True), used by its name.</summary>
  public string PredeclaredEmbedded { get; private set; } = "";

  /// <summary>
  /// The sample, made in a folder that holds a views file (step L4 of the generalisation plan), so its views are built
  /// into the page: "Orders", and "Old logging", which mentions a module the workbook doesn't have.
  /// </summary>
  public string ViewsBuiltIn { get; private set; } = "";

  /// <summary>The "Orders" view of <see cref="ViewsBuiltIn"/>, as the page keeps it, for a browser's own copy of it.</summary>
  public const string OrdersView = """
      { "name": "Orders", "description": "The classes that handle an order", "tab": "classes", "filter": ["Order", "OrderService"], "hidden": [], "show": { "off": [], "radios": {} } }
      """;

  const string BuiltInViewsFile = """
      {
        "formatVersion": 1,
        "tool": "vbadoc",
        "workbook": "SampleVba.xlsm",
        "views": [
      """ + OrdersView + """
          ,
          { "name": "Old logging", "filter": ["Logger", "Retired"], "hidden": [], "show": { "off": [], "radios": {} } }
        ]
      }
      """;

  /// <summary>
  /// The sample with a module whose blocks call things in their own statements (step L3 of the generalisation plan):
  /// an If with an Or condition and nothing called inside it, a loop's condition, and a long run of calls, for
  /// scrolling a sequence diagram.
  /// </summary>
  public string ConditionsEmbedded { get; private set; } = "";

  /// <summary>A module and members that say what they're for (step L5), in <see cref="ConditionsEmbedded"/>.</summary>
  static readonly SyntheticModule Described = new("Described", VbaModuleKind.Standard, string.Join("\r\n",
      "Attribute VB_Name = \"Described\"",
      "Option Explicit",
      "",
      "' Works out prices for the <shop>,",
      "' tax included.",
      "",
      "' Adds the tax to a net price.",
      "Public Function WithTax(ByVal net As Double) As Double",
      "Attribute WithTax.VB_Description = \"The price with tax, to two places.\"",
      "    WithTax = Round(net * 1.1, 2)",
      "End Function",
      "",
      "Public Function Plain(ByVal net As Double) As Double",
      "    Plain = net",
      "End Function",
      ""));

  static readonly SyntheticModule Conditions = new("Conditions", VbaModuleKind.Standard, string.Join("\r\n",
      "Attribute VB_Name = \"Conditions\"",
      "Option Explicit",
      "",
      "Public Sub CheckBoth()",
      "    Dim x As Long",
      "    If IsA(1) Or IsB(2) Then",
      "        x = 1",
      "    Else",
      "        DoB",
      "    End If",
      "    Do While IsA(x)",
      "        x = x - 1",
      "    Loop",
      "End Sub",
      "",
      "Public Sub ManyCalls()",
      string.Join("\r\n", Enumerable.Range(0, 40).Select(i => i % 2 == 0 ? "    DoB" : "    Logger.LogInfo \"step\"")),
      "End Sub",
      "",
      "Private Function IsA(ByVal n As Long) As Boolean",
      "    IsA = n > 0",
      "End Function",
      "",
      "Private Function IsB(ByVal n As Long) As Boolean",
      "    IsB = n > 1",
      "End Function",
      "",
      "Private Sub DoB()",
      "End Sub",
      ""));

  static readonly SyntheticModule PredeclaredSettings = new("Settings", VbaModuleKind.Class, string.Join("\r\n",
      "Attribute VB_Name = \"Settings\"",
      "Attribute VB_PredeclaredId = True",
      "Option Explicit",
      "Public Function Rate() As Double",
      "    Rate = 0.1",
      "End Function",
      ""));

  static readonly Dictionary<string, string> Folders = new()
  {
    ["AppInit"] = "Startup",
    ["Main"] = "Startup",
    ["Settings"] = "Startup",
    ["Order"] = "Orders.Model",
    ["OrderService"] = "Orders.Model",
    ["IOrderRepository"] = "Orders.Model",
    ["CsvOrderRepository"] = "Orders.Storage",
    ["SheetOrderRepository"] = "Orders.Storage",
  };

  static List<SyntheticModule> FolderedProject()
  {
    var settings = new SyntheticModule("Settings", VbaModuleKind.Standard, string.Join("\r\n",
        "Attribute VB_Name = \"Settings\"",
        "Option Explicit",
        "Public Enum Level",
        "    Low",
        "    High",
        "End Enum",
        "Public Function Current() As Level",
        "    Current = High",
        "End Function",
        ""));

    // The annotation goes straight after the VB_Name attribute, where Rubberduck puts it. The sample files' line endings
    // vary, so the new line uses whichever the attribute's line has.
    return [.. Fixtures.SampleVbaModules().Append(settings).Select(m => Folders.TryGetValue(m.Name, out var folder)
        ? m with
        {
          Source = System.Text.RegularExpressions.Regex.Replace(
              m.Source, $"(Attribute VB_Name = \"{m.Name}\"(\r?\n))", $"$1'@Folder(\"{folder}\")$2", System.Text.RegularExpressions.RegexOptions.None),
        }
        : m)];
  }

  /// <summary>A generated project of 300 modules and about 2,700 members, to check the views stay usable.</summary>
  public string LargeEmbedded { get; private set; } = "";

  /// <summary>
  /// Names and values that would mean something to a diagram language or to markdown: braces, brackets, entity
  /// codes, emphasis, generics, HTML, a lone ")", and non-ASCII names. They must all be shown exactly as written.
  /// </summary>
  public string AwkwardEmbedded { get; private set; } = "";

  public const string AwkwardModule = "Données_Ü";

  static List<SyntheticModule> AwkwardProject() =>
  [
      new(AwkwardModule, VbaModuleKind.Standard, string.Join("\r\n",
            $"Attribute VB_Name = \"{AwkwardModule}\"",
            "Option Explicit",
            "Public Const BRACES As String = \"{a} }\"",
            "Public Const PAREN As String = \"a)b\"",
            "Public Const MARKDOWN As String = \"**x** __y__ [l](u) `c`\"",
            "Public Const ENTITY As String = \"#59; &amp; <b>bold</b> ~T~\"",
            "Public Const DOLLAR As String = \"cost$\"",
            "Public Function Größe$(Optional ByVal s As String = \"(\", Optional n As Long = 5)",
            "    Größe = s",
            "End Function",
            "")),
    ];

  /// <summary>
  /// Modules Mod000..ModNNN, every third one a class, each with a public variable and eight procedures that
  /// call procedures in two other modules.
  /// </summary>
  static List<SyntheticModule> LargeProject(int count)
  {
    var modules = new List<SyntheticModule>();
    for (int i = 0; i < count; i++)
    {
      var name = $"Mod{i:000}";
      var code = new System.Text.StringBuilder($"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\nPublic Counter As Long\r\n");
      for (int p = 0; p < 8; p++)
      {
        code.Append($"Public Sub P{p}()\r\n")
            .Append($"    Mod{(i + 1) % count:000}.P{p}\r\n")
            .Append($"    Mod{(i * 7 + 3) % count:000}.P{(p + 1) % 8}\r\n")
            .Append("    Counter = Counter + 1\r\n")
            .Append("End Sub\r\n");
      }
      modules.Add(new SyntheticModule(name, i % 3 == 0 ? VbaModuleKind.Class : VbaModuleKind.Standard, code.ToString()));
    }
    return modules;
  }

  public async ValueTask DisposeAsync()
  {
    await Browser.DisposeAsync();
    Playwright.Dispose();
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  /// <summary>Runs the same steps as the vbadoc console app and returns the output folder.</summary>
  string Generate(byte[] workbook, string title, string folderName, bool embedSource, string? viewsFile = null)
  {
    var folder = Path.Combine(_root, folderName);
    var project = WorkbookReader.Read(workbook);
    var model = DependencyAnalyser.Analyse(ProjectParser.Parse(project));
    ProjectExporter.Export(project, folder);
    // A views file in the output folder is built into the page, as the tool does.
    if (viewsFile != null) File.WriteAllText(Path.Combine(folder, "vbadoc-views.json"), viewsFile);
    var views = HtmlPageWriter.ReadViews(folder, title);
    HtmlPageWriter.Write(folder, HtmlPageWriter.FileName(HtmlPageWriter.DefaultName(title)), HtmlPageWriter.Build(model, project, title, embedSource, views.Json));
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

  /// <summary>The address of the one page in an output folder.</summary>
  public static string PageUrl(string folder) => new Uri(Directory.GetFiles(folder, "*.html").Single()).AbsoluteUri;
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
  /// <summary>
  /// An init script that hides the explorer, as if it had been closed before, for tests written for the graph's or
  /// class diagram's geometry without it.
  /// </summary>
  public const string WithoutExplorer = "localStorage.setItem('vbadoc-explorer-hidden', 'true');";

  /// <summary>
  /// An init script that locks the source panel open, as if it had been locked before, for tests of its size and of
  /// views laid out beside it, which was always open before it followed the selection. It runs on every load, so it
  /// leaves a lock the test has changed alone.
  /// </summary>
  /// <summary>
  /// An init script that turns Focus on click off, as if it had been turned off before, for tests of what a selection
  /// lights up in the whole graph. It runs on every load, so it leaves a setting the test has changed alone.
  /// </summary>
  public const string WithoutFocusOnClick = "if (localStorage.getItem('vbadoc-focus-on-click') === null) localStorage.setItem('vbadoc-focus-on-click', 'false');";

  public const string WithPanelLocked = "if (localStorage.getItem('vbadoc-source-locked') === null) localStorage.setItem('vbadoc-source-locked', 'true');";

  public static async Task<OpenedPage> OpenAsync(PageFixture fixture, string folder, ColorScheme colourScheme = ColorScheme.Dark,
      int width = 1280, int height = 800, string? initScript = null)
  {
    var context = await fixture.Browser.NewContextAsync(new()
    {
      ColorScheme = colourScheme,
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
      (await Page.Locator("#side-panel-body .line .ln").AllInnerTextsAsync()).Select(int.Parse).ToArray();

  public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
