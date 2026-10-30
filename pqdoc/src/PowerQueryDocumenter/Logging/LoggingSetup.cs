using System.Globalization;
using PowerQueryDocumenter.Core.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace PowerQueryDocumenter.Logging;

/// <summary>Serilog configuration for console output (NFR-LOG-1 to NFR-LOG-6).</summary>
internal static class LoggingSetup
{
  /// <summary>Concise template used by default: level and message only.</summary>
  public const string OutputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

  /// <summary>Template used with <c>--verbose</c>: adds a timestamp.</summary>
  public const string VerboseOutputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

  /// <summary>Maps the requested verbosity to a minimum log level (NFR-LOG-2).</summary>
  public static LogEventLevel MinimumLevel(LogVerbosity verbosity) => verbosity switch
  {
    LogVerbosity.Verbose => LogEventLevel.Debug,
    LogVerbosity.Quiet => LogEventLevel.Warning,
    _ => LogEventLevel.Information,
  };

  /// <summary>
  /// Colour is used only when stderr is a terminal and the <c>NO_COLOR</c> convention
  /// (https://no-color.org) is not in effect (NFR-LOG-6).
  /// </summary>
  public static bool UseColour(IAppEnvironment environment, bool isErrorRedirected)
  {
    ArgumentNullException.ThrowIfNull(environment);
    return !isErrorRedirected && string.IsNullOrEmpty(environment.GetEnvironmentVariable("NO_COLOR"));
  }

  /// <summary>
  /// Creates a console logger that writes every level to stderr, keeping stdout free for intended
  /// output (NFR-LOG-5). Exceptions are only attached to Debug events, so stack traces are shown
  /// with <c>--verbose</c> and never otherwise (FR-ERR-4).
  /// </summary>
  public static Logger CreateConsoleLogger(LogVerbosity verbosity, bool useColour) =>
    new LoggerConfiguration()
      .MinimumLevel.Is(MinimumLevel(verbosity))
      .WriteTo.Console(
        outputTemplate: verbosity == LogVerbosity.Verbose ? VerboseOutputTemplate : OutputTemplate,
        formatProvider: CultureInfo.InvariantCulture,
        standardErrorFromLevel: LogEventLevel.Verbose,
        theme: useColour ? AnsiConsoleTheme.Sixteen : ConsoleTheme.None)
      .CreateLogger();
}
