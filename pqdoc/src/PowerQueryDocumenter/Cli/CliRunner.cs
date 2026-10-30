using System.CommandLine;
using System.CommandLine.Invocation;
using PowerQueryDocumenter.Core.Application;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Cli;

/// <summary>
/// The top-level flow: parse, validate, run, and map every outcome to an exit code (section 5.3).
/// Kept separate from <see cref="Program"/> so the whole flow can be tested with fakes.
/// </summary>
internal static class CliRunner
{
  /// <summary>Runs the tool and returns the process exit code.</summary>
  public static async Task<int> RunAsync(IReadOnlyList<string> args, AppServices services, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(services);

    var cli = new CliDefinition();
    ParseResult parseResult;
    try
    {
      parseResult = cli.Parse(args);
    }
    catch (Exception ex)
    {
      var fallbackLogger = services.CreateLogger(LogVerbosity.Normal);
      fallbackLogger.Error("Unexpected error while reading the command line: {Message}", ex.Message);
      fallbackLogger.Debug(ex, "Command-line failure detail");
      return ExitCodes.UnexpectedError;
    }

    // --help and --version: let System.CommandLine print them (exit code 0).
    if (parseResult.Action is not null and not ParseErrorAction)
    {
      var configuration = new InvocationConfiguration
      {
        Output = services.Output,
        Error = services.Error,
        ProcessTerminationTimeout = null,
      };
      return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    var logger = services.CreateLogger(OptionsBinder.GetVerbosity(parseResult, cli));

    try
    {
      // Validation errors: name the problem and the option, show brief usage, exit 2 (FR-CLI-3).
      if (parseResult.Errors.Count > 0)
      {
        return ReportInvalidArguments(parseResult.Errors.Select(e => e.Message));
      }

      var binding = await OptionsBinder
        .BindAsync(parseResult, cli, services.Environment, cancellationToken)
        .ConfigureAwait(false);
      if (binding.Options is null)
      {
        return ReportInvalidArguments(binding.Errors);
      }

      logger.Debug("Options: {@Options}", binding.Options);
      return await new AnalyserApp(logger, services).RunAsync(binding.Options, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      logger.Warning("Cancelled. No partial output files were left behind");
      return ExitCodes.Cancelled;
    }
    catch (Exception ex)
    {
      // FR-ERR-5: anything unexpected is logged without a stack trace (shown with --verbose).
      logger.Error("Unexpected error: {Message}", ex.Message);
      logger.Debug(ex, "Unexpected error detail");
      return ExitCodes.UnexpectedError;
    }

    int ReportInvalidArguments(IEnumerable<string> messages)
    {
      foreach (var message in messages)
      {
        logger.Error("{Message}", message);
      }

      services.Error.WriteLine(CliDefinition.BriefUsage);
      return ExitCodes.InvalidArguments;
    }
  }
}
