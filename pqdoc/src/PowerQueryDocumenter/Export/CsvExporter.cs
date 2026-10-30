using System.Text;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Model;

namespace PowerQueryDocumenter.Export;

/// <summary>
/// Writes the dependency CSV (FR-CSV-1 to FR-CSV-3): UTF-8 with a byte order mark, every value
/// quoted with <c>""</c> escaping, one row per (query, dependent) pair, and queries with no
/// dependents written with an empty second column. Rows are ordered case-insensitively by query,
/// then by dependent.
/// </summary>
public sealed class CsvExporter : IExporter
{
  /// <summary>Header of the first column: the upstream query being referenced.</summary>
  public const string QueryNameHeader = "Query Name";

  /// <summary>Header of the second column: the downstream query that references it.</summary>
  public const string ReferencedByHeader = "Referenced By Query Name";

  /// <summary>The complete header row.</summary>
  public const string HeaderRow = QueryNameHeader + "," + ReferencedByHeader;

  /// <inheritdoc />
  public OutputFormat Format => OutputFormat.Csv;

  /// <inheritdoc />
  public string FileExtension => Format.FileExtension();

  /// <inheritdoc />
  public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(output);

    // Encoding.UTF8 writes a BOM, which Excel relies on.
    var writer = new StreamWriter(output, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
    await using (writer.ConfigureAwait(false))
    {
      await writer.WriteLineAsync(HeaderRow.AsMemory(), cancellationToken).ConfigureAwait(false);

      foreach (var node in graph.Nodes)
      {
        var dependents = graph.GetDownstream(node.Name);
        if (dependents.Count == 0)
        {
          await writer.WriteLineAsync($"{Quote(node.Name)},".AsMemory(), cancellationToken).ConfigureAwait(false);
          continue;
        }

        foreach (var dependent in dependents)
        {
          await writer.WriteLineAsync($"{Quote(node.Name)},{Quote(dependent)}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }
      }
    }
  }

  /// <summary>Quotes a CSV value, doubling any embedded quotes.</summary>
  public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
