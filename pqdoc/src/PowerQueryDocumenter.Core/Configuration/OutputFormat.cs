namespace PowerQueryDocumenter.Core.Configuration;

/// <summary>An export format. Declaration order is the order outputs are produced in.</summary>
public enum OutputFormat
{
  /// <summary>Dependency CSV (on by default).</summary>
  Csv,

  /// <summary>PlantUML source.</summary>
  Puml,

  /// <summary>Self-contained interactive HTML graph.</summary>
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
  /// <summary>The formats that can be requested with <c>--format</c> (CSV is controlled by <c>--no-csv</c>).</summary>
  public static IReadOnlyList<OutputFormat> SelectableFormats { get; } =
    [OutputFormat.Puml, OutputFormat.Html, OutputFormat.Pdf, OutputFormat.Png, OutputFormat.Svg];

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
  public static bool TryParseSelectable(string value, out OutputFormat format)
  {
    foreach (var candidate in SelectableFormats)
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
