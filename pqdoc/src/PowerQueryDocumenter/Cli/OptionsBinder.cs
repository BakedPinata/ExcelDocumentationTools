using System.CommandLine;
using PowerQueryDocumenter.Configuration;

namespace PowerQueryDocumenter.Cli;

/// <summary>The outcome of turning a parse result into <see cref="AnalyserOptions"/>.</summary>
/// <param name="Options">The options, or null when there are errors.</param>
/// <param name="Errors">Configuration errors (exit code 2).</param>
public sealed record BindingResult(AnalyserOptions? Options, IReadOnlyList<string> Errors);

/// <summary>
/// Turns a successful parse into validated <see cref="AnalyserOptions"/>, applying the rules that
/// depend on the environment: resolving relative paths against the current directory and finding
/// the PlantUML server URL.
/// </summary>
public static class OptionsBinder
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

    var inputFolder = ResolvePath(parseResult.GetValue(cli.InputFolder), environment, "input_folder", errors);
    var outputFolderValue = parseResult.GetValue(cli.OutputFolder);
    var outputFolder = string.IsNullOrWhiteSpace(outputFolderValue)
      ? inputFolder
      : ResolvePath(outputFolderValue, environment, "--output-folder", errors);

    var requested = parseResult.GetValue(cli.Formats) ?? [];
    var formats = (parseResult.GetValue(cli.NoCsv) ? requested : requested.Prepend(OutputFormat.Csv))
      .Distinct()
      .Order()
      .ToArray();

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

    if (errors.Count > 0 || inputFolder is null || outputFolder is null)
    {
      return new BindingResult(null, errors);
    }

    var options = new AnalyserOptions
    {
      InputFolder = inputFolder,
      OutputFolder = outputFolder,
      OutputName = parseResult.GetValue(cli.OutputName) ?? AnalyserOptions.DefaultOutputName,
      Formats = formats,
      PlantUmlServer = server,
      OnExists = onExists,
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
