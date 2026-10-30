using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace VbaDocumenter.Logging;

/// <summary>How much the tool reports.</summary>
enum LogVerbosity
{
  Normal,
  Verbose,
  Quiet,
}

/// <summary>Serilog configuration for console output, the same as pqdoc's.</summary>
static class LoggingSetup
{
  /// <summary>Concise template used by default: level and message only.</summary>
  public const string OutputTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

  /// <summary>Template used with <c>--verbose</c>: adds a timestamp.</summary>
  public const string VerboseOutputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

  public static LogEventLevel MinimumLevel(LogVerbosity verbosity) => verbosity switch
  {
    LogVerbosity.Verbose => LogEventLevel.Debug,
    LogVerbosity.Quiet => LogEventLevel.Warning,
    _ => LogEventLevel.Information,
  };

  /// <summary>
  /// Colour is used only when stderr is a terminal and the <c>NO_COLOR</c> convention
  /// (https://no-color.org) is not in effect.
  /// </summary>
  public static bool UseColour(string? noColour, bool isErrorRedirected) =>
      !isErrorRedirected && string.IsNullOrEmpty(noColour);

  /// <summary>
  /// Creates a console logger that writes every level to stderr, keeping stdout free for help and version text.
  /// Exceptions are only attached to Debug events, so stack traces appear with <c>--verbose</c> and never otherwise.
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
