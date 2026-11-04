using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Core.Output;
using Serilog;

namespace PowerQueryDocumenter.Core.Application;

/// <summary>What happened to one planned output.</summary>
public enum ExportStatus
{
  /// <summary>The file was written.</summary>
  Written,

  /// <summary>The file already existed and was left alone.</summary>
  Skipped,

  /// <summary>Producing or writing the file failed.</summary>
  Failed,
}

/// <summary>The outcome for one planned output.</summary>
/// <param name="Format">The export format.</param>
/// <param name="Path">The destination path.</param>
/// <param name="Status">Written, skipped or failed.</param>
public sealed record ExportOutcome(OutputFormat Format, string Path, ExportStatus Status);

/// <summary>
/// Runs one analysis from validated options: read the queries from the workbook or folder, decide
/// which outputs to write, run each exporter in isolation, then log a summary. Returns the process
/// exit code.
/// </summary>
public sealed class AnalyserApp(ILogger logger, AppServices services)
{
  /// <summary>Runs the analysis and exports. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
  public async Task<int> RunAsync(AnalyserOptions options, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(options);

    IReadOnlyList<WorkbookQuery>? queries = null;
    AnalysisResult? analysis;
    if (options.InputIsWorkbook)
    {
      (analysis, queries) = AnalyseWorkbook(options.Input);
    }
    else
    {
      analysis = await AnalyseFolderAsync(options.Input, cancellationToken).ConfigureAwait(false);
    }

    if (analysis is null)
    {
      return ExitCodes.InputError;
    }

    cancellationToken.ThrowIfCancellationRequested();
    var graph = analysis.Graph;
    foreach (var cycle in graph.Cycles)
    {
      logger.Warning(
        "Circular dependency between {Count} queries: {Cycle}",
        cycle.Members.Count,
        cycle.Describe());
    }

    var registry = services.CreateExporters?.Invoke(options)
      ?? ExporterRegistry.CreateDefault(services.HttpClient, options.PlantUmlServer, options.RenderTimeout);

    var planned = options.Formats.Select(f => new PlannedOutput(f, options.GetOutputPath(f))).ToArray();
    var resolution = new OverwriteResolver(logger, services.Interaction).Resolve(planned, options.OnExists);
    if (resolution.Aborted)
    {
      return ExitCodes.OutputRefused;
    }

    // A workbook's queries go into .m files beside the outputs (FR-QF-1). A clash is found before anything is
    // written, and stops the run; a failure to write stops only the .m files.
    cancellationToken.ThrowIfCancellationRequested();
    var queryFilesWritten = true;
    if (queries is not null)
    {
      try
      {
        LogQueryFiles(QueryFileExporter.Export(queries, options.OutputFolder), options.OutputFolder);
      }
      catch (ExtractionException ex)
      {
        logger.Error("{Message}", ex.Message);
        return ExitCodes.InputError;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        logger.Error("Could not write the query files in {Folder}: {Reason}", options.OutputFolder, ErrorDescriber.DescribeOutputFailure(ex));
        logger.Debug(ex, "Query file failure detail");
        queryFilesWritten = false;
      }
    }

    var outcomes = resolution.Skipped
      .Select(o => new ExportOutcome(o.Format, o.Path, ExportStatus.Skipped))
      .ToList();

    if (resolution.ToWrite.Count > 0)
    {
      var sources = SourceFiles(queries, analysis);
      outcomes.AddRange(await WriteOutputsAsync(graph, options, registry, resolution.ToWrite, sources, Details(queries, sources), cancellationToken).ConfigureAwait(false));
    }

    // Only once this run's outputs are safely written, so there's always a page.
    var failed = !queryFilesWritten || outcomes.Any(o => o.Status == ExportStatus.Failed);
    if (!failed)
    {
      HandleOlderOutputs(options);
    }

    LogSummary(graph, analysis, outcomes, options.OutputFolder);

    if (failed)
    {
      return ExitCodes.ExportFailed;
    }

    return analysis.UnreadableFiles.Count > 0 ? ExitCodes.InputError : ExitCodes.Success;
  }

  /// <summary>
  /// Outputs pqdoc wrote earlier under another name (FR-OUT-2). A workbook's output folder belongs to pqdoc, so they're
  /// deleted there, and an old page can't pass for the current one. A folder of .m files belongs to the user, so
  /// there they're only mentioned.
  /// </summary>
  private void HandleOlderOutputs(AnalyserOptions options)
  {
    var older = OlderOutputs.Find(options.OutputFolder, options.OutputName);
    if (older.Count == 0)
    {
      return;
    }

    if (!options.InputIsWorkbook)
    {
      logger.Information(
        "Older outputs from pqdoc are still in {Folder}, under another name: {Files}. Delete them if they're no longer wanted",
        options.OutputFolder,
        older.Select(Path.GetFileName));
      return;
    }

    foreach (var path in older)
    {
      try
      {
        File.Delete(path);
        logger.Information("Deleted {File}, an older output from pqdoc", Path.GetFileName(path));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        logger.Warning("Could not delete {File}, an older output from pqdoc: {Reason}", path, ex.Message);
      }
    }
  }

  private void LogQueryFiles(SourceFolderResult result, string folder)
  {
    logger.Information(
      "Query files in {Folder}: {Written} written, {Unchanged} unchanged, {Deleted} deleted",
      folder,
      result.Written.Count,
      result.Unchanged.Count,
      result.Deleted.Count);
    foreach (var name in result.Deleted)
    {
      logger.Information("  deleted {File} (no longer in the workbook)", name);
    }

    if (result.CreatedGitAttributes)
    {
      logger.Information("  created .gitattributes");
    }
  }

  /// <summary>
  /// Reads the queries straight from a workbook (FR-IN-2), and returns them with their analysis. Both are null after
  /// logging why, if it can't.
  /// </summary>
  private (AnalysisResult? Analysis, IReadOnlyList<WorkbookQuery>? Queries) AnalyseWorkbook(string workbook)
  {
    logger.Information("Reading queries from {Workbook}", workbook);
    IReadOnlyList<WorkbookQuery> queries;
    try
    {
      queries = WorkbookReader.Read(workbook, logger);
    }
    catch (ExtractionException ex)
    {
      logger.Error("{Message}", ex.Message);
      logger.Debug(ex, "Workbook failure detail");
      return (null, null);
    }

    logger.Information("  {Count} queries", queries.Count);
    var graph = new DependencyAnalyser(logger).Analyse(queries.Select(q => new QuerySource(q.Name, q.Expression)));
    return (new AnalysisResult(graph, queries.Count, [], []), queries);
  }

  /// <summary>Reads every .m file in a folder. Returns null after logging why, if it can't.</summary>
  private async Task<AnalysisResult?> AnalyseFolderAsync(string folder, CancellationToken cancellationToken)
  {
    if (!Directory.Exists(folder))
    {
      logger.Error("The input {Input} does not exist", folder);
      return null;
    }

    AnalysisResult analysis;
    try
    {
      logger.Information("Analysing queries in {Folder}", folder);
      analysis = await new DependencyAnalyser(logger).AnalyseFolderAsync(folder, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
    {
      logger.Error("Cannot read the input folder {Folder}: {Reason}", folder, ex.Message);
      logger.Debug(ex, "Input folder failure detail");
      return null;
    }

    if (analysis.QueryFileCount == 0)
    {
      logger.Error(
        "No .m files were found in {Folder}. Export each Power Query query to its own .m file in that folder (subfolders are not scanned), or run pqdoc on the workbook itself",
        folder);
      return null;
    }

    return analysis;
  }

  /// <summary>
  /// Each query's .m file, for the page (FR-SRC-2): from a workbook, the files just written, made from the same
  /// bytes; from a folder, the files the analysis read. A file that can't be read again is left out, and the page says
  /// it has no source.
  /// </summary>
  private List<QueryFile> SourceFiles(IReadOnlyList<WorkbookQuery>? queries, AnalysisResult analysis)
  {
    if (queries is not null)
    {
      return queries
        .Select(q => QueryFile.FromBytes(q.Name, QueryFileNames.FileName(q.Name), QueryFileText.Bytes(q.Expression)))
        .ToList();
    }

    var files = new List<QueryFile>();
    foreach (var (name, path) in analysis.Files ?? new Dictionary<string, string>())
    {
      try
      {
        files.Add(QueryFile.FromBytes(name, Path.GetFileName(path), File.ReadAllBytes(path)));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        logger.Debug(ex, "Could not read {File} again for the page's source view", path);
      }
    }

    return files;
  }

  /// <summary>
  /// Each query's kind and group, for the page's explorer (FR-HTML-11). A workbook's metadata says both; for a folder,
  /// whether a query is a function comes from its code, and there are no groups.
  /// </summary>
  private static List<QueryDetails> Details(IReadOnlyList<WorkbookQuery>? queries, IReadOnlyList<QueryFile> sources) =>
    queries is not null
      ? queries
        .Select(q => new QueryDetails(
          q.Name,
          q.ResultType is not null ? q.ResultType == "Function" : FunctionDetector.IsFunction(q.Expression),
          q.Group))
        .ToList()
      : sources.Select(f => new QueryDetails(f.Query, FunctionDetector.IsFunction(f.Text), null)).ToList();

  private async Task<List<ExportOutcome>> WriteOutputsAsync(
    DependencyGraph graph,
    AnalyserOptions options,
    ExporterRegistry registry,
    IReadOnlyList<PlannedOutput> outputs,
    IReadOnlyList<QueryFile> sources,
    IReadOnlyList<QueryDetails> details,
    CancellationToken cancellationToken)
  {
    var outcomes = new List<ExportOutcome>();

    try
    {
      Directory.CreateDirectory(options.OutputFolder);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
      logger.Error(
        "Cannot create the output folder {Folder}: {Reason}",
        options.OutputFolder,
        ErrorDescriber.DescribeOutputFailure(ex));
      logger.Debug(ex, "Output folder failure detail");
      outcomes.AddRange(outputs.Select(o => new ExportOutcome(o.Format, o.Path, ExportStatus.Failed)));
      return outcomes;
    }

    var context = new ExportContext(options.Title, services.TimeProvider.GetLocalNow(), sources, options.EmbedSource, details);

    // Each export is isolated: one failing never stops the others (FR-ERR-2).
    foreach (var output in outputs)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        var exporter = registry.Get(output.Format);
        logger.Debug("Producing {Format} at {Path}", output.Format.CliName(), output.Path);
        await AtomicFileWriter
          .WriteAsync(output.Path, (stream, token) => exporter.ExportAsync(graph, context, stream, token), cancellationToken)
          .ConfigureAwait(false);
        logger.Information("Wrote {Format} to {Path}", output.Format.CliName(), output.Path);
        outcomes.Add(new ExportOutcome(output.Format, output.Path, ExportStatus.Written));
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        logger.Error(
          "Could not produce {Format} ({Path}): {Reason}",
          output.Format.CliName(),
          output.Path,
          ErrorDescriber.DescribeOutputFailure(ex));
        logger.Debug(ex, "{Format} export failure detail", output.Format.CliName());
        outcomes.Add(new ExportOutcome(output.Format, output.Path, ExportStatus.Failed));
      }
    }

    return outcomes;
  }

  private void LogSummary(DependencyGraph graph, AnalysisResult analysis, List<ExportOutcome> outcomes, string outputFolder)
  {
    logger.Information(
      "Found {Queries} queries and {Edges} dependencies (roots: {Roots}, leaves: {Leaves}, isolated: {Isolated}, circular dependencies: {Cycles})",
      graph.Nodes.Count,
      graph.Edges.Count,
      graph.CountByRole(QueryRole.Root),
      graph.CountByRole(QueryRole.Leaf),
      graph.CountByRole(QueryRole.Isolated),
      graph.Cycles.Count);

    if (analysis.UnreadableFiles.Count > 0)
    {
      logger.Warning("{Count} query file(s) could not be read: {Files}", analysis.UnreadableFiles.Count, analysis.UnreadableFiles);
    }

    var written = FileNames(outcomes, ExportStatus.Written);
    var skipped = FileNames(outcomes, ExportStatus.Skipped);
    var failed = FileNames(outcomes, ExportStatus.Failed);

    if (skipped.Count > 0)
    {
      logger.Information("Skipped (already existed): {Files}", skipped);
    }

    if (failed.Count > 0)
    {
      logger.Error("Failed: {Files}", failed);
    }

    if (written.Count == 0)
    {
      // Never report success when nothing was produced (FR-ERR-3).
      logger.Warning("No output files were written");
    }
    else if (failed.Count > 0)
    {
      logger.Warning(
        "Finished with errors. Wrote {Written} of {Total} output files to {Folder}: {Files}",
        written.Count,
        outcomes.Count,
        outputFolder,
        written);
    }
    else
    {
      logger.Information("Done. Wrote {Count} file(s) to {Folder}: {Files}", written.Count, outputFolder, written);
    }
  }

  private static List<string> FileNames(IEnumerable<ExportOutcome> outcomes, ExportStatus status) =>
    outcomes.Where(o => o.Status == status).Select(o => Path.GetFileName(o.Path)).ToList();
}
