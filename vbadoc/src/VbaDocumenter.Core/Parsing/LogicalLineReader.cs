using System.Text;

namespace VbaDocumenter.Core.Parsing;

/// <summary>
/// One logical line of VBA: physical lines joined at " _" continuations, with the comment split off.
/// </summary>
/// <param name="Code">The code part with runs of whitespace collapsed (outside literals) and trimmed. Empty if there is none.</param>
/// <param name="Comment">The comment text after <c>'</c> or <c>Rem</c>, or null if the line has no comment.</param>
/// <param name="FirstLine">1-based number of the first physical line.</param>
/// <param name="LastLine">1-based number of the last physical line (differs from FirstLine for continued lines).</param>
public sealed record LogicalLine(string Code, string? Comment, int FirstLine, int LastLine)
{
  public bool IsBlank => Code.Length == 0 && Comment == null;
  public bool IsCommentOnly => Code.Length == 0 && Comment != null;
}

/// <summary>
/// Splits VBA source into logical lines and statements. This is the only place that needs to know about
/// string literals, date literals, comments and line continuations, so the parser can work on clean text.
/// </summary>
public static class LogicalLineReader
{
  public static List<LogicalLine> Read(string text)
  {
    var physical = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    if (physical.Count > 0 && physical[^1].Length == 0) physical.RemoveAt(physical.Count - 1); // after the final newline

    var result = new List<LogicalLine>();
    var joined = new StringBuilder();
    int first = 1;

    for (int i = 0; i < physical.Count; i++)
    {
      if (joined.Length == 0) first = i + 1;
      var line = physical[i];

      // " _" at the end continues the line. This applies inside comments too, as in the VBA editor.
      var trimmed = line.TrimEnd();
      if (IsContinued(trimmed) && i + 1 < physical.Count)
      {
        joined.Append(trimmed, 0, trimmed.Length - 1).Append(' ');
        continue;
      }

      joined.Append(line);
      var (code, comment) = SplitComment(joined.ToString());
      result.Add(new LogicalLine(CollapseWhitespace(code), comment, first, i + 1));
      joined.Clear();
    }
    return result;
  }

  /// <summary>
  /// Splits a logical line's code at ":" statement separators, ignoring ":" in literals and in ":=" named arguments.
  /// </summary>
  public static List<string> SplitStatements(string code)
  {
    var statements = new List<string>();
    int start = 0;
    for (int i = 0; i < code.Length; i++)
    {
      int literalEnd = LiteralEnd(code, i);
      if (literalEnd > i) { i = literalEnd - 1; continue; }

      if (code[i] == ':' && (i + 1 >= code.Length || code[i + 1] != '='))
      {
        statements.Add(code[start..i]);
        start = i + 1;
      }
    }
    statements.Add(code[start..]);
    return statements.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
  }

  /// <summary>
  /// If a string or date literal starts at <paramref name="i"/>, returns the index just past it; otherwise returns <paramref name="i"/>.
  /// </summary>
  public static int LiteralEnd(string s, int i)
  {
    if (s[i] == '"')
    {
      // A doubled quote ("") inside a string is an escaped quote, not the end.
      int j = i + 1;
      while (j < s.Length)
      {
        if (s[j] == '"')
        {
          if (j + 1 < s.Length && s[j + 1] == '"') { j += 2; continue; }
          return j + 1;
        }
        j++;
      }
      return s.Length; // unterminated: treat the rest as string
    }

    if (s[i] == '#' && IsDateLiteralStart(s, i))
    {
      int close = s.IndexOf('#', i + 1);
      return close + 1;
    }
    return i;
  }

  static bool IsContinued(string trimmedLine) =>
      trimmedLine.EndsWith('_') && (trimmedLine.Length == 1 || char.IsWhiteSpace(trimmedLine[^2]));

  /// <summary>
  /// Date literals such as #12:30:00 PM# matter only because they can contain ':', which must not split statements.
  /// '#' is also a type suffix (x#) and a file number (Print #1, ...), so only treat it as a date literal if it is
  /// not attached to a name and the text up to the next '#' looks like a date or time.
  /// </summary>
  static bool IsDateLiteralStart(string s, int i)
  {
    if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_' || s[i - 1] == ')')) return false;
    int close = s.IndexOf('#', i + 1);
    if (close < 0) return false;
    var inner = s.AsSpan(i + 1, close - i - 1);
    bool hasDigit = false;
    foreach (char c in inner)
    {
      if (char.IsDigit(c)) hasDigit = true;
      else if (!(char.IsLetter(c) || c is '/' or ':' or '-' or ',' or '.' or ' ')) return false;
    }
    return hasDigit;
  }

  /// <summary>Finds a comment: "'" outside literals, or "Rem" at the start of a statement.</summary>
  static (string code, string? comment) SplitComment(string line)
  {
    bool atStatementStart = true;
    for (int i = 0; i < line.Length; i++)
    {
      char c = line[i];
      if (atStatementStart && char.IsWhiteSpace(c)) continue;

      if (atStatementStart && IsRemAt(line, i))
        return (line[..i], line[Math.Min(i + 4, line.Length)..]);
      atStatementStart = false;

      int literalEnd = LiteralEnd(line, i);
      if (literalEnd > i) { i = literalEnd - 1; continue; }

      if (c == '\'') return (line[..i], line[(i + 1)..]);
      if (c == ':' && (i + 1 >= line.Length || line[i + 1] != '=')) atStatementStart = true;
    }
    return (line, null);
  }

  static bool IsRemAt(string s, int i) =>
      string.Compare(s, i, "Rem", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
      && (i + 3 == s.Length || char.IsWhiteSpace(s[i + 3]));

  static string CollapseWhitespace(string code)
  {
    var sb = new StringBuilder(code.Length);
    for (int i = 0; i < code.Length; i++)
    {
      int literalEnd = LiteralEnd(code, i);
      if (literalEnd > i)
      {
        sb.Append(code, i, literalEnd - i);
        i = literalEnd - 1;
      }
      else if (char.IsWhiteSpace(code[i]))
      {
        if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
      }
      else
      {
        sb.Append(code[i]);
      }
    }
    return sb.ToString().TrimEnd();
  }
}
