using ExcelDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Application;
using Serilog.Core;

namespace PowerQueryDocumenter.Cli;

/// <summary>What a workbook or folder has that <c>pqdoc import</c> could take, for the desktop app to offer as a list.</summary>
public static class ImportSource
{
  /// <summary>
  /// Every query in a workbook or a folder of .m files, by the rules <c>import</c> uses. What reading it finds to warn
  /// about isn't logged here: the import says it when it runs.
  /// </summary>
  /// <exception cref="ExcelDocumenter.Shared.Workbooks.ExtractionException">The workbook can't be read, or has no queries.</exception>
  public static Task<IReadOnlyList<ImportableItem>> ItemsAsync(string source, CancellationToken cancellationToken) =>
    ImportApp.ItemsAsync(source, Logger.None, cancellationToken);
}
