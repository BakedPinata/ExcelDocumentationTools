namespace PowerQueryDocumenter.Core.Configuration;

/// <summary>An export format. Declaration order is the order outputs are produced in.</summary>
public enum OutputFormat
{
  /// <summary>Dependency CSV.</summary>
  Csv,

  /// <summary>PlantUML source.</summary>
  Puml,

  /// <summary>Self-contained interactive HTML graph (the default).</summary>
  Html,

  /// <summary>SVG rendered by a PlantUML server.</summary>
  Svg,

  /// <summary>PNG rendered by a PlantUML server.</summary>
  Png,

  /// <summary>PDF rendered by a PlantUML server.</summary>
  Pdf,
}

/// <summary>Helpers for <see cref="OutputFormat"/>.</summary>
public static class OutputFormatExtensions
{
  /// <summary>Every format <c>--format</c> accepts, in the order help and messages list them.</summary>
  public static IReadOnlyList<OutputFormat> All { get; } =
    [OutputFormat.Html, OutputFormat.Csv, OutputFormat.Puml, OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg];

  /// <summary>What's written when <c>--format</c> isn't given: the interactive page, which is the output people use.</summary>
  public static IReadOnlyList<OutputFormat> Default { get; } = [OutputFormat.Html];

  /// <summary>The name used on the command line and in messages, for example "puml".</summary>
  public static string CliName(this OutputFormat format) => format switch
  {
    OutputFormat.Csv => "csv",
    OutputFormat.Puml => "puml",
    OutputFormat.Html => "html",
    OutputFormat.Svg => "svg",
    OutputFormat.Png => "png",
    OutputFormat.Pdf => "pdf",
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
  };

  /// <summary>The file extension including the leading dot, for example ".puml".</summary>
  public static string FileExtension(this OutputFormat format) => "." + format.CliName();

  /// <summary>True for formats rendered by a PlantUML server.</summary>
  public static bool IsRendered(this OutputFormat format) =>
    format is OutputFormat.Svg or OutputFormat.Png or OutputFormat.Pdf;

  /// <summary>Parses a <c>--format</c> value (case-insensitive).</summary>
  public static bool TryParse(string value, out OutputFormat format)
  {
    foreach (var candidate in All)
    {
      if (string.Equals(candidate.CliName(), value.Trim(), StringComparison.OrdinalIgnoreCase))
      {
        format = candidate;
        return true;
      }
    }

    format = default;
    return false;
  }
}
