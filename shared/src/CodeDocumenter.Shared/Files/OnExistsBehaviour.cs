namespace CodeDocumenter.Shared.Files;

/// <summary>What to do when an output file already exists (<c>--on-exists</c>).</summary>
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
