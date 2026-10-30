using PowerQueryDocumenter.Export;

namespace PowerQueryDocumenter.Application;

/// <summary>Turns exceptions into short, actionable messages without stack traces (FR-ERR-4).</summary>
public static class ErrorDescriber
{
  // Windows: ERROR_HANDLE_DISK_FULL (0x27) and ERROR_DISK_FULL (0x70). Unix: ENOSPC (28).
  private const int WindowsHandleDiskFull = unchecked((int)0x80070027);
  private const int WindowsDiskFull = unchecked((int)0x80070070);
  private const int UnixNoSpace = 28;

  /// <summary>Describes why writing an output failed.</summary>
  public static string DescribeOutputFailure(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);
    return exception switch
    {
      PlantUmlRenderException render => render.Message,
      UnauthorizedAccessException => "permission denied. Check that you can write to the output folder and that the file is not read-only",
      DirectoryNotFoundException => "the output folder does not exist or the path is not valid",
      PathTooLongException => "the output path is too long. Use a shorter --output-folder or --output-name",
      IOException io when IsDiskFull(io) => "there is not enough disk space",
      IOException io => $"{io.Message} The file may be open in another program",
      _ => exception.Message,
    };
  }

  /// <summary>True when an I/O error means the disk is full.</summary>
  public static bool IsDiskFull(IOException exception)
  {
    ArgumentNullException.ThrowIfNull(exception);
    return exception.HResult is WindowsHandleDiskFull or WindowsDiskFull or UnixNoSpace;
  }
}
