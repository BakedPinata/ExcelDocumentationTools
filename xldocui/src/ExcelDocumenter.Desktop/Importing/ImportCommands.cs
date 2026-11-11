using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Desktop.Importing;

/// <summary>
/// Turns the Import tab's settings into a tool's <c>import</c> command, writing only what differs from the defaults, as
/// <see cref="DocumentCommands"/> does. Preview is the same command with nowhere to write, which says what the choice
/// comes to and writes nothing.
/// </summary>
internal static class ImportCommands
{
  /// <summary>The options of each tool's <c>import</c> the tab sets. A test checks them, and <see cref="LeftOut"/>, against the tools.</summary>
  public static IReadOnlyDictionary<Tool, IReadOnlyList<string>> Covered { get; } = new Dictionary<Tool, IReadOnlyList<string>>
  {
    [Tool.Vbadoc] =
    [
      "--manifest", "--items", "--no-references", "--bundle", "--new", "--into", "--in-place", "--on-clash", "--drop-signature",
      "--on-exists", "--verbose", "--quiet",
    ],
    [Tool.Pqdoc] = ["--manifest", "--items", "--bundle", "--new", "--into", "--in-place", "--on-clash", "--on-exists", "--verbose", "--quiet"],
  };

  /// <summary>Options of both tools' <c>import</c> the tab leaves out, and why.</summary>
  public static IReadOnlyDictionary<string, string> LeftOut { get; } = new Dictionary<string, string>
  {
    ["--force"] = "the same as \"If a file exists\" set to Overwrite; changing the target itself asks in a dialog instead",
    ["--help"] = "describes the program that's running, which here is the app, not the tool",
  };

  /// <summary>The tool that takes from the source: the one chosen if it reads it, or else the one that does; null if neither does.</summary>
  public static Tool? ToolFor(ImportSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var source = DocumentCommands.Tidy(settings.Source);
    var readers = Tools.All.Where(tool => Readers.Reads(tool, source)).ToList();
    return readers.Contains(settings.Tool) ? settings.Tool : readers.Count > 0 ? readers[0] : null;
  }

  /// <summary>What Run runs: the choice, and where it goes. Null until there's a source a tool reads.</summary>
  public static ToolCommand? Run(ImportSettings settings) => Build(settings, preview: false);

  /// <summary>What Preview runs: the choice with nowhere to write. Null until there's a source a tool reads.</summary>
  public static ToolCommand? Preview(ImportSettings settings) => Build(settings, preview: true);

  /// <summary>What stops Run, or with <paramref name="preview"/> what stops Preview. Empty when it can run.</summary>
  public static IReadOnlyList<string> Problems(ImportSettings settings, bool preview = false)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var source = DocumentCommands.Tidy(settings.Source);
    if (source.Length == 0)
    {
      return ["Choose a workbook to take from, or for queries a folder of .m files."];
    }

    if (ToolFor(settings) is not { } tool)
    {
      return [$"Neither tool reads {Path.GetExtension(source).ToLowerInvariant()} files."];
    }

    var problems = new List<string>();
    if (settings.ChooseFrom == ChooseFrom.Manifest)
    {
      if (DocumentCommands.Tidy(settings.Manifest).Length == 0)
      {
        problems.Add("Choose a manifest, as the page's Export tab downloads.");
      }
    }
    else if (settings.Items.Count == 0)
    {
      problems.Add("Tick what to take.");
    }
    else
    {
      // --items is split at commas, so a name with one in it can only be chosen with a manifest.
      problems.AddRange(settings.Items.Where(item => item.Contains(',', StringComparison.Ordinal)).Select(item =>
        $"{item} has a comma in its name, which --items can't take. Choose it with a manifest from the page's Export tab."));
    }

    if (!preview && Destination(settings).Length == 0)
    {
      problems.Add(settings.WriteTo switch
      {
        ImportDestination.Bundle => "Choose a folder for the bundle.",
        ImportDestination.NewWorkbook => "Choose a name for the new workbook.",
        _ => "Choose the target workbook.",
      });
    }

    if (problems.Count == 0 && Build(settings, preview) is { } command)
    {
      problems.AddRange(command.Problems().Select(problem => $"{tool.Name()}: {problem}"));
    }

    return problems;
  }

  private static ToolCommand? Build(ImportSettings settings, bool preview)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var source = DocumentCommands.Tidy(settings.Source);
    if (source.Length == 0 || ToolFor(settings) is not { } tool)
    {
      return null;
    }

    var arguments = new List<string> { "import", source };
    if (settings.ChooseFrom == ChooseFrom.Manifest)
    {
      AddValue(arguments, "--manifest", settings.Manifest);
    }
    else if (settings.Items.Count > 0)
    {
      arguments.AddRange(["--items", string.Join(',', settings.Items)]);
    }

    if (tool == Tool.Vbadoc && settings.NoReferences)
    {
      arguments.Add("--no-references");
    }

    if (!preview)
    {
      AddDestination(arguments, settings, tool);
    }

    switch (settings.LogDetail)
    {
      case LogDetail.Verbose:
        arguments.Add("--verbose");
        break;
      case LogDetail.Quiet:
        arguments.Add("--quiet");
        break;
    }

    return new ToolCommand(tool, arguments);
  }

  private static void AddDestination(List<string> arguments, ImportSettings settings, Tool tool)
  {
    var destination = Destination(settings);
    if (destination.Length == 0)
    {
      return;
    }

    switch (settings.WriteTo)
    {
      case ImportDestination.Bundle:
        arguments.AddRange(["--bundle", destination]);
        break;
      case ImportDestination.NewWorkbook:
        arguments.AddRange(["--new", destination]);
        break;
      default:
        arguments.AddRange(["--into", destination]);
        if (settings.WriteTo == ImportDestination.InPlace)
        {
          arguments.Add("--in-place");
        }

        if (settings.OnClash != OnClashBehaviour.Prompt)
        {
          arguments.AddRange(["--on-clash", settings.OnClash.ToString().ToLowerInvariant()]);
        }

        if (tool == Tool.Vbadoc && settings.DropSignature)
        {
          arguments.Add("--drop-signature");
        }

        break;
    }

    // Changing the target itself writes no new file for it to apply to; it asks instead.
    if (settings.WriteTo != ImportDestination.InPlace && settings.OnExists != OnExistsBehaviour.Prompt)
    {
      arguments.AddRange(["--on-exists", settings.OnExists.ToString().ToLowerInvariant()]);
    }
  }

  // The path for where the items go, tidied, or empty if there's none yet.
  private static string Destination(ImportSettings settings) => DocumentCommands.Tidy(settings.WriteTo switch
  {
    ImportDestination.Bundle => settings.Bundle,
    ImportDestination.NewWorkbook => settings.NewWorkbook,
    _ => settings.Target,
  });

  private static void AddValue(List<string> arguments, string option, string text)
  {
    var value = DocumentCommands.Tidy(text);
    if (value.Length > 0)
    {
      arguments.AddRange([option, value]);
    }
  }
}
