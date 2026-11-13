using System.Globalization;
using System.Text;
using CodeDocumenter.Shared;
using CodeDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Application;
using PowerQueryDocumenter.Core.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace CodeDocumenter.Desktop.Running;

/// <summary>How one run ended.</summary>
/// <param name="ExitCode">The tool's exit code, as its console app would have returned it.</param>
/// <param name="Page">The page the tool put in place, or null if it didn't.</param>
internal sealed record RunResult(int ExitCode, string? Page);

/// <summary>
/// Runs a tool's command line inside the app, off the window's thread, with the app's log, questions and cancellation in
/// place of the console's. Parsing, validation, messages and exit codes are all the tool's own.
/// </summary>
internal sealed class ToolRunner(HttpClient plantUmlClient)
{
  /// <param name="command">What to run.</param>
  /// <param name="interaction">Asks the tool's questions. It's called off the window's thread.</param>
  /// <param name="log">Takes each line the tool writes. It's called off the window's thread.</param>
  /// <param name="cancellationToken">Cancels the run, as Ctrl+C does at a console.</param>
  public Task<RunResult> RunAsync(ToolCommand command, IUserInteraction interaction, Action<LogLine> log, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(command);
    ArgumentNullException.ThrowIfNull(interaction);
    ArgumentNullException.ThrowIfNull(log);
    return Task.Run(() => RunHereAsync(command, interaction, log, cancellationToken), CancellationToken.None);
  }

  private async Task<RunResult> RunHereAsync(ToolCommand command, IUserInteraction interaction, Action<LogLine> log, CancellationToken cancellationToken)
  {
    string? page = null;
    var loggers = new List<Logger>();
    using var output = new LineWriter(text => log(new LogLine(text, LogKind.Plain)));
    using var error = new LineWriter(text => log(new LogLine(text, LogKind.Plain)));

    ILogger CreateLogger(LogEventLevel level, bool verbose)
    {
      // The console's templates, so a line reads the same in the window as at a prompt.
      var template = verbose ? VbaCli.Logging.LoggingSetup.VerboseOutputTemplate : VbaCli.Logging.LoggingSetup.OutputTemplate;
      var logger = new LoggerConfiguration().MinimumLevel.Is(level).WriteTo.Sink(new LogLineSink(template, log)).CreateLogger();
      loggers.Add(logger);
      return logger;
    }

    try
    {
      var exitCode = command.Tool switch
      {
        Tool.Vbadoc => VbaCli.CliRunner.Run(
          command.Arguments,
          new VbaCli.AppServices
          {
            Output = output,
            Error = error,
            Interaction = interaction,
            CreateLogger = verbosity => CreateLogger(
              VbaCli.Logging.LoggingSetup.MinimumLevel(verbosity), verbosity == VbaCli.Logging.LogVerbosity.Verbose),
            PageWritten = path => page = path,
          },
          cancellationToken),
        Tool.Pqdoc => await PqCli.CliRunner.RunAsync(
          command.Arguments,
          new AppServices
          {
            HttpClient = plantUmlClient,
            Interaction = interaction,
            Environment = new SystemEnvironment(),
            Output = output,
            Error = error,
            CreateLogger = verbosity => CreateLogger(PqCli.Logging.LoggingSetup.MinimumLevel(verbosity), verbosity == LogVerbosity.Verbose),
            PageWritten = path => page = path,
          },
          cancellationToken).ConfigureAwait(false),
        _ => throw new InvalidOperationException($"Unknown tool {command.Tool}."),
      };
      return new RunResult(exitCode, page);
    }
    catch (Exception ex)
    {
      // The tools turn their own failures into exit codes, so this is the app's: a question it couldn't ask, say.
      log(new LogLine($"[ERR] Unexpected error: {ex.Message}", LogKind.Error));
      return new RunResult(ExitCodes.UnexpectedError, page);
    }
    finally
    {
      output.Flush();
      error.Flush();
      foreach (var logger in loggers)
      {
        logger.Dispose();
      }
    }
  }

  /// <summary>A Serilog sink that formats each event as the console does and hands over its lines.</summary>
  private sealed class LogLineSink(string template, Action<LogLine> log) : ILogEventSink
  {
    private readonly MessageTemplateTextFormatter formatter = new(template, CultureInfo.InvariantCulture);

    public void Emit(LogEvent logEvent)
    {
      using var text = new StringWriter(CultureInfo.InvariantCulture);
      formatter.Format(logEvent, text);
      var kind = LogLine.KindOf(logEvent.Level);
      foreach (var line in text.ToString().TrimEnd('\r', '\n').Split('\n'))
      {
        log(new LogLine(line.TrimEnd('\r'), kind));
      }
    }
  }

  /// <summary>Standard output or error for a tool: hands over each line as it's finished.</summary>
  private sealed class LineWriter(Action<string> writeLine) : TextWriter(CultureInfo.InvariantCulture)
  {
    private readonly StringBuilder line = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
      if (value == '\n')
      {
        writeLine(line.ToString().TrimEnd('\r'));
        line.Clear();
      }
      else
      {
        line.Append(value);
      }
    }

    public override void Flush()
    {
      if (line.Length > 0)
      {
        writeLine(line.ToString());
        line.Clear();
      }
    }
  }
}
