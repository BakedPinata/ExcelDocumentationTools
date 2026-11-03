using System.Text;

namespace PowerQueryDocumenter.Core.Output;

/// <summary>
/// The text of a query file (FR-QF-3). Excel stores some queries with CRLF and others with LF, so line endings are
/// made CRLF, matching vbadoc's files. Line breaks inside a text literal are part of the text's value, though, so they
/// stay exactly as Excel stored them, and the text is unchanged if the query is ever imported back into a workbook.
/// </summary>
/// <remarks>
/// This walks the text much as the tokeniser does: a quote inside a comment doesn't start a text literal, and
/// <c>""</c> inside a literal is an escaped quote. Quoted identifiers (<c>#"…"</c>) follow the same rules as text,
/// though a query name can't contain a line break anyway. An unterminated literal runs to the end.
/// </remarks>
public static class QueryFileText
{
  /// <summary>The bytes of the file for an expression: UTF-8 with no byte order mark.</summary>
  public static byte[] Bytes(string expression) => new UTF8Encoding(false).GetBytes(WithCrlfOutsideText(expression));

  /// <summary>Makes every line ending CRLF, except inside text literals.</summary>
  public static string WithCrlfOutsideText(string m)
  {
    ArgumentNullException.ThrowIfNull(m);
    var result = new StringBuilder(m.Length + 32);
    var i = 0;
    while (i < m.Length)
    {
      var c = m[i];
      var next = i + 1 < m.Length ? m[i + 1] : '\0';
      if (c == '"')
      {
        var end = EndOfQuoted(m, i);
        result.Append(m, i, end - i);
        i = end;
      }
      else if (c == '/' && next == '/')
      {
        // A line comment runs to the end of its line, which the loop then handles like any other.
        var end = i + 2;
        while (end < m.Length && m[end] is not ('\r' or '\n'))
        {
          end++;
        }

        result.Append(m, i, end - i);
        i = end;
      }
      else if (c == '/' && next == '*')
      {
        var close = m.IndexOf("*/", i + 2, StringComparison.Ordinal);
        var end = close < 0 ? m.Length : close + 2;
        AppendWithCrlf(result, m, i, end);
        i = end;
      }
      else if (c is '\r' or '\n')
      {
        result.Append("\r\n");
        i += c == '\r' && next == '\n' ? 2 : 1;
      }
      else
      {
        result.Append(c);
        i++;
      }
    }

    return result.ToString();
  }

  /// <summary>The index just after the quote that closes the one at <paramref name="opening"/>.</summary>
  private static int EndOfQuoted(string m, int opening)
  {
    var i = opening + 1;
    while (i < m.Length)
    {
      if (m[i] == '"')
      {
        if (i + 1 < m.Length && m[i + 1] == '"')
        {
          i += 2;
          continue;
        }

        return i + 1;
      }

      i++;
    }

    return m.Length;
  }

  private static void AppendWithCrlf(StringBuilder result, string m, int start, int end)
  {
    for (var i = start; i < end; i++)
    {
      if (m[i] is '\r' or '\n')
      {
        result.Append("\r\n");
        if (m[i] == '\r' && i + 1 < end && m[i + 1] == '\n')
        {
          i++;
        }
      }
      else
      {
        result.Append(m[i]);
      }
    }
  }
}
