using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using Serilog;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Cli.Logging;

namespace VbaDocumenter.Cli;

/// <summary>What the tool needs from the outside world. Program uses the real console; tests capture it.</summary>
public sealed class AppServices
{
  /// <summary>Creates the logger once the requested verbosity is known.</summary>
  public required Func<LogVerbosity, ILogger> CreateLogger { get; init; }

  /// <summary>Standard output (help and version text only).</summary>
  public required TextWriter Output { get; init; }

  /// <summary>Standard error (usage text after a validation error).</summary>
  public required TextWriter Error { get; init; }

  /// <summary>Asks whether to overwrite the page, when the session can.</summary>
  public required IUserInteraction Interaction { get; init; }

  /// <summary>Whether what a library reference points at is on this machine, or null if it can't be told.</summary>
  public Func<VbaReference, bool?> IsLibraryInstalled { get; init; } = ReferenceChoice.IsInstalled;

  /// <summary>
  /// Told the page's full path once it's in place, written or already up to date, so the desktop app can open it. Not
  /// told about a page that was skipped. The console doesn't use it.
  /// </summary>
  public Action<string>? PageWritten { get; init; }

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
