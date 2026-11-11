using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;
using ExcelDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Importing;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Core.Output;
using Serilog;

namespace PowerQueryDocumenter.Core.Application;

/// <summary>
/// Takes chosen queries, with what they use, out of a workbook or a folder of <c>.m</c> files: says what that comes to,
/// and writes it as a bundle or into a workbook if asked. Everything is read and every question answered before anything
/// is written, so a choice that can't be met, or an answer of no, changes nothing on disk.
/// </summary>
public sealed class ImportApp(ILogger logger, AppServices services)
{
  /// <summary>Runs the import and returns the process exit code.</summary>
  /// <param name="request">What to take.</param>
  /// <param name="target">The workbook to write, or null if none was asked for.</param>
  /// <param name="cancellationToken">Stops the reading of a folder.</param>
  public async Task<int> RunAsync(ImportRequest request, ImportTarget? target, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    try
    {
      return await ImportAsync(request, target, cancellationToken).ConfigureAwait(false);
    }
    catch (ExtractionException ex)
    {
      logger.Error("{Message}", ex.Message);
      logger.Debug(ex, "Input failure detail");
      return ExitCodes.InputError;
    }
  }

  private async Task<int> ImportAsync(ImportRequest request, ImportTarget? target, CancellationToken cancellationToken)
  {
    var run = new ImportRun(logger, services.Interaction, "pqdoc");
    var name = Path.GetFileName(request.Source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    var source = await ReadAsync(request.Source, logger, cancellationToken).ConfigureAwait(false);
    var (items, _) = run.ReadChoice(request, name);

    var import = new QueryImport(source.Graph, source.Queries.ToDictionary(q => q.Key, q => q.Value.Expression, DependencyGraph.NameComparer));
    var unknown = items.Where(item => import.Find(item) is null).ToList();
    if (unknown.Count > 0)
    {
      logger.Error("{Source:l} has no query called {Items:l}", name, string.Join(", ", unknown));
      return ExitCodes.InputError;
    }

    var plan = import.Plan(items.Select(item => import.Find(item)!));
    run.Report(plan);
    if (target is not null)
    {
      return WriteWorkbook(new Choice(request, target, name, source, plan, import), run);
    }

    if (request.Bundle is null)
    {
      logger.Information("Nothing was written. Add --new, --into or --bundle to write them out");
      return ExitCodes.Success;
    }

    return run.WriteBundle(
      request,
      QueryBundle.Files(plan, source.Files),
      QueryBundle.Readme(name, plan, source.Graph, source.Files),
      [QueryFileNames.Extension],
      QueryFileExporter.GitAttributesContent);
  }

  /// <summary>Everything a workbook is written from.</summary>
  private sealed record Choice(ImportRequest Request, ImportTarget Target, string SourceName, Source Source, ImportPlan Plan, QueryImport Import);

  /// <summary>A source's queries: what uses what, each one's file, and each one as it would be written.</summary>
  private sealed record Source(DependencyGraph Graph, Dictionary<string, QueryBundleFile> Files, Dictionary<string, QueryToWrite> Queries);

  private int WriteWorkbook(Choice choice, ImportRun run)
  {
    var (request, target, sourceName, source, plan, import) = choice;
    if (plan.Items.Count == 0)
    {
      logger.Warning("Nothing was chosen, so nothing was written");
      return ExitCodes.Success;
    }

    if (OutputPath(target) is not { } output)
    {
      return ExitCodes.InvalidArguments;
    }

    if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(request.Source), StringComparison.OrdinalIgnoreCase))
    {
      logger.Error("{Output:l} is the source, which is never changed. Choose another file", output);
      return ExitCodes.InvalidArguments;
    }

    var workbook = target.New is not null ? TargetWorkbook.New(IsMacroEnabled(output)) : TargetWorkbook.Read(target.Into!, logger);
    var compared = QueryClashes.Compare(source.Queries, sourceName, plan, import, workbook, logger);
    var resolution = new ClashResolver(logger, services.Interaction).Resolve(compared.Clashes, target.OnClash);
    if (resolution.Aborted)
    {
      return ExitCodes.OutputRefused;
    }

    Report(compared, resolution, workbook);
    if (compared.New.Count == 0 && resolution.Overwrite.Count == 0)
    {
      logger.Information("The target needs nothing from this, so nothing was written");
      return ExitCodes.Success;
    }

    var change = new QueryChange(
      [.. compared.New.Select(id => source.Queries[id])],
      [.. resolution.Overwrite.Select(id => source.Queries[id])]);

    return target.InPlace
      ? run.ChangeInPlace(output, request.OnExists == OnExistsBehaviour.Overwrite, Summary(compared, resolution), () => workbook.Write(change))
      : run.WriteWorkbook(output, request.OnExists, () => workbook.Write(change));
  }

  // The file written, or null if it can't be one: a new workbook has to be a kind that can be built.
  private string? OutputPath(ImportTarget target)
  {
    if (target.New is { } file)
    {
      var extension = Path.GetExtension(file);
      if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
      {
        return file;
      }

      logger.Error("--new needs a file name ending in .xlsx or .xlsm, not {File:l}, as those are the kinds pqdoc can build", Path.GetFileName(file));
      return null;
    }

    return target.InPlace ? target.Into! : ImportRun.CopyOf(target.Into!, Path.GetExtension(target.Into!));
  }

  private static bool IsMacroEnabled(string path) => Path.GetExtension(path).Equals(".xlsm", StringComparison.OrdinalIgnoreCase);

  private void Report(TargetComparison compared, ClashResolution resolution, TargetWorkbook workbook)
  {
    if (compared.New.Count > 0)
    {
      logger.Information("Adding, as connection only: {Items:l}", string.Join(", ", compared.New));
    }

    if (resolution.Overwrite.Count > 0)
    {
      logger.Information("Overwriting, each still loading where the target's did: {Items:l}", string.Join(", ", resolution.Overwrite));
    }

    if (compared.Identical.Count > 0)
    {
      logger.Information("Already there, identical: {Items:l}", string.Join(", ", compared.Identical));
    }

    if (resolution.Skip.Count > 0)
    {
      logger.Warning("Skipped, keeping the target's: {Items:l}", string.Join(", ", resolution.Skip));
    }

    if (workbook.IgnoresPrivacyLevels && (compared.New.Count > 0 || resolution.Overwrite.Count > 0))
    {
      logger.Warning("The target is set to ignore privacy levels, which Excel may stop trusting now its queries have changed. Check Query Options after opening it");
    }
  }

  private static string Summary(TargetComparison compared, ClashResolution resolution)
  {
    var parts = new List<string>();
    if (compared.New.Count > 0)
    {
      parts.Add($"{Count(compared.New.Count)} added");
    }

    if (resolution.Overwrite.Count > 0)
    {
      parts.Add($"{Count(resolution.Overwrite.Count)} overwritten");
    }

    return string.Join(", ", parts);
  }

  private static string Count(int count) => count == 1 ? "1 query" : $"{count} queries";

  /// <summary>Every query a workbook or folder has, in name order, each a query or a function. Any of them can be taken.</summary>
  /// <param name="source">A workbook, or a folder of .m files.</param>
  /// <param name="logger">Takes what reading it finds to warn about.</param>
  /// <param name="cancellationToken">Stops the reading of a folder.</param>
  /// <exception cref="ExtractionException">The workbook can't be read, or has no queries.</exception>
  public static async Task<IReadOnlyList<ImportableItem>> ItemsAsync(string source, ILogger logger, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(source);
    var read = await ReadAsync(Path.GetFullPath(source), logger, cancellationToken).ConfigureAwait(false);
    return
    [
      .. read.Queries.Values
        .OrderBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
        .Select(query => new ImportableItem(query.Name, query.ResultType == "Function" ? "function" : "query", null)),
    ];
  }

  // The queries of a workbook, or of a folder of .m files, with each one's file and text.
  private static async Task<Source> ReadAsync(string source, ILogger logger, CancellationToken cancellationToken)
  {
    var files = new Dictionary<string, QueryBundleFile>(DependencyGraph.NameComparer);
    var queries = new Dictionary<string, QueryToWrite>(DependencyGraph.NameComparer);
    if (Directory.Exists(source))
    {
      var analysis = await new DependencyAnalyser(logger).AnalyseFolderAsync(source, cancellationToken).ConfigureAwait(false);
      foreach (var (query, path) in analysis.Files ?? new Dictionary<string, string>())
      {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var text = QueryFile.FromBytes(query, Path.GetFileName(path), bytes).Text;
        files[query] = new QueryBundleFile(Path.GetFileName(path), bytes);
        queries[query] = new QueryToWrite(query, QueryFileText.WithCrlfOutsideText(text), null, ResultTypeOf(text));
      }

      return new Source(analysis.Graph, files, queries);
    }

    var read = WorkbookReader.Read(WorkbookFile.ReadAllBytes(source), logger);
    foreach (var query in read)
    {
      files[query.Name] = new QueryBundleFile(QueryFileNames.FileName(query.Name), QueryFileText.Bytes(query.Expression));
      queries[query.Name] = new QueryToWrite(query.Name, query.Expression, query.Description, query.ResultType ?? ResultTypeOf(query.Expression));
    }

    return new Source(new DependencyAnalyser(logger).Analyse(read.Select(q => new QuerySource(q.Name, q.Expression))), files, queries);
  }

  private static string ResultTypeOf(string expression) => FunctionDetector.IsFunction(expression) ? "Function" : "Table";
}
