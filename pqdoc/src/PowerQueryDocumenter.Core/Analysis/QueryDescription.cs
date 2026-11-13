using System.Text;

namespace PowerQueryDocumenter.Core.Analysis;

/// <summary>
/// What a query says about itself (FR-HTML-24): its description in the workbook, the one Excel shows in the query's
/// properties, otherwise the comments at the top of its code, before anything else.
/// </summary>
public static class QueryDescription
{
  /// <summary>The most a description holds, so a page doesn't carry a history of changes written as comments.</summary>
  public const int MaxLength = 1000;

  /// <summary>The workbook's description if it has one, otherwise the comments at the top of the query's code.</summary>
  public static string? Of(string? workbookDescription, string? code) =>
    Tidy(workbookDescription) ?? (code is null ? null : FromComments(code));

  /// <summary>
  /// The text of the comments a query's code starts with: <c>//</c> lines and <c>/* … */</c> blocks, up to the first
  /// thing that isn't a comment. Null when it starts with code.
  /// </summary>
  public static string? FromComments(string code)
  {
    ArgumentNullException.ThrowIfNull(code);
    var text = new StringBuilder();
    var at = 0;
    while (true)
    {
      while (at < code.Length && char.IsWhiteSpace(code[at]))
      {
        at++;
      }

      if (at + 1 >= code.Length || code[at] != '/')
      {
        break;
      }

      if (code[at + 1] == '/')
      {
        var end = code.IndexOfAny(['\r', '\n'], at);
        end = end < 0 ? code.Length : end;
        text.Append(code, at + 2, end - at - 2).Append('\n');
        at = end;
      }
      else if (code[at + 1] == '*')
      {
        var end = code.IndexOf("*/", at + 2, StringComparison.Ordinal);
        var close = end < 0 ? code.Length : end;
        foreach (var line in code[(at + 2)..close].ReplaceLineEndings("\n").Split('\n'))
        {
          // A block's lines often start with a star of their own.
          text.Append(line.Trim().TrimStart('*')).Append('\n');
        }

        at = end < 0 ? code.Length : end + 2;
      }
      else
      {
        break;
      }
    }

    return Tidy(text.ToString());
  }

  /// <summary>Trimmed, with lines of decoration and runs of empty lines as one break, cut to <see cref="MaxLength"/>.</summary>
  private static string? Tidy(string? text)
  {
    if (text is null)
    {
      return null;
    }

    var kept = new List<string>();
    foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
    {
      var line = raw.Trim();
      if (!line.Any(char.IsLetterOrDigit))
      {
        line = string.Empty;
      }

      if (line.Length == 0 && (kept.Count == 0 || kept[^1].Length == 0))
      {
        continue;
      }

      kept.Add(line);
    }

    var tidy = string.Join("\n", kept).Trim();
    if (tidy.Length == 0)
    {
      return null;
    }

    return tidy.Length > MaxLength ? tidy[..(MaxLength - 1)].TrimEnd() + "…" : tidy;
  }
}
