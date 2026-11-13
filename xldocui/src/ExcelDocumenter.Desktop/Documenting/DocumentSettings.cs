using CodeDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;

namespace ExcelDocumenter.Desktop.Documenting;

/// <summary>How much the tools log: <c>--verbose</c>, <c>--quiet</c> or neither.</summary>
internal enum LogDetail
{
  Normal,
  Verbose,
  Quiet,
}

/// <summary>
/// What the Document tab is set to: the input, the options both tools have, and each tool's own. Text is kept as typed;
/// <see cref="DocumentCommands"/> tidies it into arguments. It's saved with the app's settings, so the window opens as
/// it was left.
/// </summary>
internal sealed class DocumentSettings
{
  /// <summary>A workbook, or for pqdoc a folder of .m files.</summary>
  public string Input { get; set; } = "";

  /// <summary><c>--embed-source</c>, for both tools.</summary>
  public bool EmbedSource { get; set; }

  /// <summary><c>--on-exists</c>, for both tools. Prompt, the default, asks in a dialog.</summary>
  public OnExistsBehaviour OnExists { get; set; } = OnExistsBehaviour.Prompt;

  /// <summary><c>--verbose</c> or <c>--quiet</c>, for both tools.</summary>
  public LogDetail LogDetail { get; set; }

  public VbadocSettings Vbadoc { get; set; } = new();

  public PqdocSettings Pqdoc { get; set; } = new();
}

/// <summary>vbadoc's own options.</summary>
internal sealed class VbadocSettings
{
  /// <summary>Whether a run includes vbadoc, when it reads the input.</summary>
  public bool Run { get; set; } = true;

  /// <summary><c>--output</c>, or empty for the default.</summary>
  public string Output { get; set; } = "";

  /// <summary><c>--output-name</c>, or empty for the default.</summary>
  public string OutputName { get; set; } = "";
}

/// <summary>pqdoc's own options.</summary>
internal sealed class PqdocSettings
{
  /// <summary>Whether a run includes pqdoc, when it reads the input.</summary>
  public bool Run { get; set; } = true;

  /// <summary><c>--output</c>, or empty for the default.</summary>
  public string Output { get; set; } = "";

  /// <summary><c>--output-name</c>, or empty for the default.</summary>
  public string OutputName { get; set; } = "";

  /// <summary><c>--format</c>: the page alone is the default.</summary>
  public List<OutputFormat> Formats { get; set; } = [.. OutputFormatExtensions.Default];

  /// <summary><c>--plantuml-server</c>, only passed when a format needs it.</summary>
  public string PlantUmlServer { get; set; } = "";
}
