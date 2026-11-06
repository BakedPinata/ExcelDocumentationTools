using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Importing;
using ExcelDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Importing;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Core.Output;
using Serilog;

namespace PowerQueryDocumenter.Core.Application;

/// <summary>
/// Takes chosen queries, with what they use, out of a workbook or a folder of <c>.m</c> files: says what that comes to,
/// and writes it as a bundle if asked. What it reads comes first, so a choice that can't be met changes nothing on disk.
/// </summary>
public sealed class ImportApp(ILogger logger, AppServices services)
{
  /// <summary>Runs the import and returns the process exit code.</summary>
  public async Task<int> RunAsync(ImportRequest request, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    var run = new ImportRun(logger, services.Interaction, "pqdoc");
    var name = Path.GetFileName(request.Source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    DependencyGraph graph;
    Dictionary<string, QueryBundleFile> files;
    Dictionary<string, string> sources;
    IReadOnlyList<string> items;
    try
    {
      (graph, files, sources) = await ReadAsync(request.Source, cancellationToken).ConfigureAwait(false);
      (items, _) = run.ReadChoice(request, name);
    }
    catch (ExtractionException ex)
    {
      logger.Error("{Message}", ex.Message);
      logger.Debug(ex, "Input failure detail");
      return ExitCodes.InputError;
    }

    var import = new QueryImport(graph, sources);
    var unknown = items.Where(item => import.Find(item) is null).ToList();
    if (unknown.Count > 0)
    {
      logger.Error("{Source:l} has no query called {Items:l}", name, string.Join(", ", unknown));
      return ExitCodes.InputError;
    }

    var plan = import.Plan(items.Select(item => import.Find(item)!));
    run.Report(plan);
    if (request.Bundle is null)
    {
      logger.Information("Nothing was written. Add --bundle <folder> to write them out");
      return ExitCodes.Success;
    }

    return run.WriteBundle(
      request,
      QueryBundle.Files(plan, files),
      QueryBundle.Readme(name, plan, graph, files),
      [QueryFileNames.Extension],
      QueryFileExporter.GitAttributesContent);
  }

  // The queries of a workbook, or of a folder of .m files, with each one's file and text.
  private async Task<(DependencyGraph Graph, Dictionary<string, QueryBundleFile> Files, Dictionary<string, string> Sources)> ReadAsync(
    string source,
    CancellationToken cancellationToken)
  {
    var files = new Dictionary<string, QueryBundleFile>(DependencyGraph.NameComparer);
    var sources = new Dictionary<string, string>(DependencyGraph.NameComparer);
    if (Directory.Exists(source))
    {
      var analysis = await new DependencyAnalyser(logger).AnalyseFolderAsync(source, cancellationToken).ConfigureAwait(false);
      foreach (var (query, path) in analysis.Files ?? new Dictionary<string, string>())
      {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        files[query] = new QueryBundleFile(Path.GetFileName(path), bytes);
        sources[query] = System.Text.Encoding.UTF8.GetString(bytes);
      }

      return (analysis.Graph, files, sources);
    }

    var queries = WorkbookReader.Read(WorkbookFile.ReadAllBytes(source), logger);
    foreach (var query in queries)
    {
      files[query.Name] = new QueryBundleFile(QueryFileNames.FileName(query.Name), QueryFileText.Bytes(query.Expression));
      sources[query.Name] = query.Expression;
    }

    return (new DependencyAnalyser(logger).Analyse(queries.Select(q => new QuerySource(q.Name, q.Expression))), files, sources);
  }
}
