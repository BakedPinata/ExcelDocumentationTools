using VbaCli = VbaDocumenter.Cli;
using PqCli = PowerQueryDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Running;

/// <summary>The tools the app runs, in the order a run takes them.</summary>
internal enum Tool
{
  Vbadoc,
  Pqdoc,
}

/// <summary>What each tool is called, from its own command line.</summary>
internal static class Tools
{
  public static IReadOnlyList<Tool> All { get; } = [Tool.Vbadoc, Tool.Pqdoc];

  /// <summary>The tool's name, as typed at a prompt.</summary>
  public static string Name(this Tool tool) => tool switch
  {
    Tool.Vbadoc => VbaCli.CliDefinition.ToolName,
    Tool.Pqdoc => PqCli.CliDefinition.ToolName,
    _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
  };
}
