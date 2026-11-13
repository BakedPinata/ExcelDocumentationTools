using Serilog;

namespace CodeDocumenter.Shared.Files;

/// <summary>An output file a run means to write.</summary>
public interface IPlannedOutput
{
  /// <summary>Full path of the destination file.</summary>
  string Path { get; }

  /// <summary>What the output is called in messages, such as <c>html</c> or <c>page</c>.</summary>
  string Name { get; }
}

/// <summary>The decision for every planned output.</summary>
/// <param name="ToWrite">Outputs to produce.</param>
/// <param name="Skipped">Outputs skipped because the file already exists.</param>
/// <param name="Aborted">True when nothing may be written (exit code 4).</param>
public sealed record OverwriteResolution<T>(IReadOnlyList<T> ToWrite, IReadOnlyList<T> Skipped, bool Aborted)
  where T : IPlannedOutput;

/// <summary>
/// Applies the <c>--on-exists</c> policy. Every destination is checked before anything is written, so a refused
/// overwrite never leaves a partial set of outputs.
/// </summary>
public sealed class OverwriteResolver(ILogger logger, IUserInteraction interaction)
{
  private const string HowToProceed =
    "Re-run with --force (-y) to overwrite, or choose a behaviour with --on-exists overwrite|skip|fail.";

  /// <summary>Decides which outputs to write.</summary>
  /// <param name="outputs">The planned outputs.</param>
  /// <param name="behaviour">The requested behaviour.</param>
  /// <param name="fileExists">
  /// Whether a file counts as existing. By default, whether it's there; a tool can also leave out files it wouldn't
  /// change, which have nothing to overwrite.
  /// </param>
  public OverwriteResolution<T> Resolve<T>(IReadOnlyList<T> outputs, OnExistsBehaviour behaviour, Func<string, bool>? fileExists = null)
    where T : IPlannedOutput
  {
    ArgumentNullException.ThrowIfNull(outputs);
    fileExists ??= File.Exists;

    var existing = outputs.Where(o => fileExists(o.Path)).ToHashSet();
    if (existing.Count == 0)
    {
      return new OverwriteResolution<T>(outputs, [], Aborted: false);
    }

    switch (behaviour)
    {
      case OnExistsBehaviour.Overwrite:
        foreach (var output in existing)
        {
          logger.Information("Overwriting existing file {Path}", output.Path);
        }

        return new OverwriteResolution<T>(outputs, [], Aborted: false);

      case OnExistsBehaviour.Skip:
        foreach (var output in existing)
        {
          logger.Warning("Skipping {Output}: {Path} already exists", output.Name, output.Path);
        }

        return new OverwriteResolution<T>(
          outputs.Where(o => !existing.Contains(o)).ToArray(),
          outputs.Where(existing.Contains).ToArray(),
          Aborted: false);

      case OnExistsBehaviour.Prompt when interaction.IsInteractive:
        return Prompt(outputs, existing);

      case OnExistsBehaviour.Prompt:
        ReportExisting(existing);
        logger.Error(
          "This session is not interactive, so the tool cannot ask whether to overwrite. Nothing was written. " + HowToProceed);
        return Abort<T>();

      default:
        ReportExisting(existing);
        logger.Error("Nothing was written because --on-exists is fail. " + HowToProceed);
        return Abort<T>();
    }
  }

  private OverwriteResolution<T> Prompt<T>(IReadOnlyList<T> outputs, HashSet<T> existing)
    where T : IPlannedOutput
  {
    var toWrite = new List<T>();
    var skipped = new List<T>();
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
          logger.Warning("Skipping {Output}: keeping existing {Path}", output.Name, output.Path);
          skipped.Add(output);
          break;
        default:
          logger.Error("Stopped at your request. Nothing was written");
          return Abort<T>();
      }
    }

    return new OverwriteResolution<T>(toWrite, skipped, Aborted: false);
  }

  private void ReportExisting<T>(IEnumerable<T> existing)
    where T : IPlannedOutput
  {
    foreach (var output in existing)
    {
      logger.Error("Output file already exists: {Path}", output.Path);
    }
  }

  private static OverwriteResolution<T> Abort<T>()
    where T : IPlannedOutput => new([], [], Aborted: true);
}
