using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Files;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using VbaDocumenter.Cli;
using VbaDocumenter.Logging;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Cli;

/// <summary>Runs the whole tool in-process, so every exit code is checked against what it really does.</summary>
public sealed class CliRunnerTests : IDisposable
{
  static readonly string Workbook = Fixtures.Workbook("SimpleMacro.xlsm");

  readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-test-{Guid.NewGuid():N}");
  readonly TestLog _log = new();
  readonly StringWriter _output = new();
  readonly StringWriter _error = new();

  string Output => Path.Combine(_folder, "out");
  string Page => Path.Combine(Output, "SimpleMacro-vba.html");

  public CliRunnerTests() => Directory.CreateDirectory(_folder);

  public void Dispose()
  {
    _output.Dispose();
    _error.Dispose();
    if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
  }

  // Like a script: nobody to ask. Tests that answer the prompt set their own.
  IUserInteraction _interaction = new FakeUserInteraction(isInteractive: false);

  AppServices Services(Func<LogVerbosity, ILogger>? createLogger = null) => new()
  {
    Output = _output,
    Error = _error,
    Interaction = _interaction,
    CreateLogger = createLogger ?? (verbosity => _log.CreateLogger(LoggingSetup.MinimumLevel(verbosity))),
  };

  int Run(params string[] args) => CliRunner.Run(args, Services(), TestContext.Current.CancellationToken);

  [Fact]
  public void A_workbook_is_exported_and_stdout_stays_clean()
  {
    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));

    Assert.True(File.Exists(Page));
    Assert.Contains(_log.Messages(LogEventLevel.Information), m => m.StartsWith("Reading "));
    Assert.Equal("", _output.ToString());
  }

  [Fact]
  public void Older_pages_from_vbadoc_are_deleted_and_other_pages_are_left_alone()
  {
    Directory.CreateDirectory(Output);
    var vbadocPage = "<!DOCTYPE html>\n<html>\n<head>\n<meta name=\"generator\" content=\"vbadoc 0.6.0 (feacd3f)\">\n</head></html>";
    File.WriteAllText(Path.Combine(Output, "index.html"), vbadocPage);         // from before the page was renamed
    File.WriteAllText(Path.Combine(Output, "OldName-vba.html"), vbadocPage);   // from before the workbook was renamed
    File.WriteAllText(Path.Combine(Output, "notes.html"), "<p>written by hand</p>");
    File.WriteAllText(Path.Combine(Output, "queries.html"), "<meta name=\"generator\" content=\"pqdoc 1.2.0 (271f0f3)\">");

    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));

    Assert.Equal(new[] { "notes.html", "queries.html", "SimpleMacro-vba.html" },
        Directory.GetFiles(Output, "*.html").Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
    Assert.Contains(_log.Messages(LogEventLevel.Information), m => m.Contains("deleted") && m.Contains("index.html"));
  }

  [Fact]
  public void Quiet_hides_the_report_but_still_exports()
  {
    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output, "--quiet"));

    Assert.True(File.Exists(Page));
    Assert.Empty(_log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public void Verbose_lists_each_member_and_normal_runs_do_not()
  {
    Run(Workbook, "--output", Output);
    Assert.Empty(_log.Messages(LogEventLevel.Debug));

    Run(Workbook, "--output", Output, "--verbose");
    Assert.NotEmpty(_log.Messages(LogEventLevel.Debug));
  }

  [Fact]
  public void Verbose_shows_how_long_each_step_took()
  {
    Run(Workbook, "--output", Output, "--verbose");

    var debug = _log.Messages(LogEventLevel.Debug);
    foreach (var step in new[] { "Reading", "Parsing", "Analysing", "Building the page", "Writing module files", "Writing the page" })
      Assert.Contains(debug, m => m.StartsWith($"  {step} took "));
    Assert.Contains(debug, m => m.StartsWith("Finished in "));
  }

  [Theory]
  [InlineData(false, ConsoleReport.ShortListLength + 2, 0)] // the heading, the short list and "... and 5 more"
  [InlineData(true, ConsoleReport.ShortListLength + 1, 5)] // the heading and the short list, then the rest
  public void Long_lists_are_cut_short_unless_verbose(bool verbose, int expectedInformation, int expectedDebug)
  {
    var items = Enumerable.Range(0, ConsoleReport.ShortListLength + 5).Select(i => $"item {i}").ToList();
    var logger = _log.CreateLogger(LoggingSetup.MinimumLevel(verbose ? LogVerbosity.Verbose : LogVerbosity.Normal));

    ConsoleReport.WriteList(logger, "Heading", items);

    Assert.Equal(expectedInformation, _log.Messages(LogEventLevel.Information).Count);
    Assert.Equal(expectedDebug, _log.Messages(LogEventLevel.Debug).Count);
  }

  [Fact]
  public void A_missing_workbook_is_an_input_problem()
  {
    Assert.Equal(ExitCodes.InputError, Run(Path.Combine(_folder, "missing.xlsm"), "--output", Output));

    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.Contains("missing.xlsm"));
    Assert.False(Directory.Exists(Output));
  }

  [Fact]
  public void A_file_that_is_not_a_workbook_is_an_input_problem()
  {
    var notAWorkbook = Path.Combine(_folder, "notes.xlsm");
    File.WriteAllText(notAWorkbook, "not a workbook");

    Assert.Equal(ExitCodes.InputError, Run(notAWorkbook, "--output", Output));
    Assert.False(Directory.Exists(Output));
  }

  public static TheoryData<string[]> InvalidArguments => new()
  {
    Array.Empty<string>(),
    new[] { "a.xlsm", "--bogus" },
    new[] { "a.xlsm", "--verbose", "--quiet" },
    new[] { "" },
  };

  [Theory]
  [MemberData(nameof(InvalidArguments))]
  public void Invalid_arguments_exit_with_2_and_show_usage(string[] args)
  {
    Assert.Equal(ExitCodes.InvalidArguments, Run(args));

    Assert.NotEmpty(_log.Messages(LogEventLevel.Error));
    Assert.Contains("Usage: vbadoc <workbook>", _error.ToString());
  }

  // ---- An existing page: asked about only when it would change; the module files never are ----

  /// <summary>Exports once, then leaves a page a new run would change, and a module file a new run would write.</summary>
  string PageThatWouldChange()
  {
    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));
    File.WriteAllText(Page, "an older page");
    var moduleFile = Directory.GetFiles(Output).First(f => f.EndsWith(".bas") || f.EndsWith(".cls"));
    File.Delete(moduleFile);
    return moduleFile;
  }

  [Fact]
  public void A_script_cannot_overwrite_a_changed_page_without_force_and_nothing_is_written()
  {
    var moduleFile = PageThatWouldChange();

    Assert.Equal(ExitCodes.OutputRefused, Run(Workbook, "--output", Output));

    Assert.Equal("an older page", File.ReadAllText(Page));
    Assert.False(File.Exists(moduleFile));
    var errors = string.Join("\n", _log.Messages(LogEventLevel.Error));
    Assert.Contains("not interactive", errors);
    Assert.Contains("--force", errors);
  }

  [Fact]
  public void An_unchanged_page_is_not_asked_about()
  {
    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));
    _interaction = new FakeUserInteraction(isInteractive: true); // a prompt would fail the run

    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));

    Assert.Contains(_log.Messages(LogEventLevel.Information), m => m.Contains("SimpleMacro-vba.html") && m.Contains("unchanged"));
  }

  [Theory]
  [InlineData("--force", null)]
  [InlineData("-y", null)]
  [InlineData("--on-exists", "overwrite")]
  public void Force_or_overwrite_replaces_a_changed_page(string option, string? value)
  {
    var moduleFile = PageThatWouldChange();
    string[] args = value is null ? [Workbook, "--output", Output, option] : [Workbook, "--output", Output, option, value];

    Assert.Equal(ExitCodes.Success, Run(args));

    Assert.NotEqual("an older page", File.ReadAllText(Page));
    Assert.True(File.Exists(moduleFile));
  }

  [Fact]
  public void Skip_keeps_a_changed_page_but_still_writes_the_module_files()
  {
    var moduleFile = PageThatWouldChange();

    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output, "--on-exists", "skip"));

    Assert.Equal("an older page", File.ReadAllText(Page));
    Assert.True(File.Exists(moduleFile));
    Assert.Contains(_log.Messages(LogEventLevel.Warning), m => m.StartsWith("Skipping \"page\""));
  }

  [Fact]
  public void Fail_refuses_even_when_someone_could_be_asked()
  {
    var moduleFile = PageThatWouldChange();
    var interaction = new FakeUserInteraction(isInteractive: true);
    _interaction = interaction;

    Assert.Equal(ExitCodes.OutputRefused, Run(Workbook, "--output", Output, "--on-exists", "fail"));

    Assert.Empty(interaction.Asked);
    Assert.Equal("an older page", File.ReadAllText(Page));
    Assert.False(File.Exists(moduleFile));
  }

  [Theory]
  [InlineData(OverwriteAnswer.Yes, true)]
  [InlineData(OverwriteAnswer.All, true)]
  [InlineData(OverwriteAnswer.No, false)]
  public void The_prompt_asks_about_the_page_and_does_what_it_is_told(OverwriteAnswer answer, bool overwritten)
  {
    var moduleFile = PageThatWouldChange();
    var interaction = new FakeUserInteraction(isInteractive: true, answer);
    _interaction = interaction;

    Assert.Equal(ExitCodes.Success, Run(Workbook, "--output", Output));

    Assert.Equal(new[] { Page }, interaction.Asked);
    Assert.Equal(overwritten, File.ReadAllText(Page) != "an older page");
    Assert.True(File.Exists(moduleFile)); // the module files follow the workbook, whatever the answer
  }

  [Fact]
  public void Quitting_at_the_prompt_writes_nothing_and_exits_with_4()
  {
    var moduleFile = PageThatWouldChange();
    _interaction = new FakeUserInteraction(isInteractive: true, OverwriteAnswer.Quit);

    Assert.Equal(ExitCodes.OutputRefused, Run(Workbook, "--output", Output));

    Assert.Equal("an older page", File.ReadAllText(Page));
    Assert.False(File.Exists(moduleFile));
  }

  [Fact]
  public void An_output_that_cannot_be_written_exits_with_5()
  {
    var notAFolder = Path.Combine(_folder, "file.txt");
    File.WriteAllText(notAFolder, "a file where the output folder should go");

    Assert.Equal(ExitCodes.ExportFailed, Run(Workbook, "--output", notAFolder));

    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.StartsWith("Could not write to "));
  }

  [Fact]
  public void An_unexpected_error_exits_with_1_and_keeps_the_detail_for_verbose()
  {
    // Audit sinks let exceptions through, which is what makes the first log line fail here. Ordinary sinks swallow them.
    var logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .AuditTo.Sink(new FailsOnInformation())
        .WriteTo.Sink(_log)
        .CreateLogger();

    var exitCode = CliRunner.Run([Workbook, "--output", Output], Services(_ => logger), TestContext.Current.CancellationToken);

    Assert.Equal(ExitCodes.UnexpectedError, exitCode);
    Assert.Contains(_log.Messages(LogEventLevel.Error), m => m.StartsWith("Unexpected error: ") && m.Contains("Failed on purpose"));
    Assert.Contains("Unexpected error detail", _log.Messages(LogEventLevel.Debug));
  }

  [Fact]
  public void Ctrl_C_exits_with_130_and_writes_nothing()
  {
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    Assert.Equal(ExitCodes.Cancelled, CliRunner.Run([Workbook, "--output", Output], Services(), cancellation.Token));

    Assert.False(Directory.Exists(Output));
  }

  [Theory]
  [InlineData("--help")]
  [InlineData("-?")]
  [InlineData("--version")]
  public void Help_and_version_go_to_stdout_and_exit_with_0(string option)
  {
    Assert.Equal(ExitCodes.Success, Run(option));

    Assert.NotEqual("", _output.ToString());
  }

  [Theory]
  [InlineData(null, false, true)]
  [InlineData("", false, true)]
  [InlineData("1", false, false)]
  [InlineData(null, true, false)]
  public void Colour_respects_NO_COLOR_and_redirection(string? noColour, bool redirected, bool expected)
  {
    Assert.Equal(expected, LoggingSetup.UseColour(noColour, redirected));
  }

  sealed class FailsOnInformation : ILogEventSink
  {
    public void Emit(LogEvent logEvent)
    {
      if (logEvent.Level == LogEventLevel.Information) throw new InvalidOperationException("Failed on purpose");
    }
  }
}
