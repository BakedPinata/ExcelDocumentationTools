using System.CommandLine;
using System.CommandLine.Parsing;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Shared.CommandLine;

/// <summary>The workbook an <c>import</c> writes.</summary>
/// <param name="New">A new workbook to build, as a full path, or null.</param>
/// <param name="Into">The workbook to take as the target, as a full path, or null.</param>
/// <param name="InPlace">Whether to change the target itself rather than write a copy beside it.</param>
/// <param name="OnClash">What to do with an item the target already has.</param>
public sealed record ImportTarget(string? New, string? Into, bool InPlace, OnClashBehaviour OnClash);

/// <summary>
/// Where an <c>import</c> writes, when that's a workbook rather than a bundle: <c>--new</c>, <c>--into</c>,
/// <c>--in-place</c> and <c>--on-clash</c>. They're added to the command by the tools that can write workbooks.
/// </summary>
public sealed class ImportTargetOptions
{
  private readonly ImportOptions import;

  /// <summary>Creates the options and adds them to the command.</summary>
  /// <param name="import">The <c>import</c> command.</param>
  /// <param name="items">What the items are, for the help text, such as "modules and classes".</param>
  /// <param name="newFile">What <c>--new</c> takes, for the help text.</param>
  public ImportTargetOptions(ImportOptions import, string items, string newFile)
  {
    ArgumentNullException.ThrowIfNull(import);
    this.import = import;

    New = new Option<string?>("--new")
    {
      Description = $"A new workbook to write them into. {newFile}",
      HelpName = "file",
    };

    Into = new Option<string?>("--into")
    {
      Description = "A workbook to take as the target. It's only read: a copy is written beside it, named with \"-imported\".",
      HelpName = "file",
    };

    InPlace = new Option<bool>("--in-place")
    {
      Description = "With --into, change the target itself, which can't be undone. It asks first; --force (-y) answers yes, for unattended runs.",
    };

    OnClash = new Option<OnClashBehaviour?>("--on-clash")
    {
      Description = $"With --into, what to do with {items} the target already has, when they differ: overwrite or skip. "
        + "[default: ask about each one when interactive, otherwise stop]",
      HelpName = "behaviour",
      CustomParser = Parse,
    };

    import.Bundle.Description = "A folder to write them into, with a README on importing them by hand.";
    import.Overwrite.OnExists.Description = "What to do when the file or bundle to write already exists: prompt, overwrite, skip or fail. "
      + "[default: prompt when interactive, otherwise fail]";
    import.Overwrite.Force.Description = "Don't ask: overwrite the file or bundle written, and with --in-place, change the target. Doesn't answer --on-clash.";

    import.Command.Options.Add(New);
    import.Command.Options.Add(Into);
    import.Command.Options.Add(InPlace);
    import.Command.Options.Add(OnClash);
    import.Command.Validators.Add(Validate);
  }

  /// <summary><c>--new</c>.</summary>
  public Option<string?> New { get; }

  /// <summary><c>--into</c>.</summary>
  public Option<string?> Into { get; }

  /// <summary><c>--in-place</c>.</summary>
  public Option<bool> InPlace { get; }

  /// <summary><c>--on-clash</c>.</summary>
  public Option<OnClashBehaviour?> OnClash { get; }

  /// <summary>The workbook asked for, with its paths made full, or null if the command wasn't told to write one.</summary>
  public ImportTarget? Bind(ParseResult parseResult)
  {
    ArgumentNullException.ThrowIfNull(parseResult);
    var newFile = parseResult.GetValue(New);
    var into = parseResult.GetValue(Into);
    return newFile is null && into is null
      ? null
      : new ImportTarget(
        newFile is null ? null : Path.GetFullPath(newFile),
        into is null ? null : Path.GetFullPath(into),
        parseResult.GetValue(InPlace),
        parseResult.GetValue(OnClash) ?? OnClashBehaviour.Prompt);
  }

  private void Validate(CommandResult result)
  {
    var into = result.GetValue(Into) is not null;
    var places = new[] { result.GetValue(import.Bundle) is not null, result.GetValue(New) is not null, into };
    if (places.Count(given => given) > 1)
    {
      result.AddError("Say where they go with one of --new, --into or --bundle (-b).");
    }

    if (result.GetValue(InPlace) && !into)
    {
      result.AddError("--in-place changes the target given with --into, so it needs one.");
    }

    if (result.GetResult(OnClash) is { Implicit: false } && !into)
    {
      result.AddError("--on-clash is about what a target already has, so it needs --into.");
    }

    if (result.GetValue(InPlace) && result.GetResult(import.Overwrite.OnExists) is { Implicit: false })
    {
      result.AddError("--in-place writes no new file for --on-exists to apply to. --force (-y) answers its question.");
    }
  }

  private static OnClashBehaviour? Parse(ArgumentResult result)
  {
    var value = result.Tokens.Count > 0 ? result.Tokens[^1].Value : string.Empty;
    switch (value.ToLowerInvariant())
    {
      case "overwrite":
        return OnClashBehaviour.Overwrite;
      case "skip":
        return OnClashBehaviour.Skip;
      default:
        result.AddError($"Invalid value '{value}' for --on-clash. Valid values are: overwrite, skip.");
        return null;
    }
  }
}
