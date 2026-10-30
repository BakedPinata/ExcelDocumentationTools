using PowerQueryDocumenter.Application;
using PowerQueryDocumenter.Cli;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Export;
using PowerQueryDocumenter.Logging;
using PowerQueryDocumenter.Output;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>
/// Runs the whole tool in-process with fakes for the console, environment, clock and HTTP, so
/// end-to-end behaviour and exit codes can be tested without a real terminal or network.
/// </summary>
public sealed class TestApp : IDisposable
{
  private readonly HttpClient httpClient;

  /// <param name="currentDirectory">The working directory the tool sees (where it looks for .env).</param>
  /// <param name="handler">HTTP handler for PlantUML requests. Defaults to one that fails if called.</param>
  /// <param name="interaction">Interactivity and prompt answers. Defaults to a non-interactive session.</param>
  /// <param name="variables">Environment variables the tool sees.</param>
  public TestApp(
    string currentDirectory,
    HttpMessageHandler? handler = null,
    IUserInteraction? interaction = null,
    Dictionary<string, string>? variables = null)
  {
    httpClient = new HttpClient(handler ?? StubHttpMessageHandler.Unused());
    Interaction = interaction ?? new FakeUserInteraction(isInteractive: false);
    Environment = new FakeEnvironment(currentDirectory, variables);
  }

  /// <summary>Captured log output.</summary>
  public TestLog Log { get; } = new();

  /// <summary>Captured standard output.</summary>
  public StringWriter Output { get; } = new();

  /// <summary>Captured standard error (usage text).</summary>
  public StringWriter Error { get; } = new();

  /// <summary>The interaction used.</summary>
  public IUserInteraction Interaction { get; }

  /// <summary>The environment used.</summary>
  public FakeEnvironment Environment { get; }

  /// <summary>Optional replacement for the exporter set.</summary>
  public Func<AnalyserOptions, ExporterRegistry>? CreateExporters { get; init; }

  /// <summary>Runs the tool with the given arguments.</summary>
  public Task<int> RunAsync(params string[] args) => RunWithCancellationAsync(TestContext.Current.CancellationToken, args);

  /// <summary>Runs the tool with the given arguments and cancellation token.</summary>
  public Task<int> RunWithCancellationAsync(CancellationToken cancellationToken, params string[] args)
  {
    var services = new AppServices
    {
      HttpClient = httpClient,
      Interaction = Interaction,
      Environment = Environment,
      TimeProvider = new FixedTimeProvider(),
      Output = Output,
      Error = Error,
      CreateLogger = verbosity => Log.CreateLogger(LoggingSetup.MinimumLevel(verbosity)),
      CreateExporters = CreateExporters,
    };

    return CliRunner.RunAsync(args, services, cancellationToken);
  }

  /// <inheritdoc />
  public void Dispose()
  {
    httpClient.Dispose();
    Output.Dispose();
    Error.Dispose();
  }
}
