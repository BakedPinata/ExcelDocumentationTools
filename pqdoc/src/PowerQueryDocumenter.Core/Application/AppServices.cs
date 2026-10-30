using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Output;
using Serilog;

namespace PowerQueryDocumenter.Core.Application;

/// <summary>
/// Everything the application needs from the outside world. The console app composes the real
/// implementations; tests substitute fakes (NFR-ARCH-3: manual composition, no container).
/// </summary>
public sealed class AppServices
{
  /// <summary>The single shared HTTP client (NFR-ARCH-4).</summary>
  public required HttpClient HttpClient { get; init; }

  /// <summary>Prompts and interactivity detection.</summary>
  public required IUserInteraction Interaction { get; init; }

  /// <summary>Environment variables and current directory.</summary>
  public required IAppEnvironment Environment { get; init; }

  /// <summary>Clock used for the generation timestamp.</summary>
  public required TimeProvider TimeProvider { get; init; }

  /// <summary>Creates the logger once the requested verbosity is known.</summary>
  public required Func<LogVerbosity, ILogger> CreateLogger { get; init; }

  /// <summary>Standard output (help and version text only).</summary>
  public required TextWriter Output { get; init; }

  /// <summary>Standard error (usage text after a validation error).</summary>
  public required TextWriter Error { get; init; }

  /// <summary>Creates the exporters for a run. Defaults to <see cref="ExporterRegistry.CreateDefault"/>.</summary>
  public Func<AnalyserOptions, ExporterRegistry>? CreateExporters { get; init; }
}
