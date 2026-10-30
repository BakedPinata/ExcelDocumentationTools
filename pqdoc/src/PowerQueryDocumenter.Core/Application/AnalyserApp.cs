using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
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
/// Runs one analysis from validated options: analyse the input folder, decide which outputs to
/// write, run each exporter in isolation, then log a summary. Returns the process exit code.
/// </summary>
public sealed class AnalyserApp(ILogger logger, AppServices services)
{
  /// <summary>Runs the analysis and exports. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
  public async Task<int> RunAsync(AnalyserOptions options, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(options);

    if (!Directory.Exists(options.InputFolder))
    {
      logger.Error("The input folder {Folder} does not exist", options.InputFolder);
      return ExitCodes.InputError;
    }

    AnalysisResult analysis;
    try
    {
      logger.Information("Analysing queries in {Folder}", options.InputFolder);
      analysis = await new DependencyAnalyser(logger)
        .AnalyseFolderAsync(options.InputFolder, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
    {
      logger.Error("Cannot read the input folder {Folder}: {Reason}", options.InputFolder, ex.Message);
      logger.Debug(ex, "Input folder failure detail");
      return ExitCodes.InputError;
    }

    if (analysis.QueryFileCount == 0)
    {
      logger.Error(
        "No .m files were found in {Folder}. Export each Power Query query to its own .m file in that folder (subfolders are not scanned)",
        options.InputFolder);
      return ExitCodes.InputError;
    }

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

    var outcomes = resolution.Skipped
      .Select(o => new ExportOutcome(o.Format, o.Path, ExportStatus.Skipped))
      .ToList();

    if (resolution.ToWrite.Count > 0)
    {
      outcomes.AddRange(await WriteOutputsAsync(graph, options, registry, resolution.ToWrite, cancellationToken).ConfigureAwait(false));
    }

    LogSummary(graph, analysis, outcomes, options.OutputFolder);

    if (outcomes.Any(o => o.Status == ExportStatus.Failed))
    {
      return ExitCodes.ExportFailed;
    }

    return analysis.UnreadableFiles.Count > 0 ? ExitCodes.InputError : ExitCodes.Success;
  }

  private async Task<List<ExportOutcome>> WriteOutputsAsync(
    DependencyGraph graph,
    AnalyserOptions options,
    ExporterRegistry registry,
    IReadOnlyList<PlannedOutput> outputs,
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

    var context = new ExportContext(options.OutputName, services.TimeProvider.GetLocalNow());

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
