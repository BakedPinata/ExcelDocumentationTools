namespace PowerQueryDocumenter.Core.Configuration;

/// <summary>Validated settings for one run, produced from the parsed command line (NFR-ARCH-2).</summary>
public sealed record AnalyserOptions
{
  /// <summary>Default base name for output files.</summary>
  public const string DefaultOutputName = "powerquery-dependencies";

  /// <summary>Default timeout for one PlantUML render request (FR-PUML-6).</summary>
  public static TimeSpan DefaultRenderTimeout { get; } = TimeSpan.FromSeconds(30);

  /// <summary>Absolute path of the folder containing the .m files.</summary>
  public required string InputFolder { get; init; }

  /// <summary>Absolute path of the folder outputs are written to.</summary>
  public required string OutputFolder { get; init; }

  /// <summary>Base file name for outputs, without an extension.</summary>
  public required string OutputName { get; init; }

  /// <summary>Formats to produce, distinct and in <see cref="OutputFormat"/> order. Includes CSV unless <c>--no-csv</c> was given.</summary>
  public required IReadOnlyList<OutputFormat> Formats { get; init; }

  /// <summary>PlantUML server base URL. Required when any rendered format is requested.</summary>
  public Uri? PlantUmlServer { get; init; }

  /// <summary>What to do when an output file already exists.</summary>
  public OnExistsBehaviour OnExists { get; init; } = OnExistsBehaviour.Prompt;

  /// <summary>Log verbosity.</summary>
  public LogVerbosity Verbosity { get; init; } = LogVerbosity.Normal;

  /// <summary>Timeout for each PlantUML render request.</summary>
  public TimeSpan RenderTimeout { get; init; } = DefaultRenderTimeout;

  /// <summary>Full path of the output file for <paramref name="format"/>.</summary>
  public string GetOutputPath(OutputFormat format) => Path.Combine(OutputFolder, OutputName + format.FileExtension());
}
