namespace PowerQueryDocumenter.Analysis;

/// <summary>The kinds of token produced by <see cref="MTokenizer"/>.</summary>
public enum MTokenKind
{
  /// <summary>A bare identifier, possibly dotted (for example <c>Sales</c> or <c>Table.SelectRows</c>).</summary>
  Identifier,

  /// <summary>A quoted identifier such as <c>#"Sales Data"</c>. The token text is the unescaped name.</summary>
  QuotedIdentifier,

  /// <summary>A reserved M keyword such as <c>let</c>, <c>in</c> or <c>each</c>.</summary>
  Keyword,

  /// <summary>A hash keyword such as <c>#table</c>, <c>#date</c> or <c>#shared</c>.</summary>
  HashKeyword,

  /// <summary>A text literal. The token text is the unescaped value.</summary>
  TextLiteral,

  /// <summary>A number literal.</summary>
  NumberLiteral,

  /// <summary>An operator or punctuator such as <c>=</c>, <c>=&gt;</c>, <c>,</c> or <c>[</c>.</summary>
  Punctuator,
}

/// <summary>A token from an M document.</summary>
/// <param name="Kind">The token kind.</param>
/// <param name="Text">
/// The token text. For quoted identifiers and text literals this is the unescaped value
/// (without the surrounding quotes and with <c>""</c> collapsed to <c>"</c>).
/// </param>
/// <param name="Position">Zero-based character offset of the token in the source.</param>
public readonly record struct MToken(MTokenKind Kind, string Text, int Position)
{
  /// <summary>True when this is a punctuator with exactly the given text.</summary>
  public bool IsPunctuator(string text) => Kind == MTokenKind.Punctuator && Text == text;

  /// <summary>True when this is a keyword with exactly the given text.</summary>
  public bool IsKeyword(string text) => Kind == MTokenKind.Keyword && Text == text;

  /// <summary>True for bare and quoted identifiers.</summary>
  public bool IsIdentifier => Kind is MTokenKind.Identifier or MTokenKind.QuotedIdentifier;
}
