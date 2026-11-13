using ExcelDocumenter.Desktop.Running;
using CodeDocumenter.Shared.Importing;
using CodeDocumenter.Shared.Workbooks;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Importing;

/// <summary>What a source has to take, read by the tool that would take it, for the Import tab's list.</summary>
internal static class ImportItems
{
  /// <summary>Reads the list off the window's thread. It can take a moment for a big workbook.</summary>
  /// <exception cref="ExtractionException">The source can't be read, or has nothing of the kind.</exception>
  public static Task<IReadOnlyList<ImportableItem>> ReadAsync(Tool tool, string source, CancellationToken cancellationToken) => tool switch
  {
    Tool.Vbadoc => Task.Run(() => VbaCli.ImportSource.Items(source), cancellationToken),
    Tool.Pqdoc => Task.Run(() => PqCli.ImportSource.ItemsAsync(source, cancellationToken), cancellationToken),
    _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
  };

  /// <summary>Why a source couldn't be read, to show in place of the list, or null for something unexpected.</summary>
  public static string? WhyUnreadable(Exception exception) => exception switch
  {
    ExtractionException or IOException or UnauthorizedAccessException => exception.Message,
    _ => null,
  };

  /// <summary>A line under the list, saying what there is and what's ticked.</summary>
  public static string Summary(IReadOnlyCollection<ImportableItem> items, int ticked)
  {
    ArgumentNullException.ThrowIfNull(items);
    var canTake = items.Count(item => item.CanTake);
    var cannot = items.Count - canTake;
    var stuck = cannot == 0 ? "" : $", and {cannot} that can't come";
    return $"{canTake} to choose from{stuck}. {ticked} ticked. What they use comes with them: Preview says what.";
  }
}
