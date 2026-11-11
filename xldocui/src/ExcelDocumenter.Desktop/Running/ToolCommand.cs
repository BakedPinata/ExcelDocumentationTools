using VbaCli = VbaDocumenter.Cli;
using PqCli = PowerQueryDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Running;

/// <summary>
/// One run of one tool, as the arguments its console app would be given. The app runs exactly these, so what the window
/// shows, what Copy copies and what runs can't differ.
/// </summary>
internal sealed record ToolCommand(Tool Tool, IReadOnlyList<string> Arguments)
{
  /// <summary>The command as it would be typed at a prompt.</summary>
  public string Text => CommandLineText.Format(Tool.Name(), Arguments);

  /// <summary>
  /// What the tool's own command line says is wrong with the arguments, found without running anything. Problems that
  /// need the files, such as a workbook that isn't there, are only found by running.
  /// </summary>
  public IReadOnlyList<string> Problems()
  {
    var errors = Tool switch
    {
      Tool.Vbadoc => new VbaCli.CliDefinition().Parse(Arguments).Errors,
      Tool.Pqdoc => new PqCli.CliDefinition().Parse(Arguments).Errors,
      _ => throw new InvalidOperationException($"Unknown tool {Tool}."),
    };
    return errors.Select(e => e.Message).ToList();
  }
}
