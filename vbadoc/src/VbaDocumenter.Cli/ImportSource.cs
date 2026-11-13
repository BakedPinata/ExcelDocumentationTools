using CodeDocumenter.Shared.Importing;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Cli;

/// <summary>What a workbook has that <c>vbadoc import</c> could take, for the desktop app to offer as a list.</summary>
public static class ImportSource
{
  /// <summary>Every module in the workbook, with why it can't be taken if it can't, by the rules <c>import</c> uses.</summary>
  /// <exception cref="CodeDocumenter.Shared.Workbooks.ExtractionException">The workbook can't be read, or has no VBA project.</exception>
  public static IReadOnlyList<ImportableItem> Items(string workbook)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(workbook);
    var project = WorkbookReader.Read(Path.GetFullPath(workbook));
    return new ModuleImport(DependencyAnalyser.Analyse(ProjectParser.Parse(project))).Items();
  }
}
