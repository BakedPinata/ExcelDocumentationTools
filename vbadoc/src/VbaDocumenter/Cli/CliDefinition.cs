using System.CommandLine;
using System.CommandLine.Parsing;

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

    EmbedSource = new Option<bool>("--embed-source")
    {
      Description = "Put the module source inside the page, so it works without \"Load folder\".",
    };

    Verbose = new Option<bool>("--verbose", "-v")
    {
      Description = "List every member found in each module, with its line range, and show debug-level log output.",
    };

    Quiet = new Option<bool>("--quiet", "-q")
    {
      Description = "Show warnings and errors only.",
    };

    RootCommand = new RootCommand(
        "Exports the VBA from a macro-enabled Excel workbook into text files and generates a self-contained HTML page "
        + "that documents it.")
    {
      Workbook,
      Output,
      EmbedSource,
      Verbose,
      Quiet,
    };

    RootCommand.Validators.Add(Validate);
  }

  public RootCommand RootCommand { get; }

  public Argument<string> Workbook { get; }

  /// <summary><c>--output</c> / <c>-o</c>.</summary>
  public Option<string?> Output { get; }

  /// <summary><c>--embed-source</c>.</summary>
  public Option<bool> EmbedSource { get; }

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
    if (result.GetValue(Verbose) && result.GetValue(Quiet))
    {
      result.AddError("--verbose (-v) and --quiet (-q) cannot be used together.");
    }
  }
}
