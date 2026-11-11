using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;

namespace ExcelDocumenter.Desktop.Documenting;

/// <summary>
/// Turns the Document tab's settings into each tool's main command. Only what differs from a tool's default is
/// written, so the command is the one a person would type. Long option names are used, so it reads well in a script.
/// </summary>
internal static class DocumentCommands
{
  /// <summary>
  /// The options of each tool's main command that the tab sets. Every other option is in <see cref="LeftOut"/>, and a
  /// test checks both against the tools' command lines, so a new option can't be forgotten.
  /// </summary>
  public static IReadOnlyDictionary<Tool, IReadOnlyList<string>> Covered { get; } = new Dictionary<Tool, IReadOnlyList<string>>
  {
    [Tool.Vbadoc] = ["--output", "--output-name", "--embed-source", "--on-exists", "--verbose", "--quiet"],
    [Tool.Pqdoc] = ["--output", "--output-name", "--format", "--embed-source", "--plantuml-server", "--on-exists", "--verbose", "--quiet"],
  };

  /// <summary>Options of both tools' main commands the tab leaves out, and why.</summary>
  public static IReadOnlyDictionary<string, string> LeftOut { get; } = new Dictionary<string, string>
  {
    ["--force"] = "the same as --on-exists overwrite, which \"If a file exists\" sets",
    ["--help"] = "describes the program that's running, which here is the app, not the tool",
    ["--version"] = "describes the program that's running; the status bar shows each tool's version instead",
  };

  /// <summary>The commands a run makes: one for each tool that's ticked and reads the input, vbadoc first.</summary>
  public static IReadOnlyList<ToolCommand> For(DocumentSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var input = Tidy(settings.Input);
    if (input.Length == 0)
    {
      return [];
    }

    var commands = new List<ToolCommand>();
    if (settings.Vbadoc.Run && Readers.Reads(Tool.Vbadoc, input))
    {
      commands.Add(Vbadoc(settings));
    }

    if (settings.Pqdoc.Run && Readers.Reads(Tool.Pqdoc, input) && settings.Pqdoc.Formats.Count > 0)
    {
      commands.Add(Pqdoc(settings));
    }

    return commands;
  }

  /// <summary>
  /// What stops a run: nothing chosen, no tool to run, or what a tool's command line says is wrong. Empty when it can
  /// run.
  /// </summary>
  public static IReadOnlyList<string> Problems(DocumentSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var input = Tidy(settings.Input);
    if (input.Length == 0)
    {
      return ["Choose a workbook, or a folder of .m files for Power Query."];
    }

    var readers = Tools.All.Where(tool => Readers.Reads(tool, input)).ToList();
    if (readers.Count == 0)
    {
      return [$"Neither tool reads {Path.GetExtension(input).ToLowerInvariant()} files."];
    }

    var problems = new List<string>();
    if (readers.Contains(Tool.Pqdoc) && settings.Pqdoc.Run && settings.Pqdoc.Formats.Count == 0)
    {
      problems.Add("Tick at least one format for Power Query.");
    }

    var commands = For(settings);
    if (commands.Count == 0 && problems.Count == 0)
    {
      problems.Add(readers.Count == 1 ? $"Tick {Label(readers[0])}." : "Tick VBA, Power Query or both.");
    }

    problems.AddRange(commands.SelectMany(command => command.Problems().Select(problem => $"{command.Tool.Name()}: {problem}")));
    return problems;
  }

  /// <summary>vbadoc's command: <c>vbadoc &lt;workbook&gt; [options]</c>.</summary>
  public static ToolCommand Vbadoc(DocumentSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var arguments = new List<string> { Tidy(settings.Input) };
    AddValue(arguments, "--output", settings.Vbadoc.Output);
    AddValue(arguments, "--output-name", settings.Vbadoc.OutputName);
    AddShared(arguments, settings);
    return new ToolCommand(Tool.Vbadoc, arguments);
  }

  /// <summary>pqdoc's command: <c>pqdoc &lt;input&gt; [options]</c>.</summary>
  public static ToolCommand Pqdoc(DocumentSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    var pqdoc = settings.Pqdoc;
    var arguments = new List<string> { Tidy(settings.Input) };
    AddValue(arguments, "--output", pqdoc.Output);
    AddValue(arguments, "--output-name", pqdoc.OutputName);

    // In the order pqdoc's help lists them, and only when they aren't the default.
    var formats = OutputFormatExtensions.All.Where(pqdoc.Formats.Contains).ToList();
    if (!formats.SequenceEqual(OutputFormatExtensions.Default))
    {
      arguments.AddRange(["--format", string.Join(',', formats.Select(format => format.CliName()))]);
    }

    // The diagram text goes to the server, so it's only passed when a format needs one.
    if (formats.Any(format => format.IsRendered()))
    {
      AddValue(arguments, "--plantuml-server", pqdoc.PlantUmlServer);
    }

    AddShared(arguments, settings);
    return new ToolCommand(Tool.Pqdoc, arguments);
  }

  /// <summary>
  /// Text as typed, tidied into an argument: without spaces around it, or the quotes Explorer's "Copy as path" puts
  /// round a path.
  /// </summary>
  public static string Tidy(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    var tidied = text.Trim();
    return tidied.Length >= 2 && tidied[0] == '"' && tidied[^1] == '"' ? tidied[1..^1].Trim() : tidied;
  }

  /// <summary>What the window calls a tool.</summary>
  public static string Label(Tool tool) => tool switch
  {
    Tool.Vbadoc => "VBA",
    Tool.Pqdoc => "Power Query",
    _ => tool.Name(),
  };

  private static void AddValue(List<string> arguments, string option, string text)
  {
    var value = Tidy(text);
    if (value.Length > 0)
    {
      arguments.AddRange([option, value]);
    }
  }

  // The options both tools have, last, as their help lists them.
  private static void AddShared(List<string> arguments, DocumentSettings settings)
  {
    if (settings.EmbedSource)
    {
      arguments.Add("--embed-source");
    }

    if (settings.OnExists != OnExistsBehaviour.Prompt)
    {
      arguments.AddRange(["--on-exists", settings.OnExists.ToString().ToLowerInvariant()]);
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
  }
}
