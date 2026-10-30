using PowerQueryDocumenter.Core.Application;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Logging;
using Serilog;

namespace PowerQueryDocumenter.Cli;

/// <summary>The real console, environment and clock, for <see cref="Program"/>.</summary>
internal static class DefaultServices
{
  /// <summary>Creates the services a normal run uses.</summary>
  public static AppServices Create(HttpClient httpClient)
  {
    var environment = new SystemEnvironment();
    return new AppServices
    {
      HttpClient = httpClient,
      Interaction = new ConsoleUserInteraction(),
      Environment = environment,
      TimeProvider = TimeProvider.System,
      Output = Console.Out,
      Error = Console.Error,
      CreateLogger = verbosity =>
      {
        // Assigned to the static logger so Program can flush it with Log.CloseAndFlush (NFR-LOG-5).
        Log.Logger = LoggingSetup.CreateConsoleLogger(verbosity, LoggingSetup.UseColour(environment, Console.IsErrorRedirected));
        return Log.Logger;
      },
    };
  }
}
