using System.CommandLine;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Cli;

/// <summary>The outcome of turning a parse result into <see cref="AnalyserOptions"/>.</summary>
/// <param name="Options">The options, or null when there are errors.</param>
/// <param name="Errors">Configuration errors (exit code 2).</param>
internal sealed record BindingResult(AnalyserOptions? Options, IReadOnlyList<string> Errors);

/// <summary>
/// Turns a successful parse into validated <see cref="AnalyserOptions"/>, applying the rules that
/// depend on the environment: resolving relative paths against the current directory and finding
/// the PlantUML server URL.
/// </summary>
internal static class OptionsBinder
{
  /// <summary>Builds options from a parse result that has no parse errors.</summary>
  public static async Task<BindingResult> BindAsync(
    ParseResult parseResult,
    CliDefinition cli,
    IAppEnvironment environment,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(parseResult);
    ArgumentNullException.ThrowIfNull(cli);
    ArgumentNullException.ThrowIfNull(environment);

    var errors = new List<string>();

    // An input that is a file is taken as a workbook; anything else as a folder of .m files, which the run reports
    // if it doesn't exist. A workbook's outputs go in a folder beside it, a folder's next to its .m files.
    var input = ResolvePath(parseResult.GetValue(cli.Input), environment, "input", errors);
    var inputIsWorkbook = input is not null && File.Exists(input);
    var outputFolderValue = parseResult.GetValue(cli.Output);
    var outputFolder = !string.IsNullOrWhiteSpace(outputFolderValue)
      ? ResolvePath(outputFolderValue, environment, "--output", errors)
      : inputIsWorkbook
        ? Path.Combine(Path.GetDirectoryName(input!)!, AnalyserOptions.DefaultWorkbookOutputFolder)
        : input;

    // --format can't be given empty, so an empty list means it wasn't given.
    var requested = parseResult.GetValue(cli.Formats) is { Count: > 0 } given ? given : OutputFormatExtensions.Default;
    var formats = requested.Distinct().Order().ToArray();

    // Always validate an explicit --plantuml-server value; only go looking for a server in the
    // environment or a .env file when a rendered format actually needs one.
    var serverOption = parseResult.GetValue(cli.PlantUmlServer);
    Uri? server = null;
    if (formats.Any(f => f.IsRendered()) || !string.IsNullOrWhiteSpace(serverOption))
    {
      var resolution = await PlantUmlServerResolver
        .ResolveAsync(serverOption, environment, cancellationToken)
        .ConfigureAwait(false);
      if (resolution.Error is not null)
      {
        errors.Add(resolution.Error);
      }
      else if (resolution.Server is null)
      {
        errors.Add(PlantUmlServerResolver.MissingServerMessage);
      }

      server = resolution.Server;
    }

    var onExists = parseResult.GetValue(cli.Force)
      ? OnExistsBehaviour.Overwrite
      : parseResult.GetValue(cli.OnExists) ?? OnExistsBehaviour.Prompt;

    if (errors.Count > 0 || input is null || outputFolder is null)
    {
      return new BindingResult(null, errors);
    }

    var options = new AnalyserOptions
    {
      Input = input,
      InputIsWorkbook = inputIsWorkbook,
      OutputFolder = outputFolder,
      OutputName = parseResult.GetValue(cli.OutputName) ?? AnalyserOptions.DefaultOutputName(input, inputIsWorkbook),
      Formats = formats,
      PlantUmlServer = server,
      OnExists = onExists,
      EmbedSource = parseResult.GetValue(cli.EmbedSource),
      Verbosity = GetVerbosity(parseResult, cli),
    };

    return new BindingResult(options, errors);
  }

  /// <summary>
  /// Reads the requested verbosity. Safe to call on a parse result with errors (it is needed to set
  /// up logging before the errors are reported); conflicting flags fall back to normal.
  /// </summary>
  public static LogVerbosity GetVerbosity(ParseResult parseResult, CliDefinition cli)
  {
    ArgumentNullException.ThrowIfNull(parseResult);
    ArgumentNullException.ThrowIfNull(cli);

    var verbose = parseResult.GetValue(cli.Verbose);
    var quiet = parseResult.GetValue(cli.Quiet);
    return (verbose, quiet) switch
    {
      (true, false) => LogVerbosity.Verbose,
      (false, true) => LogVerbosity.Quiet,
      _ => LogVerbosity.Normal,
    };
  }

  private static string? ResolvePath(string? value, IAppEnvironment environment, string optionName, List<string> errors)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      errors.Add($"{optionName} must not be empty.");
      return null;
    }

    try
    {
      return Path.GetFullPath(value, environment.CurrentDirectory);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
      errors.Add($"{optionName} '{value}' is not a valid path: {ex.Message}");
      return null;
    }
  }
}
