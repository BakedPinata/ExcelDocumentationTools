using ExcelDocumenter.Desktop.Running;
using PqTarget = PowerQueryDocumenter.Core.Importing.TargetWorkbook;
using VbaTarget = VbaDocumenter.Core.Importing.TargetWorkbook;

namespace ExcelDocumenter.Desktop.Importing;

/// <summary>What each tool writes into, for the Import tab's file pickers, from the tools' own lists.</summary>
internal static class Writers
{
  /// <summary>What a tool can take as a target.</summary>
  public static IReadOnlyList<string> Targets(Tool tool) => tool == Tool.Pqdoc ? PqTarget.Extensions : VbaTarget.Extensions;

  /// <summary>The kinds of new file a tool can build.</summary>
  public static IReadOnlyList<string> NewFiles(Tool tool) => tool == Tool.Pqdoc ? PqTarget.NewExtensions : VbaTarget.NewExtensions;

  /// <summary>The target picker's filter: everything the tool takes, then all files.</summary>
  public static string TargetFilter(Tool tool)
  {
    var targets = Targets(tool);
    var what = targets.Contains(".xlam", StringComparer.OrdinalIgnoreCase) ? "Workbooks and add-ins" : "Workbooks";
    return $"{what} ({Patterns(targets)})|{Patterns(targets)}|All files (*.*)|*.*";
  }

  /// <summary>
  /// The new file picker's filter. vbadoc's kinds are offered one at a time, as Excel's Save As does, so the one chosen
  /// is the extension a name typed without one gets.
  /// </summary>
  public static string NewFileFilter(Tool tool) => tool == Tool.Pqdoc
    ? $"Workbooks ({Patterns(NewFiles(tool))})|{Patterns(NewFiles(tool))}"
    : "Macro-enabled workbooks (*.xlsm)|*.xlsm|Excel add-ins (*.xlam)|*.xlam";

  private static string Patterns(IEnumerable<string> extensions) => string.Join(';', extensions.Select(e => "*" + e));
}
