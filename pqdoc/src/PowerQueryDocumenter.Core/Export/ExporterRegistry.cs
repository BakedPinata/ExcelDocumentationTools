using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>
/// The available exporters, keyed by format. Adding a format means adding an <see cref="IExporter"/>
/// and registering it here; the analyser is unaffected (NFR-ARCH-1).
/// </summary>
public sealed class ExporterRegistry
{
  private readonly Dictionary<OutputFormat, IExporter> exporters = [];

  /// <summary>Creates a registry from a set of exporters. Later exporters replace earlier ones for the same format.</summary>
  public ExporterRegistry(IEnumerable<IExporter> exporters)
  {
    ArgumentNullException.ThrowIfNull(exporters);
    foreach (var exporter in exporters)
    {
      this.exporters[exporter.Format] = exporter;
    }
  }

  /// <summary>The registered formats.</summary>
  public IReadOnlyCollection<OutputFormat> Formats => exporters.Keys;

  /// <summary>
  /// Creates the standard exporters. The PlantUML render exporters are only registered when a
  /// server is configured.
  /// </summary>
  public static ExporterRegistry CreateDefault(HttpClient httpClient, Uri? plantUmlServer, TimeSpan renderTimeout)
  {
    ArgumentNullException.ThrowIfNull(httpClient);

    var exporters = new List<IExporter>
    {
      new CsvExporter(),
      new PlantUmlSourceExporter(),
      new HtmlExporter(),
    };

    if (plantUmlServer is not null)
    {
      var client = new PlantUmlClient(httpClient, plantUmlServer, renderTimeout);
      exporters.Add(new PlantUmlRenderExporter(OutputFormat.Svg, client));
      exporters.Add(new PlantUmlRenderExporter(OutputFormat.Png, client));
      exporters.Add(new PlantUmlRenderExporter(OutputFormat.Pdf, client));
    }

    return new ExporterRegistry(exporters);
  }

  /// <summary>Returns the exporter for a format.</summary>
  /// <exception cref="InvalidOperationException">No exporter is registered for the format.</exception>
  public IExporter Get(OutputFormat format) =>
    exporters.TryGetValue(format, out var exporter)
      ? exporter
      : throw new InvalidOperationException($"No exporter is registered for the '{format.CliName()}' format.");
}
