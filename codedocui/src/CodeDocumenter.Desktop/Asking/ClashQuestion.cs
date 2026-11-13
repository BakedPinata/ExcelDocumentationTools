using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Desktop.Asking;

/// <summary>
/// The console's "[s]kip / [o]verwrite / [sa] skip all / [oa] overwrite all / [q]uit" as a dialog's words: what's in the
/// way, and what Skip and Overwrite would each mean. Overwrite isn't offered for what can't be replaced, such as a
/// sheet's module, as the console doesn't offer it.
/// </summary>
internal static class ClashQuestion
{
  /// <summary>The answers, in the order the dialog shows them.</summary>
  public static IReadOnlyList<(string Label, ClashAnswer Answer)> Answers(ImportClash clash)
  {
    ArgumentNullException.ThrowIfNull(clash);
    return clash.CanOverwrite
      ?
      [
        ("Skip", ClashAnswer.Skip),
        ("Overwrite", ClashAnswer.Overwrite),
        ("Skip all", ClashAnswer.SkipAll),
        ("Overwrite all", ClashAnswer.OverwriteAll),
        ("Quit", ClashAnswer.Quit),
      ]
      : [("Skip", ClashAnswer.Skip), ("Skip all", ClashAnswer.SkipAll), ("Quit", ClashAnswer.Quit)];
  }

  /// <summary>The answer Enter gives: Skip, which keeps what the target has.</summary>
  public static ClashAnswer Default => ClashAnswer.Skip;

  /// <summary>The answer given when the dialog is closed without one, as end of input is at the console.</summary>
  public static ClashAnswer Closed => ClashAnswer.Quit;

  public static string Heading(ImportClash clash) => $"{clash.Item} is already in the target";

  public static string Text(ImportClash clash)
  {
    ArgumentNullException.ThrowIfNull(clash);
    var overwrite = clash.IfOverwritten is null ? "" : $"Overwrite: {clash.IfOverwritten}\n";
    var all = clash.CanOverwrite ? "Skip all and Overwrite all answer the same for any others" : "Skip all answers the same for any others";
    return $"{clash.Problem}\n\n{overwrite}Skip: {clash.IfSkipped}\n\n{all}. Quit stops with nothing written.";
  }
}
