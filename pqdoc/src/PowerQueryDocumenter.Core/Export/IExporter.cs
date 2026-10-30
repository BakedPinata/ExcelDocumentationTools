using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>Information shared by every exporter during a run.</summary>
/// <param name="OutputName">The output base name (used in titles).</param>
/// <param name="GeneratedAt">When the run started (used in the HTML heading).</param>
public sealed record ExportContext(string OutputName, DateTimeOffset GeneratedAt);

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
