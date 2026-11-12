using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Workbooks;
using Serilog;

namespace ExcelDocumenter.Shared.Importing;

/// <summary>A workbook to be written, for <c>--on-exists</c>.</summary>
/// <param name="Path">The file.</param>
public sealed record PlannedWorkbook(string Path) : IPlannedOutput
{
  /// <inheritdoc />
  public string Name => "workbook";
}

/// <summary>
/// What's the same in every tool's <c>import</c>: reading the choice, saying what it comes to, and writing a bundle or a
/// workbook. The source is never changed, and nor is a target unless <c>--in-place</c> says so.
/// </summary>
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

  /// <summary>The copy of a target that's written beside it, such as Target-imported.xlsm. Returns the full path.</summary>
  /// <param name="target">The target.</param>
  /// <param name="extension">The copy's extension, with its dot.</param>
  public static string CopyOf(string target, string extension)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(target);
    return Path.Combine(Path.GetDirectoryName(target) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(target)}-imported{extension}");
  }

  /// <summary>Writes a new file, unless one's there and the person says not to. Returns the exit code.</summary>
  /// <param name="path">The file to write.</param>
  /// <param name="onExists">What to do if it's there.</param>
  /// <param name="build">Makes the workbook, once it's settled that it's to be written.</param>
  public int WriteWorkbook(string path, OnExistsBehaviour onExists, Func<byte[]> build)
  {
    ArgumentNullException.ThrowIfNull(build);
    var resolution = new OverwriteResolver(logger, interaction).Resolve([new PlannedWorkbook(path)], onExists);
    if (resolution.Aborted)
    {
      return ExitCodes.OutputRefused;
    }

    return resolution.ToWrite.Count == 0 ? ExitCodes.Success : Save(path, build, AtomicFileWriter.Write);
  }

  /// <summary>
  /// Changes the target itself, if the person says so. It can't be undone, so <c>--force</c> or a yes are the only ways
  /// on, and nothing happens to the file until the result is finished, when it's replaced in one step. Returns the exit code.
  /// </summary>
  /// <param name="path">The target.</param>
  /// <param name="force">Whether <c>--force</c> answers yes.</param>
  /// <param name="change">What will change, as a phrase.</param>
  /// <param name="build">Makes and reads back the workbook.</param>
  public int ChangeInPlace(string path, bool force, string change, Func<byte[]> build)
  {
    ArgumentNullException.ThrowIfNull(build);

    // Said first, so nobody answers a question about something that can't happen.
    if (WhyNotReplaceable(path) is { } reason)
    {
      var open = Path.GetExtension(path).Equals(".xlam", StringComparison.OrdinalIgnoreCase)
        ? "Is it open in Excel? A loaded add-in is: untick it in the Add-ins dialog, or close Excel."
        : "Is it open in Excel?";
      logger.Error("{Path:l} can't be replaced, so nothing was changed. {Open:l} {Reason:l}", path, open, reason);
      return ExitCodes.ExportFailed;
    }

    var question = $"This changes {Path.GetFileName(path)} itself: {change}. It can't be undone. Are you sure?";
    if (!force && !interaction.IsInteractive)
    {
      logger.Error("This session is not interactive, so the tool cannot ask. Nothing was changed. Re-run with --force (-y) to go ahead");
      return ExitCodes.OutputRefused;
    }

    if (!force && !interaction.Confirm(question))
    {
      logger.Error("Stopped at your request. Nothing was changed");
      return ExitCodes.OutputRefused;
    }

    return Save(path, build, AtomicFileWriter.Replace);
  }

  private delegate void WriteFile(string path, ReadOnlySpan<byte> content);

  private int Save(string path, Func<byte[]> build, WriteFile write)
  {
    var content = build();
    try
    {
      write(path, content);
      logger.Information("Wrote {Path:l}", path);
      return ExitCodes.Success;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      logger.Error("Could not write {Path:l}: {Reason:l}", path, ex.Message);
      return ExitCodes.ExportFailed;
    }
  }

  // Excel holds a workbook open, so one that can't be had to ourselves can't be replaced either.
  private static string? WhyNotReplaceable(string path)
  {
    try
    {
      using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
      return null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return ex.Message;
    }
  }
}
