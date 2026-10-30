using System.Globalization;
using System.Text;

namespace PowerQueryDocumenter.Analysis;

/// <summary>
/// A small, forgiving tokenizer for Power Query M (FR-ANA-1). It is not a full parser: its job is
/// to separate identifiers from comments and text literals so that names mentioned in comments
/// or strings are never mistaken for query references. Malformed input never throws; an
/// unterminated comment, text literal or quoted identifier simply runs to the end of the input.
/// </summary>
public static class MTokenizer
{
  // Longest first, so "..." wins over "..".
  private static readonly string[] MultiCharacterPunctuators = ["...", "=>", "..", "<=", ">=", "<>", "??"];

  /// <summary>Splits M source text into tokens. Whitespace and comments are discarded.</summary>
  public static IReadOnlyList<MToken> Tokenize(string text)
  {
    ArgumentNullException.ThrowIfNull(text);

    var tokens = new List<MToken>();
    var i = 0;

    while (i < text.Length)
    {
      var c = text[i];
      var next = CharAt(text, i + 1);

      if (char.IsWhiteSpace(c))
      {
        i++;
      }
      else if (c == '/' && next == '/')
      {
        i = SkipLineComment(text, i);
      }
      else if (c == '/' && next == '*')
      {
        i = SkipBlockComment(text, i);
      }
      else if (c == '"')
      {
        var (value, end) = ReadQuoted(text, i);
        tokens.Add(new MToken(MTokenKind.TextLiteral, value, i));
        i = end;
      }
      else if (c == '#' && next == '"')
      {
        var (value, end) = ReadQuoted(text, i + 1);
        tokens.Add(new MToken(MTokenKind.QuotedIdentifier, value, i));
        i = end;
      }
      else if (c == '#' && IsIdentifierStart(next))
      {
        var end = i + 1;
        while (end < text.Length && IsIdentifierPart(text[end]))
        {
          end++;
        }

        tokens.Add(new MToken(MTokenKind.HashKeyword, text[i..end], i));
        i = end;
      }
      else if (IsIdentifierStart(c))
      {
        var end = ReadIdentifier(text, i);
        var word = text[i..end];
        var kind = MLanguage.IsKeyword(word) ? MTokenKind.Keyword : MTokenKind.Identifier;
        tokens.Add(new MToken(kind, word, i));
        i = end;
      }
      else if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(next)))
      {
        var end = ReadNumber(text, i);
        tokens.Add(new MToken(MTokenKind.NumberLiteral, text[i..end], i));
        i = end;
      }
      else
      {
        var punctuator = MultiCharacterPunctuators.FirstOrDefault(p => string.CompareOrdinal(text, i, p, 0, p.Length) == 0)
          ?? c.ToString();
        tokens.Add(new MToken(MTokenKind.Punctuator, punctuator, i));
        i += punctuator.Length;
      }
    }

    return tokens;
  }

  private static char CharAt(string text, int index) => index < text.Length ? text[index] : '\0';

  private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

  private static bool IsIdentifierPart(char c) =>
    char.IsLetterOrDigit(c)
    || c == '_'
    || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
      or UnicodeCategory.SpacingCombiningMark
      or UnicodeCategory.ConnectorPunctuation
      or UnicodeCategory.Format;

  private static int SkipLineComment(string text, int start)
  {
    var i = start + 2;
    while (i < text.Length && text[i] is not ('\n' or '\r' or '\u0085' or '\u2028' or '\u2029'))
    {
      i++;
    }

    return i;
  }

  private static int SkipBlockComment(string text, int start)
  {
    var close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
    return close < 0 ? text.Length : close + 2;
  }

  /// <summary>Reads a "..." sequence starting at the opening quote, with "" as an escaped quote.</summary>
  private static (string Value, int End) ReadQuoted(string text, int openingQuote)
  {
    var value = new StringBuilder();
    var i = openingQuote + 1;

    while (i < text.Length)
    {
      if (text[i] == '"')
      {
        if (CharAt(text, i + 1) == '"')
        {
          value.Append('"');
          i += 2;
          continue;
        }

        return (value.ToString(), i + 1);
      }

      value.Append(text[i]);
      i++;
    }

    return (value.ToString(), text.Length);
  }

  /// <summary>Reads a (possibly dotted) identifier such as <c>Table.SelectRows</c> or <c>Int64.Type</c>.</summary>
  private static int ReadIdentifier(string text, int start)
  {
    var end = start;
    while (true)
    {
      while (end < text.Length && IsIdentifierPart(text[end]))
      {
        end++;
      }

      // A dot joins identifier parts ("Table.SelectRows"), but ".." is the range operator.
      if (CharAt(text, end) == '.' && IsIdentifierStart(CharAt(text, end + 1)))
      {
        end++;
        continue;
      }

      return end;
    }
  }

  private static int ReadNumber(string text, int start)
  {
    var i = start;

    if (text[i] == '0' && CharAt(text, i + 1) is 'x' or 'X')
    {
      i += 2;
      while (i < text.Length && char.IsAsciiHexDigit(text[i]))
      {
        i++;
      }

      return i;
    }

    while (i < text.Length && char.IsAsciiDigit(text[i]))
    {
      i++;
    }

    if (CharAt(text, i) == '.' && char.IsAsciiDigit(CharAt(text, i + 1)))
    {
      i++;
      while (i < text.Length && char.IsAsciiDigit(text[i]))
      {
        i++;
      }
    }

    if (CharAt(text, i) is 'e' or 'E')
    {
      var exponent = i + 1;
      if (CharAt(text, exponent) is '+' or '-')
      {
        exponent++;
      }

      if (char.IsAsciiDigit(CharAt(text, exponent)))
      {
        i = exponent;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
          i++;
        }
      }
    }

    return i;
  }
}
