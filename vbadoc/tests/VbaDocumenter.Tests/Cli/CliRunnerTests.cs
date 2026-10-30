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
  string Page => Path.Combine(Output, "index.html");

  public CliRunnerTests() => Directory.CreateDirectory(_folder);

  public void Dispose()
  {
    _output.Dispose();
    _error.Dispose();
    if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
  }

  AppServices Services(Func<LogVerbosity, ILogger>? createLogger = null) => new()
  {
    Output = _output,
    Error = _error,
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
