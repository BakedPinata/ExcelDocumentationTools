using CodeDocumenter.Shared;

namespace CodeDocumenter.Desktop.Running;

/// <summary>How a run ended, in words, for the log pane. The exit code follows, for anyone scripting the same command.</summary>
internal static class ExitCodeText
{
  public static string Describe(int exitCode)
  {
    var words = exitCode switch
    {
      ExitCodes.Success => "Done.",
      ExitCodes.UnexpectedError => "Stopped by an unexpected error. With Log set to Verbose, the log says more.",
      ExitCodes.InvalidArguments => "Stopped: an option isn't right. The log says which.",
      ExitCodes.InputError => "There's a problem with the workbook or folder. The log says what.",
      ExitCodes.OutputRefused => "Nothing was written: a file was already there, and wasn't to be overwritten.",
      ExitCodes.ExportFailed => "Something couldn't be written. The log says what.",
      ExitCodes.Cancelled => "Cancelled.",
      _ => "Finished.",
    };
    return $"{words} (exit code {exitCode})";
  }
}
