namespace PowerQueryDocumenter.Core.Analysis;

/// <summary>Word lists from the Power Query M language specification.</summary>
public static class MLanguage
{
  // Reserved keywords. M is case-sensitive, so these only match in lower case.
  private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
  {
    "and", "as", "catch", "each", "else", "error", "false", "if", "in", "is", "let", "meta",
    "not", "null", "or", "otherwise", "section", "shared", "then", "true", "try", "type",
  };

  // Primitive type names and type modifiers. They are not reserved words, but in practice they
  // only appear in type positions ("as table", "type [A = text]").
  private static readonly HashSet<string> TypeWords = new(StringComparer.Ordinal)
  {
    "any", "anynonnull", "binary", "date", "datetime", "datetimezone", "duration", "function",
    "list", "logical", "none", "number", "record", "table", "text", "time", "nullable", "optional",
  };

  /// <summary>True for reserved M keywords (exact, case-sensitive match).</summary>
  public static bool IsKeyword(string word) => Keywords.Contains(word);

  /// <summary>
  /// True for M primitive type names and the type modifiers <c>nullable</c> and <c>optional</c>
  /// (exact, case-sensitive match, so <c>table</c> matches but <c>Table</c> does not).
  /// </summary>
  public static bool IsTypeWord(string word) => TypeWords.Contains(word);
}
