using System.Text;
using CodeDocumenter.Shared;
using CodeDocumenter.Shared.Files;
using Serilog.Events;
using VbaDocumenter.Cli;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Cli.Logging;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Cli;

/// <summary><c>vbadoc import</c>: what a choice comes to, and writing it as a bundle.</summary>
public sealed class ImportCommandTests : IDisposable
{
  static readonly SyntheticModule[] Project =
  [
    new("ILogger", VbaModuleKind.Class, "Attribute VB_Name = \"ILogger\"\r\nOption Explicit\r\nPublic Sub Log(ByVal m As String)\r\nEnd Sub\r\n"),
    new("PathTools", VbaModuleKind.Standard, "Attribute VB_Name = \"PathTools\"\r\nOption Explicit\r\nPrivate m_log As ILogger\r\nPublic Sub Pick()\r\n    m_log.Log \"x\"\r\n    Sheet1.Activate\r\nEnd Sub\r\n"),
    new("Unrelated", VbaModuleKind.Standard, "Attribute VB_Name = \"Unrelated\"\r\nOption Explicit\r\n"),
    new("Sheet1", VbaModuleKind.Document, "Attribute VB_Name = \"Sheet1\"\r\nAttribute VB_Base = \"0{00020820-0000-0000-C000-000000000046}\"\r\nOption Explicit\r\n"),
  ];

  readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-import-{Guid.NewGuid():N}");
  readonly TestLog _log = new();
  readonly StringWriter _output = new();
  readonly StringWriter _error = new();
  IUserInteraction _interaction = new FakeUserInteraction(isInteractive: false);

  string Source => Path.Combine(_folder, "Tools.xlsm");
  string Bundle => Path.Combine(_folder, "Shared");

  public ImportCommandTests()
  {
    Directory.CreateDirectory(_folder);
    File.WriteAllBytes(Source, SyntheticWorkbook.Xlsm(Project));
  }

  public void Dispose()
  {
    _output.Dispose();
    _error.Dispose();
    Directory.Delete(_folder, recursive: true);
  }

  int Run(params string[] args) => CliRunner.Run(args, new AppServices
  {
    Output = _output,
    Error = _error,
    Interaction = _interaction,
    CreateLogger = verbosity => _log.CreateLogger(LoggingSetup.MinimumLevel(verbosity)),
  }, TestContext.Current.CancellationToken);

  string WriteManifest(string text)
  {
    var path = Path.Combine(_folder, "choice.json");
    File.WriteAllText(path, text);
    return path;
  }

  [Fact]
  public void Named_modules_say_what_they_come_to_and_nothing_is_written_without_a_bundle()
  {
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools"));

    var info = _log.Messages(LogEventLevel.Information);
    Assert.Contains("Chosen: PathTools", info);
    Assert.Contains("Brought along: ILogger (used by PathTools)", info);
    Assert.Contains(info, m => m.StartsWith("Nothing was written"));
    Assert.Contains(_log.Messages(LogEventLevel.Warning), m => m.StartsWith("To check in the target: Sheet1"));
    Assert.False(Directory.Exists(Bundle));
    Assert.Equal("", _output.ToString());
  }

  [Fact]
  public void A_manifest_gives_the_choice_and_a_different_workbook_is_mentioned_but_not_a_failure()
  {
    var manifest = WriteManifest("""{ "formatVersion": 1, "tool": "vbadoc", "workbook": "Other.xlsm", "items": ["ILogger"] }""");

    Assert.Equal(ExitCodes.Success, Run("import", Source, "--manifest", manifest));

    Assert.Contains(_log.Messages(LogEventLevel.Warning), m => m.Contains("Other.xlsm") && m.Contains("Tools.xlsm"));
    Assert.Contains("Chosen: ILogger", _log.Messages(LogEventLevel.Information));
  }

  [Theory]
  [InlineData]
  [InlineData("--items", "ILogger", "--manifest", "m.json")]
  public void It_takes_a_manifest_or_names_but_not_both_or_neither(params string[] choice)
  {
    Assert.Equal(ExitCodes.InvalidArguments, Run(["import", Source, .. choice]));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("--manifest") && m.Contains("--items"));
  }

  [Fact]
  public void A_module_it_doesnt_have_or_cant_take_stops_it_saying_why()
  {
    Assert.Equal(ExitCodes.InputError, Run("import", Source, "--items", "Nope", "--bundle", Bundle));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("no module") && m.Contains("Nope"));

    Assert.Equal(ExitCodes.InputError, Run("import", Source, "--items", "Sheet1", "--bundle", Bundle));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("Sheet1") && m.Contains("A document module belongs to its workbook"));
    Assert.False(Directory.Exists(Bundle));
  }

  [Fact]
  public void Another_tools_manifest_is_refused()
  {
    var manifest = WriteManifest("""{ "formatVersion": 1, "tool": "pqdoc", "items": [] }""");

    Assert.Equal(ExitCodes.InputError, Run("import", Source, "--manifest", manifest));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("pqdoc's choices"));
  }

  [Fact]
  public void A_bundle_holds_the_files_as_the_VBA_editor_imports_them_and_a_README_on_how()
  {
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools", "--bundle", Bundle));

    Assert.Equal([".gitattributes", "ILogger.cls", "PathTools.bas", "README.md"], Directory.GetFiles(Bundle).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    Assert.StartsWith("VERSION 1.0 CLASS\r\n", File.ReadAllText(Path.Combine(Bundle, "ILogger.cls")));

    var readme = File.ReadAllText(Path.Combine(Bundle, "README.md"));
    Assert.Contains("# Modules from Tools.xlsm", readme);
    Assert.Contains("| `PathTools.bas` | PathTools | Chosen |", readme);
    Assert.Contains("| `ILogger.cls` | ILogger | Used by PathTools |", readme);
    Assert.Contains("**Sheet1:** a sheet's code name", readme);
    Assert.DoesNotContain("Unrelated", readme);
  }

  [Fact]
  public void The_same_bundle_again_is_nothing_to_ask_about()
  {
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools", "--bundle", Bundle));
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools", "--bundle", Bundle));
  }

  [Fact]
  public void A_different_bundle_over_an_earlier_one_needs_saying_so_and_replaces_it()
  {
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools", "--bundle", Bundle));

    Assert.Equal(ExitCodes.OutputRefused, Run("import", Source, "--items", "ILogger", "--bundle", Bundle));
    Assert.True(File.Exists(Path.Combine(Bundle, "PathTools.bas")));

    _interaction = new FakeUserInteraction(isInteractive: true, OverwriteAnswer.Yes);
    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "ILogger", "--bundle", Bundle));
    Assert.False(File.Exists(Path.Combine(Bundle, "PathTools.bas")));

    Assert.Equal(ExitCodes.Success, Run("import", Source, "--items", "PathTools", "--bundle", Bundle, "--force"));
    Assert.True(File.Exists(Path.Combine(Bundle, "PathTools.bas")));
  }

  [Fact]
  public void A_folder_of_someones_own_files_is_left_alone()
  {
    Directory.CreateDirectory(Bundle);
    var mine = Path.Combine(Bundle, "Mine.bas");
    File.WriteAllText(mine, "mine", Encoding.ASCII);

    Assert.Equal(ExitCodes.OutputRefused, Run("import", Source, "--items", "ILogger", "--bundle", Bundle, "--force"));

    Assert.True(File.Exists(mine));
    Assert.False(File.Exists(Path.Combine(Bundle, "ILogger.cls")));
  }

  [Fact]
  public void The_documenting_command_still_works_beside_it()
  {
    Assert.Equal(ExitCodes.Success, Run(Source, "--output", Path.Combine(_folder, "out")));
    Assert.True(File.Exists(Path.Combine(_folder, "out", "Tools-vba.html")));
  }
}
