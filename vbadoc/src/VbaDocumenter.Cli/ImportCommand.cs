using CodeDocumenter.Shared;
using CodeDocumenter.Shared.CommandLine;
using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;
using Serilog;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Cli;

/// <summary>
/// <c>vbadoc import</c>: takes chosen modules, with what they use, out of a workbook, says what that comes to, and writes
/// it as a bundle or into a workbook if asked. Everything is read and every question answered before anything is written,
/// so a choice that can't be met, or an answer of no, changes nothing on disk.
/// </summary>
sealed class ImportCommand(ILogger logger, AppServices services)
{
  readonly IUserInteraction _interaction = services.Interaction;

  public int Run(ImportRequest request, ImportTarget? target, bool noReferences, bool dropSignature)
  {
    var run = new ImportRun(logger, _interaction, CliDefinition.ToolName);
    var workbook = Path.GetFileName(request.Source);
    var project = WorkbookReader.Read(request.Source);
    var (items, leaveOut) = run.ReadChoice(request, workbook);
    var import = new ModuleImport(DependencyAnalyser.Analyse(ProjectParser.Parse(project)));

    var unknown = items.Where(item => import.Find(item) is null).ToList();
    if (unknown.Count > 0)
    {
      logger.Error("{Workbook:l} has no module called {Items:l}", workbook, string.Join(", ", unknown));
      return ExitCodes.InputError;
    }

    var picks = items.Select(item => import.Find(item)!).ToList();
    var refused = picks.Where(id => import.WhyNot(id) is not null).ToList();
    if (refused.Count > 0)
    {
      foreach (var id in refused) logger.Error("{Module:l} can't be taken: {Reason:l}", id, import.WhyNot(id));
      return ExitCodes.InputError;
    }

    var plan = import.Plan(picks);
    run.Report(plan);
    var references = noReferences ? [] : project.References
        .Where(r => !r.IsStandard && !leaveOut.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).ToList();

    if (target is not null)
    {
      return WriteWorkbook(new Choice(request, target, project, plan, import, references, dropSignature), run);
    }

    foreach (var reference in references) logger.Information("Library reference: {Reference:l} ({Name:l})", reference.Description, reference.Name);
    if (request.Bundle is null)
    {
      logger.Information("Nothing was written. Add --new, --into or --bundle to write them out");
      return ExitCodes.Success;
    }

    return run.WriteBundle(request, ModuleBundle.Files(project, plan), ModuleBundle.Readme(workbook, project, plan, references),
        ProjectExporter.ModuleExtensions.ToList(), ProjectExporter.GitAttributesContent);
  }

  /// <summary>Everything a workbook is written from.</summary>
  sealed record Choice(ImportRequest Request, ImportTarget Target, VbaProject Project, ImportPlan Plan, ModuleImport Import, IReadOnlyList<VbaReference> References, bool DropSignature);

  int WriteWorkbook(Choice choice, ImportRun run)
  {
    var (request, target, project, plan, _, _, _) = choice;
    if (plan.Items.Count == 0)
    {
      logger.Warning("Nothing was chosen, so nothing was written");
      return ExitCodes.Success;
    }

    if (OutputPath(target) is not { } output) return ExitCodes.InvalidArguments;
    if (SamePath(output, request.Source))
    {
      logger.Error("{Output:l} is the source, which is never changed. Choose another file", output);
      return ExitCodes.InvalidArguments;
    }

    if (target.InPlace && IsXlsx(output))
    {
      logger.Error("An .xlsx can't hold modules, so {Target:l} can't be changed in place. Without --in-place, a copy is written as an .xlsm", output);
      return ExitCodes.OutputRefused;
    }

    var workbook = target.New is not null ? TargetWorkbook.New(addin: IsXlam(output)) : TargetWorkbook.Read(target.Into!);
    if (workbook.IsSigned && !choice.DropSignature)
    {
      logger.Error("The target's VBA project is signed, and changing it would break the signature. Nothing was written. "
          + "Re-run with --drop-signature to remove the signature and go ahead");
      return ExitCodes.OutputRefused;
    }

    var sourceName = Path.GetFileName(request.Source);
    var compared = ModuleClashes.Compare(project, sourceName, plan, choice.Import, workbook);
    var resolution = new ClashResolver(logger, _interaction).Resolve(compared.Clashes, target.OnClash);
    if (resolution.Aborted) return ExitCodes.OutputRefused;

    var references = ReferenceChoice.Make(choice.References, workbook.Project, services.IsLibraryInstalled);
    var adding = compared.New.Concat(resolution.Overwrite).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Report(compared, resolution, references, workbook);
    if (adding.Count == 0 && references.Add.Count == 0)
    {
      logger.Information("The target needs nothing from this, so nothing was written");
      return ExitCodes.Success;
    }

    var change = new ModuleChange(
        [.. project.Modules.Where(m => adding.Contains(m.Name))],
        resolution.Overwrite,
        references.Add,
        project.CodePage);

    if (target.InPlace)
    {
      return run.ChangeInPlace(output, request.OnExists == OnExistsBehaviour.Overwrite, Summary(compared, resolution, references), () => workbook.Write(change));
    }

    return run.WriteWorkbook(output, request.OnExists, () => workbook.Write(change));
  }

  // The file written, or null if it can't be one: a new workbook has to be able to hold its modules.
  string? OutputPath(ImportTarget target)
  {
    if (target.New is { } file)
    {
      if (TargetWorkbook.NewExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) return file;
      logger.Error("--new needs a file name ending in .xlsm, or .xlam for an add-in, not {File:l}, as those are the kinds vbadoc can build", Path.GetFileName(file));
      return null;
    }

    return target.InPlace ? target.Into! : ImportRun.CopyOf(target.Into!, IsXlsx(target.Into!) ? ".xlsm" : Path.GetExtension(target.Into!));
  }

  static bool IsXlsx(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

  static bool IsXlam(string path) => Path.GetExtension(path).Equals(".xlam", StringComparison.OrdinalIgnoreCase);

  static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

  void Report(TargetComparison compared, ClashResolution resolution, ReferenceChoice references, TargetWorkbook workbook)
  {
    if (compared.New.Count > 0) logger.Information("Adding: {Items:l}", string.Join(", ", compared.New));
    if (resolution.Overwrite.Count > 0) logger.Information("Overwriting: {Items:l}", string.Join(", ", resolution.Overwrite));
    if (compared.Identical.Count > 0) logger.Information("Already there, identical: {Items:l}", string.Join(", ", compared.Identical));
    if (resolution.Skip.Count > 0) logger.Warning("Skipped, keeping the target's: {Items:l}", string.Join(", ", resolution.Skip));

    foreach (var reference in references.Add) logger.Information("Adding the library reference: {Reference:l} ({Name:l})", reference.Description, reference.Name);
    foreach (var reference in references.NotFound)
    {
      logger.Warning("Left out, as it can't be found on this machine and would stop the project compiling: {Reference:l} ({Name:l}). Tick it in Tools > References where it's installed", reference.Description, reference.Name);
    }

    foreach (var reference in references.NameTaken)
    {
      logger.Warning("Left out, as the target has a different library called {Name:l}: {Reference:l}", reference.Name, reference.Description);
    }

    foreach (var reference in references.NotChecked)
    {
      logger.Warning("Couldn't check that it's installed, which only works on Windows: {Reference:l} ({Name:l})", reference.Description, reference.Name);
    }

    if (workbook.IsSigned) logger.Warning("The target's signature will be removed, as this change would break it");
    if (workbook.IsLocked) logger.Information("The target's VBA project is locked, and stays locked");
  }

  static string Summary(TargetComparison compared, ClashResolution resolution, ReferenceChoice references)
  {
    var parts = new List<string>();
    if (compared.New.Count > 0) parts.Add($"{Count(compared.New.Count, "module")} added");
    if (resolution.Overwrite.Count > 0) parts.Add($"{Count(resolution.Overwrite.Count, "module")} overwritten");
    if (references.Add.Count > 0) parts.Add($"{Count(references.Add.Count, "library reference")} added");
    return string.Join(", ", parts);
  }

  static string Count(int count, string what) => count == 1 ? $"1 {what}" : $"{count} {what}s";
}
