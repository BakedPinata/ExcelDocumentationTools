using System.Text;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Model;

namespace PowerQueryDocumenter.Export;

/// <summary>Writes the PlantUML source file (FR-PUML-1), UTF-8 without a byte order mark.</summary>
public sealed class PlantUmlSourceExporter : IExporter
{
  /// <summary>UTF-8 without a byte order mark.</summary>
  public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

  /// <inheritdoc />
  public OutputFormat Format => OutputFormat.Puml;

  /// <inheritdoc />
  public string FileExtension => Format.FileExtension();

  /// <summary>The diagram title used for an export.</summary>
  public static string Title(ExportContext context)
  {
    ArgumentNullException.ThrowIfNull(context);
    return $"Power Query dependencies: {context.OutputName}";
  }

  /// <inheritdoc />
  public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(output);
    var source = PlantUmlDiagramBuilder.Build(graph, Title(context));
    await output.WriteAsync(Utf8NoBom.GetBytes(source), cancellationToken).ConfigureAwait(false);
  }
}
