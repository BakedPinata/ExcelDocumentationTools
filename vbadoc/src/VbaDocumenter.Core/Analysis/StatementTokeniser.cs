using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Analysis;

public enum TokenKind
{
  Identifier,
  /// <summary>Member access: "obj.Member", or "With" access ".Member".</summary>
  Dot,
  /// <summary>Dictionary access: "rs!Field".</summary>
  Bang,
  OpenParen,
  CloseParen,
  /// <summary>"=" (assignment or comparison).</summary>
  Equals,
  /// <summary>":=" after a named argument.</summary>
  ColonEquals,
  /// <summary>A string, date or number literal.</summary>
  Literal,
  /// <summary>Anything else: operators, commas.</summary>
  Other,
}

/// <param name="Text">For identifiers, the name without any type-declaration character or [brackets].</param>
public readonly record struct Token(TokenKind Kind, string Text)
{
  public bool IsKeyword(string keyword) => Kind == TokenKind.Identifier && Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Splits one statement (comments already removed by the parser) into tokens. Only the tokens needed to find
/// references are distinguished; everything else is <see cref="TokenKind.Other"/>.
/// </summary>
public static class StatementTokeniser
{
  const string TypeSuffixes = "%&!#@$^";

  public static List<Token> Tokenise(string statement)
  {
    var tokens = new List<Token>();
    int i = 0;
    while (i < statement.Length)
    {
      char c = statement[i];

      int literalEnd = LogicalLineReader.LiteralEnd(statement, i);
      if (literalEnd > i)
      {
        tokens.Add(new Token(TokenKind.Literal, statement[i..literalEnd]));
        i = literalEnd;
      }
      else if (char.IsWhiteSpace(c))
      {
        i++;
      }
      else if (char.IsLetter(c))
      {
        int start = i;
        while (i < statement.Length && IsIdentifierChar(statement[i])) i++;
        var name = statement[start..i];
        // A type-declaration character belongs to the name (s$), unless it is really an operator:
        // "rs!Field" is dictionary access, and "a&b" is concatenation.
        if (i < statement.Length && TypeSuffixes.Contains(statement[i]) && (i + 1 >= statement.Length || !IsIdentifierChar(statement[i + 1])))
          i++;
        tokens.Add(new Token(TokenKind.Identifier, name));
      }
      else if (c == '[')
      {
        int close = statement.IndexOf(']', i);
        if (close < 0) close = statement.Length - 1;
        tokens.Add(new Token(TokenKind.Identifier, statement[(i + 1)..close]));
        i = close + 1;
      }
      else if (char.IsDigit(c) || (c == '.' && i + 1 < statement.Length && char.IsDigit(statement[i + 1])))
      {
        int start = i;
        while (i < statement.Length && (char.IsLetterOrDigit(statement[i]) || statement[i] == '.')) i++;
        if (i < statement.Length && TypeSuffixes.Contains(statement[i])) i++;
        tokens.Add(new Token(TokenKind.Literal, statement[start..i]));
      }
      else if (c == '&' && i + 2 < statement.Length && "HhOo".Contains(statement[i + 1]) && char.IsAsciiHexDigit(statement[i + 2]))
      {
        int start = i;
        i += 2;
        while (i < statement.Length && char.IsAsciiHexDigit(statement[i])) i++;
        if (i < statement.Length && TypeSuffixes.Contains(statement[i])) i++;
        tokens.Add(new Token(TokenKind.Literal, statement[start..i]));
      }
      else
      {
        var (kind, length) = c switch
        {
          '.' => (TokenKind.Dot, 1),
          '!' => (TokenKind.Bang, 1),
          '(' => (TokenKind.OpenParen, 1),
          ')' => (TokenKind.CloseParen, 1),
          '=' => (TokenKind.Equals, 1),
          ':' when i + 1 < statement.Length && statement[i + 1] == '=' => (TokenKind.ColonEquals, 2),
          _ => (TokenKind.Other, 1),
        };
        tokens.Add(new Token(kind, statement.Substring(i, length)));
        i += length;
      }
    }
    return tokens;
  }

  static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
