using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Model;

using PowerQueryDocumenter.Core.Extraction;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>Information shared by every exporter during a run.</summary>
/// <param name="Title">What the outputs document, used in their titles: the workbook's file name or the folder's name,
/// as vbadoc titles its page with the workbook's file name.</param>
/// <param name="WorkbookSaved">When the workbook was last saved, from its document properties, for the page's summary;
/// null for a folder of .m files or a workbook without the property. Never when the page was made, so the same input
/// always gives the same outputs.</param>
/// <param name="Sources">The queries' .m files, for the page's source view, or null when there are none.</param>
/// <param name="EmbedSource">Whether the page holds the files' text (<c>--embed-source</c>) or loads them from a folder.</param>
/// <param name="Details">Each query's kind and group, for the page's explorer, or null when not known.</param>
/// <param name="ViewsJson">Saved views to build into the page, from <c>pqdoc-views.json</c> in the output folder
/// (FR-HTML-23), or null for none.</param>
public sealed record ExportContext(
  string Title,
  DateTimeOffset? WorkbookSaved,
  IReadOnlyList<QueryFile>? Sources = null,
  bool EmbedSource = false,
  IReadOnlyList<QueryDetails>? Details = null,
  string? ViewsJson = null);

/// <summary>What the page says about a query besides its dependencies (FR-HTML-11).</summary>
/// <param name="Query">The query's name.</param>
/// <param name="IsFunction">Whether it's a function: from the workbook's metadata, or its code for a folder.</param>
/// <param name="Group">Its Excel query group, or null.</param>
public sealed record QueryDetails(string Query, bool IsFunction, QueryGroup? Group);

/// <summary>
/// Produces one output format from a <see cref="DependencyGraph"/> (NFR-ARCH-1). Exporters work
/// only from the graph and never re-read the input files (FR-ANA-7). They write to a stream; the
/// caller owns the destination file, which keeps writes atomic and exporters easy to test.
/// </summary>
public interface IExporter
{
  /// <summary>The format this exporter produces.</summary>
  OutputFormat Format { get; }

  /// <summary>The file extension, including the leading dot.</summary>
  string FileExtension { get; }

  /// <summary>Writes the export to <paramref name="output"/>.</summary>
  Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken);
}
