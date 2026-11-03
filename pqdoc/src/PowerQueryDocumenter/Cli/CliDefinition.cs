using System.CommandLine;
using System.CommandLine.Parsing;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Cli;

/// <summary>
/// The command-line surface (FR-CLI-1 to FR-CLI-3), built with System.CommandLine so that help,
/// version and parse-error messages are generated automatically. Rules that only involve the
/// arguments themselves are enforced here as parse errors; rules that need the environment
/// (such as finding a PlantUML server) are applied afterwards by <see cref="OptionsBinder"/>.
/// </summary>
internal sealed class CliDefinition
{
  /// <summary>The tool's name as shown in usage text.</summary>
  public const string ToolName = "pqdoc";

  // Characters that are invalid in a Windows file name, checked on every platform so that the same
  // --output-name works everywhere, and # and %, which break or confuse a browser's address. The
  // same rule as vbadoc's --output-name.
  private static readonly char[] InvalidFileNameCharacters = ['<', '>', ':', '"', '|', '?', '*', '#', '%'];

  /// <summary>Creates the command and all of its options.</summary>
  public CliDefinition()
  {
    Input = new Argument<string>("input")
    {
      Description = "An Excel workbook (.xlsx, .xlsm or .xlsb), or a folder of exported Power Query .m files (one query "
        + "per file; subfolders are not scanned).",
    };

    OutputName = new Option<string?>("--output-name", "-n")
    {
      Description = "Base file name for all outputs, without an extension. The right extension is added per format. "
        + "[default: the workbook's or folder's name and \"-powerquery\", e.g. Sales-powerquery]",
      HelpName = "name",
    };

    Output = new Option<string?>("--output", "-o")
    {
      Description = "Folder to write outputs to. Created if it does not exist. [default: a PowerQuery folder beside a workbook, or the input folder]",
      HelpName = "path",
    };

    // Left empty when not given, and the binder applies the default (html). Help shows the default from the
    // description, because System.CommandLine would show nothing for a list.
    Formats = new Option<IReadOnlyList<OutputFormat>>("--format", "-f")
    {
      Description = "Formats to write: html, csv, puml, pdf, png, svg. Comma-separated (-f html,csv) or repeated "
        + "(-f html -f csv). Replaces the default, so include html to keep the page. pdf, png and svg are rendered by "
        + "a PlantUML server. [default: html]",
      HelpName = "format",
      Arity = ArgumentArity.OneOrMore,
      AllowMultipleArgumentsPerToken = false,
      CustomParser = ParseFormats,
      DefaultValueFactory = _ => [],
    };

    EmbedSource = new Option<bool>("--embed-source")
    {
      Description = "Put each query's M code inside the page, so it shows without \"Load folder\". M code can hold "
        + "server names and file paths, so it's left out unless asked for.",
    };

    PlantUmlServer = new Option<string?>("--plantuml-server")
    {
      Description = "Base URL of the PlantUML server used to render pdf, png and svg. Can also be set with the "
        + $"{PlantUmlServerResolver.EnvironmentVariableName} environment variable or a .env file in the current directory.",
      HelpName = "url",
    };

    // --on-exists and --force, the same in every tool.
    Overwrite = new OverwriteOptions("an output file", "existing output files");

    Verbose = new Option<bool>("--verbose", "-v")
    {
      Description = "Show debug-level log output.",
    };

    Quiet = new Option<bool>("--quiet", "-q")
    {
      Description = "Show warnings and errors only.",
    };

    RootCommand = new RootCommand(
      "Analyses the dependencies between the Power Query queries in an Excel workbook, or in a folder of exported .m "
      + "files, and shows them as an interactive HTML graph. It can also write them as CSV, PlantUML, or diagrams "
      + "rendered by a PlantUML server.")
    {
      Input,
      OutputName,
      Output,
      Formats,
      EmbedSource,
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

  /// <summary>The required input: a workbook or a folder.</summary>
  public Argument<string> Input { get; }

  /// <summary><c>--output-name</c> / <c>-n</c>.</summary>
  public Option<string?> OutputName { get; }

  /// <summary><c>--output</c> / <c>-o</c>.</summary>
  public Option<string?> Output { get; }

  /// <summary><c>--format</c> / <c>-f</c>.</summary>
  public Option<IReadOnlyList<OutputFormat>> Formats { get; }

  /// <summary><c>--embed-source</c>.</summary>
  public Option<bool> EmbedSource { get; }

  /// <summary><c>--plantuml-server</c>.</summary>
  public Option<string?> PlantUmlServer { get; }

  /// <summary><c>--on-exists</c> and <c>--force</c> / <c>-y</c>.</summary>
  public OverwriteOptions Overwrite { get; }

  /// <summary><c>--on-exists</c>.</summary>
  public Option<OnExistsBehaviour?> OnExists => Overwrite.OnExists;

  /// <summary><c>--force</c> / <c>-y</c>.</summary>
  public Option<bool> Force => Overwrite.Force;

  /// <summary><c>--verbose</c> / <c>-v</c>.</summary>
  public Option<bool> Verbose { get; }

  /// <summary><c>--quiet</c> / <c>-q</c>.</summary>
  public Option<bool> Quiet { get; }

  /// <summary>One-line usage shown after a validation error (FR-CLI-3).</summary>
  public static string BriefUsage =>
    $"Usage: {ToolName} <input> [options]{Environment.NewLine}Run '{ToolName} --help' for the full list of options.";

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
      return $"--output-name '{name}' must be a file name, not a path. Use --output to choose where files are written.";
    }

    if (name.IndexOfAny(InvalidFileNameCharacters) >= 0 || name.Any(char.IsControl))
    {
      return $"--output-name '{name}' contains a character that can't be used in an output name "
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

    Overwrite.Validate(result);

    if (result.GetValue(Verbose) && result.GetValue(Quiet))
    {
      result.AddError("--verbose (-v) and --quiet (-q) cannot be used together.");
    }
  }

  private static IReadOnlyList<OutputFormat> ParseFormats(ArgumentResult result)
  {
    var formats = new List<OutputFormat>();
    var validNames = string.Join(", ", OutputFormatExtensions.All.Select(f => f.CliName()));

    foreach (var token in result.Tokens)
    {
      foreach (var part in token.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
      {
        if (!OutputFormatExtensions.TryParse(part, out var format))
        {
          result.AddError($"Invalid value '{part}' for --format. Valid values are: {validNames}.");
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
}
