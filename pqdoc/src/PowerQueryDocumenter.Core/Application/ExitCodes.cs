namespace PowerQueryDocumenter.Core.Application;

/// <summary>Process exit codes (requirements section 5.3).</summary>
public static class ExitCodes
{
  /// <summary>All requested outputs produced. Skips caused by <c>--on-exists skip</c> are not failures.</summary>
  public const int Success = 0;

  /// <summary>Unexpected error.</summary>
  public const int UnexpectedError = 1;

  /// <summary>Invalid arguments or configuration.</summary>
  public const int InvalidArguments = 2;

  /// <summary>Input problem: folder missing or unreadable, no .m files, or a .m file could not be read.</summary>
  public const int InputError = 3;

  /// <summary>Output refused: an output file exists and overwriting was refused.</summary>
  public const int OutputRefused = 4;

  /// <summary>One or more exports failed (others may have succeeded).</summary>
  public const int ExportFailed = 5;

  /// <summary>Cancelled by the user (Ctrl+C).</summary>
  public const int Cancelled = 130;
}
