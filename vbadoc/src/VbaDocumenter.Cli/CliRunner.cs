using System.CommandLine;
using System.Diagnostics;
using System.CommandLine.Invocation;
using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Workbooks;
using Serilog;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Cli;

/// <summary>
/// The top-level flow: parse, validate, run, and map every outcome to an exit code. Kept separate from Program so the
/// whole flow can be tested.
/// </summary>
public static class CliRunner
{
  public static int Run(IReadOnlyList<string> args, AppServices services, CancellationToken cancellationToken)
  {
    var cli = new CliDefinition();
    var parseResult = cli.Parse(args);

    // --help and --version: let System.CommandLine print them (exit code 0).
    if (parseResult.Action is not null and not ParseErrorAction && parseResult.Action != cli.RootCommand.Action)
    {
      return parseResult.Invoke(new InvocationConfiguration
      {
        Output = services.Output,
        Error = services.Error,
        ProcessTerminationTimeout = null,
      });
    }

    var logger = services.CreateLogger(OptionsBinder.GetVerbosity(parseResult, cli));

    try
    {
      var unknownOptions = UnknownOptions.Find(parseResult, args);
      if (unknownOptions.Count > 0) return ReportInvalidArguments(unknownOptions);
      if (parseResult.Errors.Count > 0) return ReportInvalidArguments(parseResult.Errors.Select(e => e.Message));

      if (parseResult.CommandResult.Command == cli.Import.Command)
      {
        return new ImportCommand(logger, services).Run(
            cli.Import.Bind(parseResult),
            cli.ImportTarget.Bind(parseResult),
            parseResult.GetValue(cli.NoReferences),
            parseResult.GetValue(cli.DropSignature));
      }

      var binding = OptionsBinder.Bind(parseResult, cli);
      if (binding.Options is null) return ReportInvalidArguments(binding.Errors);

      return Export(binding.Options, logger, services, cancellationToken);
    }
    catch (ExtractionException ex)
    {
      logger.Error("{Message}", ex.Message);
      logger.Debug(ex, "Workbook failure detail");
      return ExitCodes.InputError;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      logger.Warning("Cancelled before anything was written");
      return ExitCodes.Cancelled;
    }
    catch (Exception ex)
    {
      logger.Error("Unexpected error: {Message}", ex.Message);
      logger.Debug(ex, "Unexpected error detail");
      return ExitCodes.UnexpectedError;
    }

    int ReportInvalidArguments(IEnumerable<string> messages)
    {
      foreach (var message in messages) logger.Error("{Message}", message);
      services.Error.WriteLine(CliDefinition.BriefUsage);
      return ExitCodes.InvalidArguments;
    }
  }

  /// <summary>
  /// Core's work is synchronous and quick, so Ctrl+C is honoured between steps. Writing, once started, always finishes:
  /// half an export would be worse than waiting a moment for all of it.
  /// </summary>
  static int Export(CommandLineOptions options, ILogger logger, AppServices services, CancellationToken cancellationToken)
  {
    var interaction = services.Interaction;
    var total = Stopwatch.StartNew();
    logger.Information("Reading {Workbook}", options.WorkbookPath);
    var project = Timed(logger, "Reading", () => WorkbookReader.Read(options.WorkbookPath));
    logger.Information("  Project '{Project}', code page {CodePage}, {ModuleCount} module(s)", project.Name, project.CodePage, project.Modules.Count);
    cancellationToken.ThrowIfCancellationRequested();

    var parsed = Timed(logger, "Parsing", () => ProjectParser.Parse(project));
    ConsoleReport.WriteModules(parsed, logger);
    cancellationToken.ThrowIfCancellationRequested();

    logger.Information("Analysing dependencies");
    var dependencies = Timed(logger, "Analysing", () => DependencyAnalyser.Analyse(parsed));
    ConsoleReport.WriteDependencies(dependencies, logger);

    var html = Timed(logger, "Building the page",
        () => HtmlPageWriter.Build(dependencies, project, Path.GetFileName(options.WorkbookPath), options.EmbedSource));
    cancellationToken.ThrowIfCancellationRequested();

    // The page is asked about before anything is written, so quitting or refusing changes nothing on disk. A page that
    // would be written unchanged has nothing to overwrite, so it isn't asked about; the module files follow the
    // workbook and never are.
    var pageFile = HtmlPageWriter.FileName(options.OutputName);
    var pagePath = Path.Combine(options.OutputFolder, pageFile);
    var resolution = new OverwriteResolver(logger, interaction).Resolve(
        [new PlannedPage(pagePath)], options.OnExists, path => File.Exists(path) && HtmlPageWriter.WouldChange(path, html));
    if (resolution.Aborted) return ExitCodes.OutputRefused;
    cancellationToken.ThrowIfCancellationRequested();

    try
    {
      logger.Information("Exporting to {Folder}", options.OutputFolder);
      var result = Timed(logger, "Writing module files", () => ProjectExporter.Export(project, options.OutputFolder));
      logger.Information("  {Written} written, {Unchanged} unchanged, {Deleted} deleted", result.Written.Count, result.Unchanged.Count, result.Deleted.Count);
      foreach (var name in result.Deleted) logger.Information("    deleted {File} (no longer in the workbook)", name);
      if (result.CreatedGitAttributes) logger.Information("  Created .gitattributes");

      // A page skipped at the prompt or with --on-exists skip has already been reported.
      if (resolution.ToWrite.Count > 0)
      {
        bool pageWritten = Timed(logger, "Writing the page", () => HtmlPageWriter.Write(options.OutputFolder, pageFile, html));
        logger.Information("  {Page} {Outcome} {Hint}",
            pageFile,
            pageWritten ? "written" : "unchanged",
            options.EmbedSource ? "(source embedded)" : "(open it and use Load folder to see source)");
        services.PageWritten?.Invoke(pagePath);
      }

      // Only once the new page is safely written, so there's always a page.
      foreach (var name in HtmlPageWriter.RemoveOlderPages(options.OutputFolder, pageFile))
        logger.Information("    deleted {File} (an older page from vbadoc)", name);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      logger.Error("Could not write to {Folder}: {Reason}", options.OutputFolder, ex.Message);
      logger.Debug(ex, "Output failure detail");
      return ExitCodes.ExportFailed;
    }

    logger.Debug("Finished in {Milliseconds} ms", total.ElapsedMilliseconds);
    return ExitCodes.Success;
  }

  /// <summary>Runs one step of the export, and logs how long it took (shown with --verbose).</summary>
  static T Timed<T>(ILogger logger, string step, Func<T> work)
  {
    var stopwatch = Stopwatch.StartNew();
    var result = work();
    logger.Debug("  {Step:l} took {Milliseconds} ms", step, stopwatch.ElapsedMilliseconds);
    return result;
  }
}
