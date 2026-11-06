using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;
using ExcelDocumenter.Shared.Workbooks;
using Serilog.Events;
using VbaDocumenter.Cli;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Logging;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Cli;

/// <summary><c>vbadoc import</c> writing into a workbook: a new one, a copy of a target, or the target itself.</summary>
public sealed class ImportWorkbookTests : IDisposable
{
  const string Scripting = @"*\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime";
  const string NotInstalled = @"*\G{11111111-2222-3333-4444-555555555555}#1.0#0#C:\nope.dll#Not Installed";

  static readonly SyntheticModule[] Project =
  [
    new("ILogger", VbaModuleKind.Class, "Attribute VB_Name = \"ILogger\"\r\nOption Explicit\r\nPublic Sub Log(ByVal m As String)\r\nEnd Sub\r\n"),
    new("Paths", VbaModuleKind.Standard, "Attribute VB_Name = \"Paths\"\r\nOption Explicit\r\nPrivate m_log As ILogger\r\nPublic Sub Pick()\r\n    m_log.Log \"x\"\r\nEnd Sub\r\n"),
    new("Unrelated", VbaModuleKind.Standard, "Attribute VB_Name = \"Unrelated\"\r\nOption Explicit\r\n"),
  ];

  static readonly SyntheticModule TheirLogger = new("ILogger", VbaModuleKind.Class, "Attribute VB_Name = \"ILogger\"\r\nOption Explicit\r\n' their own\r\n");
  static readonly SyntheticModule TheirMain = new("Main", VbaModuleKind.Standard, "Attribute VB_Name = \"Main\"\r\nOption Explicit\r\n");

  readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-write-{Guid.NewGuid():N}");
  readonly TestLog _log = new();
  readonly StringWriter _output = new();
  readonly StringWriter _error = new();
  FakeUserInteraction _interaction = new(isInteractive: false);
  Func<VbaReference, bool?> _isInstalled = r => !r.Name.Equals("Missing", StringComparison.Ordinal);

  string Source => Path.Combine(_folder, "Tools.xlsm");
  string Target => Path.Combine(_folder, "Target.xlsm");
  string Copy => Path.Combine(_folder, "Target-imported.xlsm");
  string New => Path.Combine(_folder, "Toolkit.xlsm");

  public ImportWorkbookTests()
  {
    Directory.CreateDirectory(_folder);
    File.WriteAllBytes(Source, SyntheticWorkbook.Xlsm(Project, references: [("Scripting", Scripting), ("Missing", NotInstalled)]));
  }

  public void Dispose()
  {
    _output.Dispose();
    _error.Dispose();
    Directory.Delete(_folder, recursive: true);
  }

  int Run(params string[] args) => CliRunner.Run(["import", Source, .. args], new AppServices
  {
    Output = _output,
    Error = _error,
    Interaction = _interaction,
    IsLibraryInstalled = _isInstalled,
    CreateLogger = verbosity => _log.CreateLogger(LoggingSetup.MinimumLevel(verbosity)),
  }, TestContext.Current.CancellationToken);

  void WriteTarget(params SyntheticModule[] modules) => File.WriteAllBytes(Target, SyntheticWorkbook.Package(modules));

  void Answer(params ClashAnswer[] answers)
  {
    _interaction = new FakeUserInteraction(isInteractive: true);
    foreach (var answer in answers) _interaction.ClashAnswers.Enqueue(answer);
  }

  void Confirm(bool answer)
  {
    _interaction = new FakeUserInteraction(isInteractive: true);
    _interaction.Confirmations.Enqueue(answer);
  }

  static VbaProject Read(string path) => WorkbookReader.Read(path);

  static IEnumerable<string> Names(string path) => Read(path).Modules.Select(m => m.Name);

  string SourceOf(string path, string module) => Read(path).Modules.Single(m => m.Name == module).Source;

  [Fact]
  public void A_new_workbook_holds_what_was_chosen_and_what_it_uses_and_no_more()
  {
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--new", New));

    Assert.Equal(["ILogger", "Paths", "Sheet1", "ThisWorkbook"], Names(New));
    Assert.Equal(Project[1].Source, SourceOf(New, "Paths"));
    Assert.Equal(VbaModuleKind.Class, Read(New).Modules.Single(m => m.Name == "ILogger").Kind);
    Assert.Contains(New, _log.Messages(LogEventLevel.Information).Select(m => m.Replace("Wrote ", "")));
  }

  [Fact]
  public void A_new_workbook_gets_the_references_except_any_that_cant_be_found()
  {
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--new", New));

    Assert.Equal(["stdole", "Office", "Scripting"], Read(New).References.Select(r => r.Name));
    Assert.Contains(_log.Messages(LogEventLevel.Warning), m => m.StartsWith("Left out, as it can't be found") && m.Contains("Missing"));
  }

  [Fact]
  public void No_references_leaves_them_all_out()
  {
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--new", New, "--no-references"));

    Assert.Equal(["stdole", "Office"], Read(New).References.Select(r => r.Name));
    Assert.DoesNotContain(_log.Messages(LogEventLevel.Warning), m => m.Contains("Left out"));
  }

  [Fact]
  public void A_manifest_says_which_references_to_leave_out()
  {
    var manifest = Path.Combine(_folder, "choice.json");
    File.WriteAllText(manifest, """{ "formatVersion": 1, "tool": "vbadoc", "items": ["Paths"], "leaveOutReferences": ["Scripting"] }""");

    Assert.Equal(ExitCodes.Success, Run("--manifest", manifest, "--new", New));

    Assert.DoesNotContain("Scripting", Read(New).References.Select(r => r.Name));
  }

  [Theory]
  [InlineData("Toolkit.xlsx")]
  [InlineData("Toolkit.xlsb")]
  [InlineData("Toolkit")]
  public void A_new_workbook_has_to_be_able_to_hold_modules(string name)
  {
    Assert.Equal(ExitCodes.InvalidArguments, Run("--items", "Paths", "--new", Path.Combine(_folder, name)));

    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("ending in .xlsm"));
    Assert.False(File.Exists(Path.Combine(_folder, name)));
  }

  [Theory]
  [InlineData("--new")]
  [InlineData("--into")]
  public void The_source_is_never_written_over(string option)
  {
    var before = File.ReadAllBytes(Source);

    int exit = option == "--new"
      ? Run("--items", "Paths", "--new", Source, "--force")
      : Run("--items", "Paths", "--into", Source, "--in-place", "--force");

    Assert.Equal(ExitCodes.InvalidArguments, exit);
    Assert.Equal(before, File.ReadAllBytes(Source));
  }

  [Fact]
  public void A_copy_goes_beside_the_target_which_like_the_source_is_left_byte_for_byte_as_it_was()
  {
    WriteTarget(TheirMain);
    var source = File.ReadAllBytes(Source);
    var target = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Equal(["ILogger", "Main", "Paths"], Names(Copy));
    Assert.Equal(source, File.ReadAllBytes(Source));
    Assert.Equal(target, File.ReadAllBytes(Target));
  }

  [Fact]
  public void A_real_workbook_can_be_the_target()
  {
    File.Copy(Fixtures.Workbook("SimpleMacro.xlsm"), Target);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Contains("Paths", Names(Copy));
    Assert.Contains("Module1", Names(Copy));
  }

  [Fact]
  public void An_xlsx_is_written_as_an_xlsm_since_it_cant_hold_modules()
  {
    var target = Path.Combine(_folder, "Target.xlsx");
    File.WriteAllBytes(target, BlankWorkbook.Build(macroEnabled: false).Write());

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", target));

    Assert.Contains("Paths", Names(Copy));
    Assert.False(File.Exists(Path.Combine(_folder, "Target-imported.xlsx")));
  }

  [Fact]
  public void An_xlsb_stays_an_xlsb()
  {
    var target = Path.Combine(_folder, "Target.xlsb");
    File.WriteAllBytes(target, SyntheticWorkbook.Binary());

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", target));

    Assert.Contains("Paths", Names(Path.Combine(_folder, "Target-imported.xlsb")));
  }

  [Fact]
  public void An_earlier_copy_is_covered_by_on_exists_and_force()
  {
    WriteTarget(TheirMain);
    File.WriteAllBytes(Copy, [1, 2, 3]);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target));
    Assert.Equal([1, 2, 3], File.ReadAllBytes(Copy));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--on-exists", "skip"));
    Assert.Equal([1, 2, 3], File.ReadAllBytes(Copy));

    _interaction = new FakeUserInteraction(isInteractive: true, OverwriteAnswer.Yes);
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));
    Assert.Contains("Paths", Names(Copy));

    Assert.Equal(ExitCodes.Success, Run("--items", "ILogger", "--into", Target, "--force"));
    Assert.DoesNotContain("Paths", Names(Copy));
  }

  [Fact]
  public void A_reference_the_target_has_isnt_added_twice()
  {
    File.WriteAllBytes(Target, SyntheticWorkbook.Package([TheirMain], references: [("Scripting", Scripting)]));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Equal(["Scripting"], Read(Copy).References.Select(r => r.Name));
  }

  [Fact]
  public void An_encrypted_target_is_refused_saying_why()
  {
    var target = Path.Combine(_folder, "Secret.xlsm");
    File.WriteAllBytes(target, SyntheticWorkbook.Encrypted());

    Assert.Equal(ExitCodes.InputError, Run("--items", "Paths", "--into", target));

    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("encrypted"));
    Assert.False(File.Exists(Path.Combine(_folder, "Secret-imported.xlsm")));
  }

  [Fact]
  public void A_signed_target_is_refused_unless_told_to_go_ahead()
  {
    File.WriteAllBytes(Target, SyntheticWorkbook.Package([TheirMain], change: package => package.Set("xl/vbaProjectSignature.bin", [9])));

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target));
    Assert.False(File.Exists(Copy));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("signed") && m.Contains("--drop-signature"));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--drop-signature"));
    Assert.False(OpcPackage.Read(File.ReadAllBytes(Copy)).Has("xl/vbaProjectSignature.bin"));
  }

  [Fact]
  public void Drop_signature_needs_a_target()
  {
    Assert.Equal(ExitCodes.InvalidArguments, Run("--items", "Paths", "--new", New, "--drop-signature"));
  }

  [Fact]
  public void An_identical_module_is_left_alone_without_asking_and_counted()
  {
    WriteTarget(Project[0], TheirMain);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Contains("Already there, identical: ILogger", _log.Messages(LogEventLevel.Information));
    Assert.Contains("Adding: Paths", _log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public void When_everything_is_there_already_nothing_is_written()
  {
    File.WriteAllBytes(Target, SyntheticWorkbook.Package(Project, references: [("Scripting", Scripting)]));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.False(File.Exists(Copy));
    Assert.Contains("The target needs nothing from this, so nothing was written", _log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public void A_clash_with_nobody_to_ask_stops_with_nothing_written()
  {
    WriteTarget(TheirLogger);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target));

    Assert.False(File.Exists(Copy));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("--on-clash"));
  }

  [Fact]
  public void Force_doesnt_answer_a_clash()
  {
    WriteTarget(TheirLogger);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target, "--force"));

    Assert.False(File.Exists(Copy));
  }

  [Fact]
  public void Skip_keeps_the_targets_version_and_says_so()
  {
    WriteTarget(TheirLogger);
    Answer(ClashAnswer.Skip);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Equal(TheirLogger.Source, SourceOf(Copy, "ILogger"));
    Assert.Contains("Paths", Names(Copy));
    Assert.Contains("Skipped, keeping the target's: ILogger", _log.Messages(LogEventLevel.Warning));
    Assert.Equal("Keeps the target's version, which may differ, for Paths to use.", Assert.Single(_interaction.ClashesAsked).IfSkipped);
  }

  [Fact]
  public void Overwrite_replaces_the_targets_version()
  {
    WriteTarget(TheirLogger);
    Answer(ClashAnswer.Overwrite);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Equal(Project[0].Source, SourceOf(Copy, "ILogger"));
    Assert.Contains("Overwriting: ILogger", _log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public void Skip_All_and_Overwrite_All_answer_for_the_rest()
  {
    var theirPaths = Project[1] with { Source = "Attribute VB_Name = \"Paths\"\r\nOption Explicit\r\n' theirs\r\n" };
    WriteTarget(TheirLogger, theirPaths);

    Answer(ClashAnswer.OverwriteAll);
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));
    Assert.Single(_interaction.ClashesAsked);
    Assert.Equal(Project[0].Source, SourceOf(Copy, "ILogger"));
    Assert.Equal(Project[1].Source, SourceOf(Copy, "Paths"));

    File.Delete(Copy);
    Answer(ClashAnswer.SkipAll);
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--no-references"));
    Assert.Single(_interaction.ClashesAsked);
    Assert.False(File.Exists(Copy), "Nothing was left to add");
  }

  [Fact]
  public void A_reference_the_target_lacks_is_still_added_when_its_modules_are_all_there()
  {
    WriteTarget(Project);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target));

    Assert.Equal(["Scripting"], Read(Copy).References.Select(r => r.Name));
    Assert.DoesNotContain(_log.Messages(LogEventLevel.Information), m => m.StartsWith("Adding: "));
  }

  [Fact]
  public void Quit_stops_with_nothing_written_though_earlier_answers_were_given()
  {
    var theirPaths = Project[1] with { Source = "Attribute VB_Name = \"Paths\"\r\nOption Explicit\r\n' theirs\r\n" };
    WriteTarget(TheirLogger, theirPaths);
    Answer(ClashAnswer.Overwrite, ClashAnswer.Quit);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target));

    Assert.False(File.Exists(Copy));
  }

  [Theory]
  [InlineData("overwrite")]
  [InlineData("skip")]
  public void On_clash_answers_for_an_unattended_run(string behaviour)
  {
    WriteTarget(TheirLogger);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--on-clash", behaviour));

    Assert.Equal(behaviour == "overwrite" ? Project[0].Source : TheirLogger.Source, SourceOf(Copy, "ILogger"));
  }

  [Fact]
  public void A_different_kind_of_module_can_be_overwritten()
  {
    WriteTarget(new SyntheticModule("ILogger", VbaModuleKind.Standard, "Attribute VB_Name = \"ILogger\"\r\n"));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--on-clash", "overwrite"));

    Assert.Equal(VbaModuleKind.Class, Read(Copy).Modules.Single(m => m.Name == "ILogger").Kind);
  }

  [Fact]
  public void A_name_taken_by_a_sheet_stops_on_clash_overwrite_but_can_be_skipped()
  {
    WriteTarget(new SyntheticModule("ILogger", VbaModuleKind.Document, "Attribute VB_Name = \"ILogger\"\r\n"));

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target, "--on-clash", "overwrite"));
    Assert.False(File.Exists(Copy));

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--on-clash", "skip"));
    Assert.Equal(VbaModuleKind.Document, Read(Copy).Modules.Single(m => m.Name == "ILogger").Kind);
  }

  [Fact]
  public void In_place_changes_the_target_once_answered_yes_saying_what_will_change()
  {
    WriteTarget(TheirMain);
    Confirm(true);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--in-place"));

    Assert.Equal(["ILogger", "Main", "Paths"], Names(Target));
    Assert.False(File.Exists(Copy));
    Assert.Equal("This changes Target.xlsm itself: 2 modules added, 1 library reference added. It can't be undone. Are you sure?", Assert.Single(_interaction.Questions));
  }

  [Fact]
  public void In_place_answered_no_leaves_the_target_as_it_was()
  {
    WriteTarget(TheirMain);
    var before = File.ReadAllBytes(Target);
    Confirm(false);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target, "--in-place"));

    Assert.Equal(before, File.ReadAllBytes(Target));
  }

  [Fact]
  public void In_place_with_force_needs_nobody_to_ask()
  {
    WriteTarget(TheirMain);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--in-place", "--force"));

    Assert.Equal(["ILogger", "Main", "Paths"], Names(Target));
    Assert.Empty(_interaction.Questions);
  }

  [Fact]
  public void In_place_with_nobody_to_ask_and_no_force_stops()
  {
    WriteTarget(TheirMain);
    var before = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target, "--in-place"));

    Assert.Equal(before, File.ReadAllBytes(Target));
  }

  [Fact]
  public void In_place_with_force_still_stops_at_a_clash_nobody_answered()
  {
    WriteTarget(TheirLogger);
    var before = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", Target, "--in-place", "--force"));

    Assert.Equal(before, File.ReadAllBytes(Target));
  }

  [Fact]
  public void In_place_with_force_and_on_clash_is_a_whole_unattended_run()
  {
    WriteTarget(TheirLogger);

    Assert.Equal(ExitCodes.Success, Run("--items", "Paths", "--into", Target, "--in-place", "--force", "--on-clash", "overwrite"));

    Assert.Equal(Project[0].Source, SourceOf(Target, "ILogger"));
  }

  [Fact]
  public void An_xlsx_cant_gain_modules_in_place()
  {
    var target = Path.Combine(_folder, "Target.xlsx");
    var before = BlankWorkbook.Build(macroEnabled: false).Write();
    File.WriteAllBytes(target, before);

    Assert.Equal(ExitCodes.OutputRefused, Run("--items", "Paths", "--into", target, "--in-place", "--force"));

    Assert.Equal(before, File.ReadAllBytes(target));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("An .xlsx can't hold modules"));
  }

  [Fact]
  public void A_target_open_in_Excel_is_left_as_it_was()
  {
    WriteTarget(TheirMain);
    var before = File.ReadAllBytes(Target);

    using (new FileStream(Target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
    {
      Assert.Equal(ExitCodes.ExportFailed, Run("--items", "Paths", "--into", Target, "--in-place", "--force"));
    }

    Assert.Equal(before, File.ReadAllBytes(Target));
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("Is it open in Excel?"));
  }

  [Fact]
  public void What_cant_be_made_leaves_the_target_as_it_was()
  {
    // The target's code page can't hold what's coming, which only comes out as the workbook is made.
    File.WriteAllBytes(Source, SyntheticWorkbook.Xlsm([new("Wide", VbaModuleKind.Standard, "Attribute VB_Name = \"Wide\"\r\n' 中文\r\n")], codePage: 950));
    File.WriteAllBytes(Target, SyntheticWorkbook.Package([TheirMain], codePage: 1252));
    var before = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.InputError, Run("--items", "Wide", "--into", Target, "--in-place", "--force"));

    Assert.Equal(before, File.ReadAllBytes(Target));
    Assert.Equal(["Target.xlsm", "Tools.xlsm"], Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
  }

  [Theory]
  [InlineData("--on-clash", "skip")]
  [InlineData("--in-place")]
  public void Options_about_a_target_need_one(params string[] args)
  {
    Assert.Equal(ExitCodes.InvalidArguments, Run(["--items", "Paths", .. args]));
  }

  [Fact]
  public void An_empty_choice_writes_nothing()
  {
    var manifest = Path.Combine(_folder, "choice.json");
    File.WriteAllText(manifest, """{ "formatVersion": 1, "tool": "vbadoc", "items": [] }""");

    Assert.Equal(ExitCodes.Success, Run("--manifest", manifest, "--new", New));

    Assert.False(File.Exists(New));
  }

  [Fact]
  public void Without_anywhere_to_go_it_says_what_would_write_them()
  {
    Assert.Equal(ExitCodes.Success, Run("--items", "Paths"));

    Assert.Contains("Nothing was written. Add --new, --into or --bundle to write them out", _log.Messages(LogEventLevel.Information));
  }
}
