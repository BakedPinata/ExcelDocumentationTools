namespace ExcelDocumenter.Shared.Files;

/// <summary>
/// Writes files atomically: content goes to a temporary file in the destination folder, which is then moved over the
/// destination. A failure or cancellation part-way through deletes the temporary file and leaves any existing
/// destination untouched, so no truncated file is left behind.
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
    ArgumentNullException.ThrowIfNull(write);
    var temporaryPath = TemporaryPathFor(destination);

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

  /// <summary>Writes <paramref name="content"/> to <paramref name="destination"/>.</summary>
  public static void Write(string destination, ReadOnlySpan<byte> content)
  {
    var temporaryPath = TemporaryPathFor(destination);

    try
    {
      using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        stream.Write(content);
      }

      File.Move(temporaryPath, destination, overwrite: true);
    }
    catch
    {
      TryDelete(temporaryPath);
      throw;
    }
  }

  /// <summary>
  /// Puts <paramref name="content"/> in place of a file that's there, in one step, as a move can't quite do: the file keeps
  /// its own permissions and creation time, which matter when it's someone's workbook rather than a tool's output.
  /// </summary>
  public static void Replace(string destination, ReadOnlySpan<byte> content)
  {
    var temporaryPath = TemporaryPathFor(destination);

    try
    {
      using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        stream.Write(content);
      }

      File.Replace(temporaryPath, destination, destinationBackupFileName: null);
    }
    catch
    {
      TryDelete(temporaryPath);
      throw;
    }
  }

  /// <summary>A hidden-style name in the destination's folder, so the final move is a rename on the same volume.</summary>
  private static string TemporaryPathFor(string destination)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(destination);
    var folder = Path.GetDirectoryName(Path.GetFullPath(destination))
      ?? throw new ArgumentException($"'{destination}' has no parent folder.", nameof(destination));
    return Path.Combine(folder, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}{TemporaryFileSuffix}");
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
