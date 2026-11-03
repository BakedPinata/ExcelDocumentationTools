namespace PowerQueryDocumenter.Core.Configuration;

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
