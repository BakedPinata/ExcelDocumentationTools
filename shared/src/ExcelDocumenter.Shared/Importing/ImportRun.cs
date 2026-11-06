using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Workbooks;
using Serilog;

namespace ExcelDocumenter.Shared.Importing;

/// <summary>What's the same in every tool's <c>import</c>: reading the choice, saying what it comes to, writing a bundle.</summary>
public sealed class ImportRun(ILogger logger, IUserInteraction interaction, string tool)
{
  /// <summary>The items asked for, and the references to leave out.</summary>
  /// <param name="request">What was asked for.</param>
  /// <param name="sourceName">The source's file name, to compare with the one the manifest names.</param>
  /// <exception cref="ExtractionException">The manifest can't be used.</exception>
  public (IReadOnlyList<string> Items, IReadOnlyList<string> LeaveOutReferences) ReadChoice(ImportRequest request, string sourceName)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (request.Manifest is null)
    {
      return (request.Items, []);
    }

    var manifest = ExportManifest.Read(request.Manifest, tool);
    if (manifest.Workbook is not null && !manifest.Workbook.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
    {
      logger.Warning("The manifest was made from {Manifest:l}, not {Source:l}", manifest.Workbook, sourceName);
    }

    return (manifest.Items, manifest.LeaveOutReferences);
  }

  /// <summary>Says what the choice comes to, as the page's Export tab does.</summary>
  public void Report(ImportPlan plan)
  {
    ArgumentNullException.ThrowIfNull(plan);
    logger.Information("Chosen: {Items:l}", string.Join(", ", plan.Picked));
    logger.Information("Brought along: {Items:l}", plan.Brought.Count == 0 ? "nothing else" : string.Join(", ", plan.Brought.Select(b => $"{b.Id} (used by {string.Join(", ", b.UsedBy)})")));
    foreach (var need in plan.Needs)
    {
      logger.Warning("To check in the target: {What:l}, {Why:l}. Used by {Items:l}", need.Need.What, need.Need.Why, string.Join(", ", need.UsedBy));
    }

    foreach (var offer in plan.Offers)
    {
      logger.Information("Not included, though you may want it: {Item:l}, for {For:l}", offer.Id, offer.For);
    }
  }

  /// <summary>Writes a bundle, unless the folder isn't free or the person says not to. Returns the exit code.</summary>
  /// <param name="request">What was asked for, with its bundle folder.</param>
  /// <param name="files">Each file's name and bytes.</param>
  /// <param name="readme">The README's text.</param>
  /// <param name="ownedExtensions">The extensions of the tool's source files.</param>
  /// <param name="gitAttributes">The text of the <c>.gitattributes</c> to create in a folder that has none.</param>
  public int WriteBundle(
    ImportRequest request,
    IReadOnlyDictionary<string, byte[]> files,
    string readme,
    IReadOnlyCollection<string> ownedExtensions,
    string gitAttributes)
  {
    ArgumentNullException.ThrowIfNull(request);
    var folder = request.Bundle!;
    if (!BundleFolder.CanHold(folder, tool))
    {
      logger.Error("{Folder:l} has other files in it, and writing a bundle would delete any {Extensions:l} files. Choose an empty folder", folder, string.Join(", ", ownedExtensions));
      return ExitCodes.OutputRefused;
    }

    var resolution = new OverwriteResolver(logger, interaction).Resolve(
      [new PlannedBundle(Path.Combine(folder, BundleFolder.ReadmeName))],
      request.OnExists,
      path => File.Exists(path) && BundleFolder.WouldChange(folder, files, readme));
    if (resolution.Aborted)
    {
      return ExitCodes.OutputRefused;
    }

    if (resolution.ToWrite.Count == 0)
    {
      return ExitCodes.Success;
    }

    try
    {
      var result = BundleFolder.Write(folder, files, readme, ownedExtensions, gitAttributes);
      logger.Information("Wrote the bundle to {Folder:l}: {Written} written, {Unchanged} unchanged, {Deleted} deleted", folder, result.Written.Count, result.Unchanged.Count, result.Deleted.Count);
      return ExitCodes.Success;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      logger.Error("Could not write to {Folder}: {Reason}", folder, ex.Message);
      return ExitCodes.ExportFailed;
    }
  }
}
