namespace PowerQueryDocumenter.Core.Configuration;

/// <summary>What to do when an output file already exists (FR-OVR).</summary>
public enum OnExistsBehaviour
{
  /// <summary>Ask per file when interactive; behave as <see cref="Fail"/> when not.</summary>
  Prompt,

  /// <summary>Replace the existing file.</summary>
  Overwrite,

  /// <summary>Leave the existing file and do not produce that output.</summary>
  Skip,

  /// <summary>Abort before writing anything.</summary>
  Fail,
}

/// <summary>How much log output to show.</summary>
public enum LogVerbosity
{
  /// <summary>Information and above (default).</summary>
  Normal,

  /// <summary>Debug and above (<c>--verbose</c>).</summary>
  Verbose,

  /// <summary>Warnings and errors only (<c>--quiet</c>).</summary>
  Quiet,
}
