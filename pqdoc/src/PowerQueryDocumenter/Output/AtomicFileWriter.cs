namespace PowerQueryDocumenter.Output;

/// <summary>
/// Writes files atomically (FR-OVR-4): content goes to a temporary file in the destination folder,
/// which is then moved over the destination. A failure or cancellation part-way through deletes the
/// temporary file and leaves any existing destination untouched, so no truncated output is left
/// behind (FR-ERR-6).
/// </summary>
public static class AtomicFileWriter
{
  /// <summary>Suffix used for temporary files.</summary>
  public const string TemporaryFileSuffix = ".tmp";

  /// <summary>Writes <paramref name="destination"/> using <paramref name="write"/> to produce its content.</summary>
  public static async Task WriteAsync(
    string destination,
    Func<Stream, CancellationToken, Task> write,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(destination);
    ArgumentNullException.ThrowIfNull(write);

    var folder = Path.GetDirectoryName(Path.GetFullPath(destination))
      ?? throw new ArgumentException($"'{destination}' has no parent folder.", nameof(destination));

    // Hidden-style name in the same folder, so the final move is a rename on the same volume.
    var temporaryPath = Path.Combine(folder, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}{TemporaryFileSuffix}");

    try
    {
      var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
      await using (stream.ConfigureAwait(false))
      {
        await write(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      File.Move(temporaryPath, destination, overwrite: true);
    }
    catch
    {
      TryDelete(temporaryPath);
      throw;
    }
  }

  private static void TryDelete(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // Best effort: the original error is more useful than a cleanup failure.
    }
  }
}
