namespace ExcelDocumenter.Shared;

/// <summary>
/// Process exit codes, the same in every tool (see docs/GUIDELINES.md at the repository root).
/// </summary>
public static class ExitCodes
{
  /// <summary>Everything asked for was done.</summary>
  public const int Success = 0;

  /// <summary>Unexpected error.</summary>
  public const int UnexpectedError = 1;

  /// <summary>Invalid arguments or configuration.</summary>
  public const int InvalidArguments = 2;

  /// <summary>Input problem: missing, unreadable, damaged, or without what the tool documents.</summary>
  public const int InputError = 3;

  /// <summary>Output refused: an output file exists and overwriting was refused.</summary>
  public const int OutputRefused = 4;

  /// <summary>One or more outputs failed (others may have succeeded).</summary>
  public const int ExportFailed = 5;

  /// <summary>Cancelled with Ctrl+C.</summary>
  public const int Cancelled = 130;
}
