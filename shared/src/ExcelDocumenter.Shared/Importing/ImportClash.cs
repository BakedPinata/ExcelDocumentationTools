using ExcelDocumenter.Shared.Files;
using Serilog;

namespace ExcelDocumenter.Shared.Importing;

/// <summary>What to do with an item the target already has (<c>--on-clash</c>). It's about the items, not the file written.</summary>
public enum OnClashBehaviour
{
  /// <summary>Ask about each one when interactive; stop when not.</summary>
  Prompt,

  /// <summary>Replace the target's.</summary>
  Overwrite,

  /// <summary>Keep the target's.</summary>
  Skip,
}

/// <summary>An answer to the "already in the target" prompt.</summary>
public enum ClashAnswer
{
  /// <summary>Keep the target's.</summary>
  Skip,

  /// <summary>Replace the target's.</summary>
  Overwrite,

  /// <summary>Keep the target's, and every other one's without asking again.</summary>
  SkipAll,

  /// <summary>Replace the target's, and every other one's without asking again.</summary>
  OverwriteAll,

  /// <summary>Stop with nothing written.</summary>
  Quit,
}

/// <summary>An item the target already has, which isn't the same. An identical one is no clash: it's left alone.</summary>
/// <param name="Item">The item's name.</param>
/// <param name="Problem">What's in the way, as a sentence.</param>
/// <param name="IfOverwritten">What Overwrite would mean, as a sentence, or null if the target's can't be replaced.</param>
/// <param name="IfSkipped">What Skip would mean, as a sentence.</param>
public sealed record ImportClash(string Item, string Problem, string? IfOverwritten, string IfSkipped)
{
  /// <summary>Whether the target's can be replaced. A sheet's module can't be, as it belongs to the sheet.</summary>
  public bool CanOverwrite => IfOverwritten is not null;

  /// <summary>What Overwrite means.</summary>
  /// <param name="usedInTarget">What else in the target uses the item.</param>
  public static string Overwriting(IReadOnlyCollection<string> usedInTarget)
  {
    ArgumentNullException.ThrowIfNull(usedInTarget);
    return usedInTarget.Count == 0
      ? "Replaces the target's version, which nothing else there uses."
      : $"Replaces the target's version, used there by {Names(usedInTarget)}.";
  }

  /// <summary>What Skip means.</summary>
  /// <param name="comingUsers">What's coming that uses the item.</param>
  public static string Skipping(IReadOnlyCollection<string> comingUsers)
  {
    ArgumentNullException.ThrowIfNull(comingUsers);
    return comingUsers.Count == 0
      ? "Keeps the target's version."
      : $"Keeps the target's version, which may differ, for {Names(comingUsers)} to use.";
  }

  /// <summary>Names as a list in a sentence, in name order, ignoring case.</summary>
  public static string Names(IEnumerable<string> names) => string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase));
}

/// <summary>How a choice of items compares with what a target has.</summary>
/// <param name="New">The items it doesn't have.</param>
/// <param name="Identical">The items it has, word for word, which are left alone without asking.</param>
/// <param name="Clashes">The names it has for something else.</param>
public sealed record TargetComparison(IReadOnlyList<string> New, IReadOnlyList<string> Identical, IReadOnlyList<ImportClash> Clashes);

/// <summary>The decision for every clash.</summary>
/// <param name="Overwrite">The items to replace.</param>
/// <param name="Skip">The items to leave as the target has them.</param>
/// <param name="Aborted">True when nothing may be written (exit code 4).</param>
public sealed record ClashResolution(IReadOnlyList<string> Overwrite, IReadOnlyList<string> Skip, bool Aborted);

/// <summary>
/// Applies <c>--on-clash</c>. Every answer comes before anything is written. <c>--force</c> plays no part: an unattended
/// run says what it wants done with each item, because replacing the wrong one is worse than stopping.
/// </summary>
public sealed class ClashResolver(ILogger logger, IUserInteraction interaction)
{
  private const string HowToProceed = "Re-run with --on-clash overwrite or --on-clash skip to say what to do with them.";

  /// <summary>Decides what happens to each clash.</summary>
  public ClashResolution Resolve(IReadOnlyList<ImportClash> clashes, OnClashBehaviour behaviour)
  {
    ArgumentNullException.ThrowIfNull(clashes);
    if (clashes.Count == 0)
    {
      return new ClashResolution([], [], Aborted: false);
    }

    switch (behaviour)
    {
      case OnClashBehaviour.Skip:
        return new ClashResolution([], [.. clashes.Select(c => c.Item)], Aborted: false);

      case OnClashBehaviour.Overwrite:
        var stuck = clashes.Where(c => !c.CanOverwrite).ToList();
        foreach (var clash in stuck)
        {
          logger.Error("{Problem:l}", clash.Problem);
        }

        if (stuck.Count > 0)
        {
          logger.Error("Nothing was written. Rename it in the source, or use --on-clash skip to leave it out");
          return Abort();
        }

        return new ClashResolution([.. clashes.Select(c => c.Item)], [], Aborted: false);

      case OnClashBehaviour.Prompt when interaction.IsInteractive:
        return Prompt(clashes);

      default:
        foreach (var clash in clashes)
        {
          logger.Error("{Problem:l}", clash.Problem);
        }

        logger.Error("This session is not interactive, so the tool cannot ask. Nothing was written. " + HowToProceed);
        return Abort();
    }
  }

  private ClashResolution Prompt(IReadOnlyList<ImportClash> clashes)
  {
    var overwrite = new List<string>();
    var skip = new List<string>();
    ClashAnswer? forAll = null;

    foreach (var clash in clashes)
    {
      // Overwrite All can't answer for one that can't be replaced, so that one is still asked about.
      var answer = forAll == ClashAnswer.SkipAll || (forAll == ClashAnswer.OverwriteAll && clash.CanOverwrite)
        ? forAll.Value
        : interaction.AskClash(clash);

      switch (answer)
      {
        case ClashAnswer.Skip:
          skip.Add(clash.Item);
          break;
        case ClashAnswer.SkipAll:
          forAll = answer;
          skip.Add(clash.Item);
          break;
        case ClashAnswer.Overwrite when clash.CanOverwrite:
          overwrite.Add(clash.Item);
          break;
        case ClashAnswer.OverwriteAll when clash.CanOverwrite:
          forAll = answer;
          overwrite.Add(clash.Item);
          break;
        default:
          logger.Error("Stopped at your request. Nothing was written");
          return Abort();
      }
    }

    return new ClashResolution(overwrite, skip, Aborted: false);
  }

  private static ClashResolution Abort() => new([], [], Aborted: true);
}
