namespace PowerQueryDocumenter.Core.Extraction;

/// <summary>
/// A workbook's queries can't be read: it isn't a workbook, it has no queries, it's encrypted or it's damaged. The
/// message says which, in words the user can act on. The run stops with exit code 3.
/// </summary>
public sealed class ExtractionException : Exception
{
  /// <summary>Creates the exception with a message for the user.</summary>
  public ExtractionException(string message)
    : base(message)
  {
  }

  /// <summary>Creates the exception with a message for the user and the error behind it.</summary>
  public ExtractionException(string message, Exception? innerException)
    : base(message, innerException)
  {
  }
}
