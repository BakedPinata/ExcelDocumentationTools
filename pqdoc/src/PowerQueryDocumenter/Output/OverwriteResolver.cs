using PowerQueryDocumenter.Configuration;
using Serilog;

namespace PowerQueryDocumenter.Output;

/// <summary>An output file the run intends to produce.</summary>
/// <param name="Format">The export format.</param>
/// <param name="Path">Full path of the destination file.</param>
public sealed record PlannedOutput(OutputFormat Format, string Path);

/// <summary>The decision for every planned output.</summary>
/// <param name="ToWrite">Outputs to produce.</param>
/// <param name="Skipped">Outputs skipped because the file already exists.</param>
/// <param name="Aborted">True when nothing may be written (exit code 4).</param>
public sealed record OverwriteResolution(
  IReadOnlyList<PlannedOutput> ToWrite,
  IReadOnlyList<PlannedOutput> Skipped,
  bool Aborted);

/// <summary>
/// Applies the <c>--on-exists</c> policy (FR-OVR-1 to FR-OVR-3). Every destination is checked
/// before anything is written, so a refused overwrite never leaves a partial set of outputs.
/// </summary>
public sealed class OverwriteResolver(ILogger logger, IUserInteraction interaction)
{
  private const string HowToProceed =
    "Re-run with --force (-y) to overwrite, or choose a behaviour with --on-exists overwrite|skip|fail.";

  /// <summary>Decides which outputs to write.</summary>
  /// <param name="outputs">The planned outputs.</param>
  /// <param name="behaviour">The requested behaviour.</param>
  /// <param name="fileExists">Checks whether a file exists (replaceable for testing).</param>
  public OverwriteResolution Resolve(
    IReadOnlyList<PlannedOutput> outputs,
    OnExistsBehaviour behaviour,
    Func<string, bool>? fileExists = null)
  {
    ArgumentNullException.ThrowIfNull(outputs);
    fileExists ??= File.Exists;

    var existing = outputs.Where(o => fileExists(o.Path)).ToHashSet();
    if (existing.Count == 0)
    {
      return new OverwriteResolution(outputs, [], Aborted: false);
    }

    switch (behaviour)
    {
      case OnExistsBehaviour.Overwrite:
        foreach (var output in existing)
        {
          logger.Information("Overwriting existing file {Path}", output.Path);
        }

        return new OverwriteResolution(outputs, [], Aborted: false);

      case OnExistsBehaviour.Skip:
        foreach (var output in existing)
        {
          logger.Warning("Skipping {Format}: {Path} already exists", output.Format.CliName(), output.Path);
        }

        return new OverwriteResolution(
          outputs.Where(o => !existing.Contains(o)).ToArray(),
          outputs.Where(existing.Contains).ToArray(),
          Aborted: false);

      case OnExistsBehaviour.Prompt when interaction.IsInteractive:
        return Prompt(outputs, existing);

      case OnExistsBehaviour.Prompt:
        ReportExisting(existing);
        logger.Error(
          "This session is not interactive, so the tool cannot ask whether to overwrite. Nothing was written. " + HowToProceed);
        return Abort();

      default:
        ReportExisting(existing);
        logger.Error("Nothing was written because --on-exists is fail. " + HowToProceed);
        return Abort();
    }
  }

  private OverwriteResolution Prompt(IReadOnlyList<PlannedOutput> outputs, HashSet<PlannedOutput> existing)
  {
    var toWrite = new List<PlannedOutput>();
    var skipped = new List<PlannedOutput>();
    var overwriteAll = false;

    foreach (var output in outputs)
    {
      if (!existing.Contains(output) || overwriteAll)
      {
        toWrite.Add(output);
        continue;
      }

      switch (interaction.AskOverwrite(output.Path))
      {
        case OverwriteAnswer.Yes:
          toWrite.Add(output);
          break;
        case OverwriteAnswer.All:
          overwriteAll = true;
          toWrite.Add(output);
          break;
        case OverwriteAnswer.No:
          logger.Warning("Skipping {Format}: keeping existing {Path}", output.Format.CliName(), output.Path);
          skipped.Add(output);
          break;
        default:
          logger.Error("Stopped at your request. Nothing was written");
          return Abort();
      }
    }

    return new OverwriteResolution(toWrite, skipped, Aborted: false);
  }

  private void ReportExisting(IEnumerable<PlannedOutput> existing)
  {
    foreach (var output in existing)
    {
      logger.Error("Output file already exists: {Path}", output.Path);
    }
  }

  private static OverwriteResolution Abort() => new([], [], Aborted: true);
}
