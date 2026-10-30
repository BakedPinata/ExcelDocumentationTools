using System.CommandLine;
using VbaDocumenter.Logging;

namespace VbaDocumenter.Cli;

/// <summary>The outcome of turning a parse result into <see cref="CommandLineOptions"/>.</summary>
/// <param name="Options">The options, or null when there are errors.</param>
/// <param name="Errors">What was wrong with the arguments (exit code 2).</param>
sealed record BindingResult(CommandLineOptions? Options, IReadOnlyList<string> Errors);

/// <summary>Turns a successful parse into <see cref="CommandLineOptions"/>, resolving paths.</summary>
static class OptionsBinder
{
  public static BindingResult Bind(ParseResult parseResult, CliDefinition cli)
  {
    var errors = new List<string>();

    var workbook = ResolvePath(parseResult.GetValue(cli.Workbook), "workbook", errors);
    var output = parseResult.GetValue(cli.Output);
    var outputFolder = output is not null
        ? ResolvePath(output, "--output", errors)
        : workbook is null ? null : Path.Combine(Path.GetDirectoryName(workbook)!, "VBAProject");

    if (workbook is null || outputFolder is null) return new BindingResult(null, errors);

    var options = new CommandLineOptions(workbook, outputFolder, parseResult.GetValue(cli.EmbedSource), GetVerbosity(parseResult, cli));
    return new BindingResult(options, errors);
  }

  /// <summary>
  /// Reads the requested verbosity. Safe to call on a parse result with errors, since it is needed to set up logging
  /// before they are reported. Conflicting flags fall back to normal.
  /// </summary>
  public static LogVerbosity GetVerbosity(ParseResult parseResult, CliDefinition cli) =>
      (parseResult.GetValue(cli.Verbose), parseResult.GetValue(cli.Quiet)) switch
      {
        (true, false) => LogVerbosity.Verbose,
        (false, true) => LogVerbosity.Quiet,
        _ => LogVerbosity.Normal,
      };

  static string? ResolvePath(string? value, string name, List<string> errors)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      errors.Add($"{name} must not be empty.");
      return null;
    }

    try
    {
      return Path.GetFullPath(value);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
      errors.Add($"{name} '{value}' is not a valid path: {ex.Message}");
      return null;
    }
  }
}
