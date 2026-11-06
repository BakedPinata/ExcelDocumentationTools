using System.CommandLine;
using System.CommandLine.Parsing;
using ExcelDocumenter.Shared.CommandLine;

namespace VbaDocumenter.Cli;

/// <summary>
/// The command line, built with System.CommandLine so that help, version and parse errors work the same way as in
/// pqdoc. Rules that only involve the arguments are parse errors here; paths are resolved by <see cref="OptionsBinder"/>.
/// </summary>
sealed class CliDefinition
{
  /// <summary>The tool's name as shown in usage text.</summary>
  public const string ToolName = "vbadoc";

  public CliDefinition()
  {
    Workbook = new Argument<string>("workbook")
    {
      Description = ".xlsm, .xlsb, .xlam, .xls or .xla file.",
    };

    Output = new Option<string?>("--output", "-o")
    {
      Description = "Where to write the source files and page. [default: a \"VBAProject\" folder next to the workbook]",
      HelpName = "folder",
    };

    OutputName = new Option<string?>("--output-name", "-n")
    {
      Description = "The page's file name, without \".html\". [default: the workbook's name and \"-vba\", e.g. Sales-vba]",
      HelpName = "name",
    };

    EmbedSource = new Option<bool>("--embed-source")
    {
      Description = "Put the module source inside the page, so it works without \"Load folder\".",
    };

    // --on-exists and --force, the same in every tool. Only the page is asked about: the module files follow the workbook.
    Overwrite = new OverwriteOptions("the page", "an existing page");

    // Recursive, so they work on import too.
    Verbose = new Option<bool>("--verbose", "-v")
    {
      Description = "List every member found in each module, with its line range, and show debug-level log output.",
      Recursive = true,
    };

    Quiet = new Option<bool>("--quiet", "-q")
    {
      Description = "Show warnings and errors only.",
      Recursive = true,
    };

    Import = new ImportOptions(".xlsm, .xlsb, .xlam, .xls or .xla file to take them from.", "modules and classes");
    NoReferences = new Option<bool>("--no-references")
    {
      Description = "Leave out the source's library references.",
    };
    Import.Command.Options.Add(NoReferences);
    ImportTarget = new ImportTargetOptions(Import, "modules and classes", "It has to be an .xlsm.");
    DropSignature = new Option<bool>("--drop-signature")
    {
      Description = "With --into, go ahead though the target's VBA project is signed, removing the signature the change would break.",
    };
    Import.Command.Options.Add(DropSignature);
    Import.Command.Validators.Add(result =>
    {
      if (result.GetValue(DropSignature) && result.GetValue(ImportTarget.Into) is null)
      {
        result.AddError("--drop-signature is about the target given with --into, so it needs one.");
      }
    });

    RootCommand = new RootCommand(
        "Exports the VBA from a macro-enabled Excel workbook into text files and generates a self-contained HTML page "
        + "that documents it.")
    {
      Workbook,
      Output,
      OutputName,
      EmbedSource,
      Overwrite.OnExists,
      Overwrite.Force,
      Verbose,
      Quiet,
      Import.Command,
    };

    // CliRunner does the work. A command with subcommands is only allowed to run on its own if it has an action.
    RootCommand.SetAction(_ => { });
    RootCommand.Validators.Add(Validate);
  }

  public RootCommand RootCommand { get; }

  /// <summary>The <c>import</c> command.</summary>
  public ImportOptions Import { get; }

  /// <summary><c>--no-references</c> on <c>import</c>.</summary>
  public Option<bool> NoReferences { get; }

  /// <summary><c>--new</c>, <c>--into</c>, <c>--in-place</c> and <c>--on-clash</c> on <c>import</c>.</summary>
  public ImportTargetOptions ImportTarget { get; }

  /// <summary><c>--drop-signature</c> on <c>import</c>.</summary>
  public Option<bool> DropSignature { get; }

  public Argument<string> Workbook { get; }

  /// <summary><c>--output</c> / <c>-o</c>.</summary>
  public Option<string?> Output { get; }

  /// <summary><c>--output-name</c> / <c>-n</c>.</summary>
  public Option<string?> OutputName { get; }

  /// <summary><c>--embed-source</c>.</summary>
  public Option<bool> EmbedSource { get; }

  /// <summary><c>--on-exists</c> and <c>--force</c> / <c>-y</c>.</summary>
  public OverwriteOptions Overwrite { get; }

  /// <summary><c>--verbose</c> / <c>-v</c>.</summary>
  public Option<bool> Verbose { get; }

  /// <summary><c>--quiet</c> / <c>-q</c>.</summary>
  public Option<bool> Quiet { get; }

  /// <summary>Brief usage, shown after a validation error.</summary>
  public static string BriefUsage =>
      $"Usage: {ToolName} <workbook> [options]{Environment.NewLine}Run '{ToolName} --help' for the full list of options.";

  public ParseResult Parse(IReadOnlyList<string> args) => RootCommand.Parse(args);

  void Validate(CommandResult result)
  {
    var name = result.GetResult(OutputName);
    if (name is { Implicit: false, Tokens.Count: > 0 } && ValidateOutputName(name.Tokens[^1].Value) is { } nameError)
    {
      result.AddError(nameError);
    }

    Overwrite.Validate(result);

    if (result.GetValue(Verbose) && result.GetValue(Quiet))
    {
      result.AddError("--verbose (-v) and --quiet (-q) cannot be used together.");
    }
  }

  // Characters Windows doesn't allow in file names, checked on every platform so a name works everywhere, and # and
  // %, which break or confuse a browser's address. The same rule as pqdoc's --output-name.
  static readonly char[] InvalidNameCharacters = ['<', '>', ':', '"', '|', '?', '*', '#', '%'];

  /// <summary>
  /// Checks a page name: a plain file name with no folder and no extension. Returns an error message, or null when the
  /// name is fine.
  /// </summary>
  public static string? ValidateOutputName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name)) return "--output-name must not be empty.";
    if (name.IndexOfAny(['/', '\\']) >= 0)
      return $"--output-name '{name}' must be a file name, not a path. Use --output to choose where files are written.";
    if (name.IndexOfAny(InvalidNameCharacters) >= 0 || name.Any(char.IsControl))
      return $"--output-name '{name}' contains a character that can't be used in the page's name ({string.Join(' ', InvalidNameCharacters)}).";
    if (name.StartsWith(' ') || name.EndsWith(' ') || name.EndsWith('.'))
      return $"--output-name '{name}' must not start or end with a space, or end with a dot.";
    if (Path.HasExtension(name))
      return $"--output-name '{name}' must not include an extension ('{Path.GetExtension(name)}'). \".html\" is added.";
    return null;
  }
}
