namespace PowerQueryDocumenter.Core.Configuration;

/// <summary>Validated settings for one run, produced from the parsed command line (NFR-ARCH-2).</summary>
public sealed record AnalyserOptions
{
  /// <summary>Default base name for output files.</summary>
  public const string DefaultOutputName = "powerquery-dependencies";

  /// <summary>The folder, next to a workbook, that its outputs go in unless <c>--output</c> says otherwise.</summary>
  public const string DefaultWorkbookOutputFolder = "PowerQuery";

  /// <summary>Default timeout for one PlantUML render request (FR-PUML-6).</summary>
  public static TimeSpan DefaultRenderTimeout { get; } = TimeSpan.FromSeconds(30);

  /// <summary>Absolute path of the input: a workbook, or a folder of .m files.</summary>
  public required string Input { get; init; }

  /// <summary>True when <see cref="Input"/> is a workbook file rather than a folder (FR-IN-1).</summary>
  public bool InputIsWorkbook { get; init; }

  /// <summary>Absolute path of the folder outputs are written to.</summary>
  public required string OutputFolder { get; init; }

  /// <summary>Base file name for outputs, without an extension.</summary>
  public required string OutputName { get; init; }

  /// <summary>Formats to produce, distinct and in <see cref="OutputFormat"/> order. HTML alone unless <c>--format</c> was given.</summary>
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
