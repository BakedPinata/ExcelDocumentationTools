using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Model;

namespace PowerQueryDocumenter.Export;

/// <summary>
/// Renders the diagram to SVG, PNG or PDF through a PlantUML server (FR-PUML-4 to FR-PUML-8).
/// The PlantUML source is generated in memory; requesting a rendered format does not write a
/// .puml file (FR-PUML-7).
/// </summary>
public sealed class PlantUmlRenderExporter : IExporter
{
  private readonly PlantUmlClient client;

  /// <param name="format">svg, png or pdf.</param>
  /// <param name="client">The PlantUML server client.</param>
  public PlantUmlRenderExporter(OutputFormat format, PlantUmlClient client)
  {
    ArgumentNullException.ThrowIfNull(client);
    if (!format.IsRendered())
    {
      throw new ArgumentOutOfRangeException(nameof(format), format, "Only svg, png and pdf are rendered by a PlantUML server.");
    }

    Format = format;
    this.client = client;
  }

  /// <inheritdoc />
  public OutputFormat Format { get; }

  /// <inheritdoc />
  public string FileExtension => Format.FileExtension();

  /// <inheritdoc />
  public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(output);
    var source = PlantUmlDiagramBuilder.Build(graph, PlantUmlSourceExporter.Title(context));
    var rendered = await client.RenderAsync(source, Format, cancellationToken).ConfigureAwait(false);
    await output.WriteAsync(rendered, cancellationToken).ConfigureAwait(false);
  }
}
