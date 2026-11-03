using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using Serilog;
using VbaDocumenter.Logging;

namespace VbaDocumenter.Cli;

/// <summary>What the tool needs from the outside world. Program uses the real console; tests capture it.</summary>
sealed class AppServices
{
  /// <summary>Creates the logger once the requested verbosity is known.</summary>
  public required Func<LogVerbosity, ILogger> CreateLogger { get; init; }

  /// <summary>Standard output (help and version text only).</summary>
  public required TextWriter Output { get; init; }

  /// <summary>Standard error (usage text after a validation error).</summary>
  public required TextWriter Error { get; init; }

  /// <summary>Asks whether to overwrite the page, when the session can.</summary>
  public required IUserInteraction Interaction { get; init; }

  public static AppServices CreateDefault() => new()
  {
    Output = Console.Out,
    Error = Console.Error,
    Interaction = new ConsoleUserInteraction(),
    CreateLogger = verbosity =>
    {
      var useColour = LoggingSetup.UseColour(Environment.GetEnvironmentVariable("NO_COLOR"), Console.IsErrorRedirected);

      // Assigned to the static logger so Program can flush it with Log.CloseAndFlush.
      Log.Logger = LoggingSetup.CreateConsoleLogger(verbosity, useColour);
      return Log.Logger;
    },
  };
}
