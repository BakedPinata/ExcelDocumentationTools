using System.CommandLine;
using System.CommandLine.Invocation;
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
static class CliRunner
{
  public static int Run(IReadOnlyList<string> args, AppServices services, CancellationToken cancellationToken)
  {
    var cli = new CliDefinition();
    var parseResult = cli.Parse(args);

    // --help and --version: let System.CommandLine print them (exit code 0).
    if (parseResult.Action is not null and not ParseErrorAction)
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
      if (parseResult.Errors.Count > 0) return ReportInvalidArguments(parseResult.Errors.Select(e => e.Message));

      var binding = OptionsBinder.Bind(parseResult, cli);
      if (binding.Options is null) return ReportInvalidArguments(binding.Errors);

      return Export(binding.Options, logger, cancellationToken);
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
  static int Export(CommandLineOptions options, ILogger logger, CancellationToken cancellationToken)
  {
    logger.Information("Reading {Workbook}", options.WorkbookPath);
    var project = WorkbookReader.Read(options.WorkbookPath);
    logger.Information("  Project '{Project}', code page {CodePage}, {ModuleCount} module(s)", project.Name, project.CodePage, project.Modules.Count);
    cancellationToken.ThrowIfCancellationRequested();

    var parsed = ProjectParser.Parse(project);
    ConsoleReport.WriteModules(parsed, logger);
    cancellationToken.ThrowIfCancellationRequested();

    logger.Information("Analysing dependencies");
    var dependencies = DependencyAnalyser.Analyse(parsed);
    ConsoleReport.WriteDependencies(dependencies, logger);

    var html = HtmlPageWriter.Build(dependencies, project, Path.GetFileName(options.WorkbookPath), options.EmbedSource);
    cancellationToken.ThrowIfCancellationRequested();

    try
    {
      logger.Information("Exporting to {Folder}", options.OutputFolder);
      var result = ProjectExporter.Export(project, options.OutputFolder);
      logger.Information("  {Written} written, {Unchanged} unchanged, {Deleted} deleted", result.Written.Count, result.Unchanged.Count, result.Deleted.Count);
      foreach (var name in result.Deleted) logger.Information("    deleted {File} (no longer in the workbook)", name);
      if (result.CreatedGitAttributes) logger.Information("  Created .gitattributes");

      bool pageWritten = HtmlPageWriter.Write(options.OutputFolder, html);
      logger.Information("  {Page} {Outcome} {Hint}",
          HtmlPageWriter.FileName,
          pageWritten ? "written" : "unchanged",
          options.EmbedSource ? "(source embedded)" : "(open it and use Load folder to see source)");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      logger.Error("Could not write to {Folder}: {Reason}", options.OutputFolder, ex.Message);
      logger.Debug(ex, "Output failure detail");
      return ExitCodes.ExportFailed;
    }

    return ExitCodes.Success;
  }
}
