namespace VbaDocumenter.Cli;

/// <summary>
/// Process exit codes, shared by every tool (see docs/GUIDELINES.md at the repository root). Code 4, output refused,
/// isn't here: vbadoc never refuses to overwrite its output.
/// </summary>
static class ExitCodes
{
  public const int Success = 0;

  /// <summary>Unexpected error.</summary>
  public const int UnexpectedError = 1;

  /// <summary>Invalid arguments.</summary>
  public const int InvalidArguments = 2;

  /// <summary>A problem with the workbook: missing, unreadable, corrupt or without a VBA project.</summary>
  public const int InputError = 3;

  /// <summary>The source files or page could not be written.</summary>
  public const int ExportFailed = 5;

  /// <summary>Cancelled with Ctrl+C.</summary>
  public const int Cancelled = 130;
}
