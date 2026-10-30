using System.CommandLine;
using System.CommandLine.Parsing;
using PowerQueryDocumenter.Configuration;

namespace PowerQueryDocumenter.Cli;

/// <summary>
/// The command-line surface (FR-CLI-1 to FR-CLI-3), built with System.CommandLine so that help,
/// version and parse-error messages are generated automatically. Rules that only involve the
/// arguments themselves are enforced here as parse errors; rules that need the environment
/// (such as finding a PlantUML server) are applied afterwards by <see cref="OptionsBinder"/>.
/// </summary>
public sealed class CliDefinition
{
  /// <summary>The tool's name as shown in usage text.</summary>
  public const string ToolName = "pqdoc";

  // Characters that are invalid in a Windows file name. Checked on every platform so that the
  // same --output-name works everywhere.
  private static readonly char[] InvalidFileNameCharacters = ['<', '>', ':', '"', '|', '?', '*'];

  /// <summary>Creates the command and all of its options.</summary>
  public CliDefinition()
  {
    InputFolder = new Argument<string>("input_folder")
    {
      Description = "Folder containing the exported Power Query .m files (one query per file). Subfolders are not scanned.",
    };

    OutputName = new Option<string>("--output-name", "-n")
    {
      Description = "Base file name for all outputs, without an extension. The right extension is added per format.",
      HelpName = "name",
      DefaultValueFactory = _ => AnalyserOptions.DefaultOutputName,
    };

    OutputFolder = new Option<string?>("--output-folder", "-o")
    {
      Description = "Folder to write outputs to. Created if it does not exist. [default: the input folder]",
      HelpName = "path",
    };

    Formats = new Option<IReadOnlyList<OutputFormat>>("--format", "-f")
    {
      Description = "Additional formats to produce: puml, html, pdf, png, svg. Comma-separated (-f puml,html) "
        + "or repeated (-f puml -f html). pdf, png and svg are rendered by a PlantUML server.",
      HelpName = "format",
      Arity = ArgumentArity.OneOrMore,
      AllowMultipleArgumentsPerToken = false,
      CustomParser = ParseFormats,
      DefaultValueFactory = _ => [],
    };

    NoCsv = new Option<bool>("--no-csv")
    {
      Description = "Do not produce the CSV. Requires at least one --format value.",
    };

    PlantUmlServer = new Option<string?>("--plantuml-server")
    {
      Description = "Base URL of the PlantUML server used to render pdf, png and svg. Can also be set with the "
        + $"{PlantUmlServerResolver.EnvironmentVariableName} environment variable or a .env file in the current directory.",
      HelpName = "url",
    };

    OnExists = new Option<OnExistsBehaviour?>("--on-exists")
    {
      Description = "What to do when an output file already exists: prompt, overwrite, skip or fail. "
        + "[default: prompt when interactive, otherwise fail]",
      HelpName = "behaviour",
      CustomParser = ParseOnExists,
    };

    Force = new Option<bool>("--force", "-y")
    {
      Description = "Overwrite existing output files without asking. Shorthand for --on-exists overwrite.",
    };

    Verbose = new Option<bool>("--verbose", "-v")
    {
      Description = "Show debug-level log output.",
    };

    Quiet = new Option<bool>("--quiet", "-q")
    {
      Description = "Show warnings and errors only.",
    };

    RootCommand = new RootCommand(
      "Analyses the dependencies between exported Power Query (.m) queries and exports them as CSV, "
      + "PlantUML, rendered diagrams (via a PlantUML server) or an interactive HTML graph.")
    {
      InputFolder,
      OutputName,
      OutputFolder,
      Formats,
      NoCsv,
      PlantUmlServer,
      OnExists,
      Force,
      Verbose,
      Quiet,
    };

    RootCommand.Validators.Add(Validate);
  }

  /// <summary>The root command.</summary>
  public RootCommand RootCommand { get; }

  /// <summary>The required input folder argument.</summary>
  public Argument<string> InputFolder { get; }

  /// <summary><c>--output-name</c> / <c>-n</c>.</summary>
  public Option<string> OutputName { get; }

  /// <summary><c>--output-folder</c> / <c>-o</c>.</summary>
  public Option<string?> OutputFolder { get; }

  /// <summary><c>--format</c> / <c>-f</c>.</summary>
  public Option<IReadOnlyList<OutputFormat>> Formats { get; }

  /// <summary><c>--no-csv</c>.</summary>
  public Option<bool> NoCsv { get; }

  /// <summary><c>--plantuml-server</c>.</summary>
  public Option<string?> PlantUmlServer { get; }

  /// <summary><c>--on-exists</c>.</summary>
  public Option<OnExistsBehaviour?> OnExists { get; }

  /// <summary><c>--force</c> / <c>-y</c>.</summary>
  public Option<bool> Force { get; }

  /// <summary><c>--verbose</c> / <c>-v</c>.</summary>
  public Option<bool> Verbose { get; }

  /// <summary><c>--quiet</c> / <c>-q</c>.</summary>
  public Option<bool> Quiet { get; }

  /// <summary>One-line usage shown after a validation error (FR-CLI-3).</summary>
  public static string BriefUsage =>
    $"Usage: {ToolName} <input_folder> [options]{Environment.NewLine}Run '{ToolName} --help' for the full list of options.";

  /// <summary>Parses command-line arguments.</summary>
  public ParseResult Parse(IReadOnlyList<string> args) => RootCommand.Parse(args);

  /// <summary>
  /// Validates an output base name: it must be a plain file name with no folder and no extension.
  /// Returns an error message, or null when the name is valid.
  /// </summary>
  public static string? ValidateOutputName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return "--output-name must not be empty.";
    }

    if (name.IndexOfAny(['/', '\\']) >= 0)
    {
      return $"--output-name '{name}' must be a file name, not a path. Use --output-folder to choose where files are written.";
    }

    if (name.IndexOfAny(InvalidFileNameCharacters) >= 0 || name.Any(char.IsControl))
    {
      return $"--output-name '{name}' contains a character that is not allowed in file names "
        + $"({string.Join(' ', InvalidFileNameCharacters)}).";
    }

    if (name.StartsWith(' ') || name.EndsWith(' ') || name.EndsWith('.'))
    {
      return $"--output-name '{name}' must not start or end with a space, or end with a dot.";
    }

    if (Path.HasExtension(name))
    {
      return $"--output-name '{name}' must not include an extension ('{Path.GetExtension(name)}'). "
        + "The right extension is added for each format.";
    }

    return null;
  }

  private void Validate(CommandResult result)
  {
    // Inside a command validator, GetValue returns default(T) for options that were not given
    // (default value factories are not applied yet) and throws for options whose own parser
    // reports an error. So only explicitly given values are checked, from their raw tokens.
    var nameResult = result.GetResult(OutputName);
    if (nameResult is { Implicit: false, Tokens.Count: > 0 })
    {
      var nameError = ValidateOutputName(nameResult.Tokens[^1].Value);
      if (nameError is not null)
      {
        result.AddError(nameError);
      }
    }

    var formatsResult = result.GetResult(Formats);
    var formatsGiven = formatsResult is { Implicit: false } && formatsResult.Tokens.Count > 0;
    if (result.GetValue(NoCsv) && !formatsGiven)
    {
      result.AddError("--no-csv was given without --format, so nothing would be produced. "
        + "Add at least one --format value (for example -f html) or remove --no-csv.");
    }

    var onExistsResult = result.GetResult(OnExists);
    if (result.GetValue(Force)
      && onExistsResult is { Implicit: false, Tokens.Count: > 0 }
      && TryParseOnExists(onExistsResult.Tokens[^1].Value, out var onExists)
      && onExists != OnExistsBehaviour.Overwrite)
    {
      result.AddError($"--force (-y) means --on-exists overwrite, which conflicts with --on-exists {onExists.ToString().ToLowerInvariant()}. "
        + "Use only one of them.");
    }

    if (result.GetValue(Verbose) && result.GetValue(Quiet))
    {
      result.AddError("--verbose (-v) and --quiet (-q) cannot be used together.");
    }
  }

  private static bool TryParseOnExists(string value, out OnExistsBehaviour behaviour) =>
    Enum.TryParse(value, ignoreCase: true, out behaviour)
    && Enum.IsDefined(behaviour)
    && !int.TryParse(value, out _);

  private static IReadOnlyList<OutputFormat> ParseFormats(ArgumentResult result)
  {
    var formats = new List<OutputFormat>();
    var validNames = string.Join(", ", OutputFormatExtensions.SelectableFormats.Select(f => f.CliName()));

    foreach (var token in result.Tokens)
    {
      foreach (var part in token.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
      {
        if (!OutputFormatExtensions.TryParseSelectable(part, out var format))
        {
          var hint = string.Equals(part, "csv", StringComparison.OrdinalIgnoreCase)
            ? " CSV is produced by default (use --no-csv to turn it off)."
            : string.Empty;
          result.AddError($"Invalid value '{part}' for --format. Valid values are: {validNames}.{hint}");
          return [];
        }

        if (!formats.Contains(format))
        {
          formats.Add(format);
        }
      }
    }

    if (formats.Count == 0)
    {
      result.AddError($"--format needs at least one value. Valid values are: {validNames}.");
    }

    return formats;
  }

  private static OnExistsBehaviour? ParseOnExists(ArgumentResult result)
  {
    var value = result.Tokens.Count > 0 ? result.Tokens[^1].Value : string.Empty;
    if (TryParseOnExists(value, out var behaviour))
    {
      return behaviour;
    }

    result.AddError($"Invalid value '{value}' for --on-exists. Valid values are: prompt, overwrite, skip, fail.");
    return null;
  }
}
