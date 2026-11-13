using CodeDocumenter.Shared.Files;

namespace ExcelDocumenter.Desktop.Asking;

/// <summary>
/// The console's "already exists. Overwrite? [y]es / [n]o / [a]ll / [q]uit" as a dialog's words. The tools only ask
/// about a file this run would change, and ask before writing anything.
/// </summary>
internal static class OverwriteQuestion
{
  /// <summary>The answers, in the order the dialog shows them.</summary>
  public static IReadOnlyList<(string Label, OverwriteAnswer Answer)> Answers { get; } =
  [
    ("Overwrite", OverwriteAnswer.Yes),
    ("Keep it", OverwriteAnswer.No),
    ("Overwrite all", OverwriteAnswer.All),
    ("Quit", OverwriteAnswer.Quit),
  ];

  /// <summary>The answer given when the dialog is closed without one, as end of input is at the console.</summary>
  public static OverwriteAnswer Closed => OverwriteAnswer.Quit;

  public static string Heading(string path) => $"{Path.GetFileName(path)} is already there";

  public static string Text(string path) =>
    $"It's in {Path.GetDirectoryName(path)}, and this run would change it.\n\n"
    + "Keep it leaves it as it is. Overwrite all overwrites this and any others without asking again. "
    + "Quit stops before anything is written.";
}
