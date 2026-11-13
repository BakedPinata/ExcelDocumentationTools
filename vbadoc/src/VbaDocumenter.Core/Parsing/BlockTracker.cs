using System.Text.RegularExpressions;

namespace VbaDocumenter.Core.Parsing;

/// <summary>
/// Follows a procedure's block statements as its lines are read (step E3 of the feedback plan): If, ElseIf and Else
/// branches (a single-line If too), Select Case and each Case, For, Do and While loops, With, and On Error handlers.
/// Each statement gets the innermost block it's in; a block's own statement (its condition, say) is in the block around
/// it, since that's where it runs, and says which block it opens, so what its condition calls can be shown as that
/// block's.
/// </summary>
/// <remarks>
/// A single-line If is split into its parts, as a block If would be: "If x Then A Else B" becomes "If x Then", "A",
/// "Else" and "B", and everything after Then on the logical line belongs to it, colons or not, as in VBA. Under #If,
/// each branch starts from the blocks open at the #If, and after the #End If the first branch's blocks carry on: VBA
/// compiles one branch, and the first is the one shown first.
/// </remarks>
sealed partial class BlockTracker(Member procedure)
{
  const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

  [GeneratedRegex(@"^ElseIf\b", Opts)] private static partial Regex ElseIfStatement();
  [GeneratedRegex(@"^Else$", Opts)] private static partial Regex ElseStatement();
  [GeneratedRegex(@"^End\s+If$", Opts)] private static partial Regex EndIfStatement();
  [GeneratedRegex(@"^Select\s+Case\b", Opts)] private static partial Regex SelectStatement();
  [GeneratedRegex(@"^Case\b", Opts)] private static partial Regex CaseStatement();
  [GeneratedRegex(@"^End\s+Select$", Opts)] private static partial Regex EndSelectStatement();
  [GeneratedRegex(@"^For\b", Opts)] private static partial Regex ForStatement();
  [GeneratedRegex(@"^Next\b(?<vars>.*)$", Opts)] private static partial Regex NextStatement();
  [GeneratedRegex(@"^Do\b", Opts)] private static partial Regex DoStatement();
  [GeneratedRegex(@"^Loop\b", Opts)] private static partial Regex LoopStatement();
  [GeneratedRegex(@"^While\b", Opts)] private static partial Regex WhileStatement();
  [GeneratedRegex(@"^Wend$", Opts)] private static partial Regex WendStatement();
  [GeneratedRegex(@"^With\b", Opts)] private static partial Regex WithStatement();
  [GeneratedRegex(@"^End\s+With$", Opts)] private static partial Regex EndWithStatement();
  [GeneratedRegex(@"^On\s+Error\s+GoTo\s+(?<label>\w+)$", Opts)] private static partial Regex OnErrorGoTo();

  List<int> _open = [];                                              // ids of the open blocks, innermost last
  readonly List<(List<int> AtIf, List<int>? EndOfFirst)> _directives = []; // the #If blocks open now
  readonly HashSet<string> _handlers = new(StringComparer.OrdinalIgnoreCase); // labels On Error GoTo names

  int? Current => _open.Count > 0 ? _open[^1] : null;

  CodeBlock BlockOf(int id) => procedure.BlocksList[id];

  int Open(string kind, string text, int line, int? parent)
  {
    var id = procedure.BlocksList.Count;
    procedure.BlocksList.Add(new CodeBlock(id, kind, text, line, parent));
    _open.Add(id);
    return id;
  }

  // The innermost open block of one of the kinds, or null.
  int? Innermost(params string[] kinds)
  {
    for (int i = _open.Count - 1; i >= 0; i--)
      if (kinds.Contains(BlockOf(_open[i]).Kind)) return _open[i];
    return null;
  }

  // Closes a block and every block inside it.
  void Close(int id)
  {
    var at = _open.LastIndexOf(id);
    if (at >= 0) _open.RemoveRange(at, _open.Count - at);
  }

  /// <summary>A label at the start of a line: where an On Error handler starts, if On Error GoTo named it.</summary>
  public void Label(string name, int line)
  {
    if (!_handlers.Contains(name)) return;
    _open.Clear(); // a handler sits at the top of the procedure, after the code it handles
    Open("onError", "On error: " + name, line, null);
  }

  public void DirectiveIf() => _directives.Add(([.. _open], null));

  public void DirectiveElse()
  {
    if (_directives.Count == 0) return;
    var (atIf, endOfFirst) = _directives[^1];
    _directives[^1] = (atIf, endOfFirst ?? [.. _open]);
    _open = [.. atIf];
  }

  public void DirectiveEndIf()
  {
    if (_directives.Count == 0) return;
    var (_, endOfFirst) = _directives[^1];
    _directives.RemoveAt(_directives.Count - 1);
    if (endOfFirst != null) _open = endOfFirst;
  }

  /// <summary>
  /// The statements of a logical line's code, each with the innermost block it's in and, for a block's own statement,
  /// the block it opens (see <see cref="Statement.Opens"/>).
  /// </summary>
  public List<(string Text, int? Block, int? Opens)> Statements(string code, int line)
  {
    var result = new List<(string, int?, int?)>();
    var statements = LogicalLineReader.SplitStatements(code);
    for (int i = 0; i < statements.Count; i++)
    {
      if (SingleLineIf(statements[i], out var header, out var rest))
      {
        // Everything after Then on the logical line is the If's.
        SingleLine(header, string.Join(": ", statements.Skip(i + 1).Prepend(rest)), line, result);
        break;
      }
      Statement(statements[i], line, result);
    }
    return result;
  }

  void SingleLine(string header, string tail, int line, List<(string, int?, int?)> result)
  {
    var parent = Current;
    var elseAt = FindKeyword(tail, "Else", 0);
    var then = Open("if", header, line, parent);
    result.Add((header, parent, then));
    result.AddRange(Statements((elseAt < 0 ? tail : tail[..elseAt]).Trim(), line));
    Close(then);
    if (elseAt < 0) return;
    result.Add(("Else", parent, null));
    var otherwise = Open("else", "Else", line, parent);
    result.AddRange(Statements(tail[(elseAt + 4)..].Trim(), line));
    Close(otherwise);
  }

  void Statement(string s, int line, List<(string, int?, int?)> result)
  {
    if (s.Length == 0) return;
    if (IsBlockIf(s))
    {
      var parent = Current;
      result.Add((s, parent, Open("if", s, line, parent)));
    }
    else if (ElseIfStatement().IsMatch(s) || ElseStatement().IsMatch(s))
    {
      var branch = Innermost("if", "elseIf", "else");
      var parent = branch is { } b ? BlockOf(b).Parent : Current;
      if (branch is { } open) Close(open);
      // An Else has nothing of its own to work out.
      if (ElseStatement().IsMatch(s))
      {
        result.Add((s, parent, null));
        Open("else", s, line, parent);
      }
      else result.Add((s, parent, Open("elseIf", s, line, parent)));
    }
    else if (EndIfStatement().IsMatch(s)) CloseWith(s, result, "if", "elseIf", "else");
    else if (SelectStatement().IsMatch(s)) OpenWith(s, line, result, "select");
    else if (CaseStatement().IsMatch(s) && Innermost("select") is { } select)
    {
      // A Case ends the one before it, inside the same Select Case.
      _open.RemoveRange(_open.IndexOf(select) + 1, _open.Count - _open.IndexOf(select) - 1);
      result.Add((s, select, Open("case", s, line, select)));
    }
    else if (EndSelectStatement().IsMatch(s)) CloseWith(s, result, "select");
    else if (ForStatement().IsMatch(s)) OpenWith(s, line, result, "for");
    else if (NextStatement().Match(s) is { Success: true } next)
    {
      result.Add((s, Current, null));
      // "Next i, j" ends two loops.
      for (int n = next.Groups["vars"].Value.Count(c => c == ',') + 1; n > 0 && Innermost("for") is { } loop; n--) Close(loop);
    }
    else if (DoStatement().IsMatch(s)) OpenWith(s, line, result, "do");
    else if (LoopStatement().IsMatch(s)) CloseWith(s, result, "do");
    else if (WhileStatement().IsMatch(s)) OpenWith(s, line, result, "while");
    else if (WendStatement().IsMatch(s)) CloseWith(s, result, "while");
    else if (WithStatement().IsMatch(s)) OpenWith(s, line, result, "with");
    else if (EndWithStatement().IsMatch(s)) CloseWith(s, result, "with");
    else
    {
      if (OnErrorGoTo().Match(s) is { Success: true } onError && onError.Groups["label"].Value is not ("0" or "-1"))
        _handlers.Add(onError.Groups["label"].Value);
      result.Add((s, Current, null));
    }
  }

  // A block's own statement is in the block around it, and opens the block.
  void OpenWith(string s, int line, List<(string, int?, int?)> result, string kind)
  {
    var parent = Current;
    result.Add((s, parent, Open(kind, s, line, parent)));
  }

  // A closing statement (End If, Loop While …) is the block's last, so it's inside it.
  void CloseWith(string s, List<(string, int?, int?)> result, params string[] kinds)
  {
    result.Add((s, Current, null));
    if (Innermost(kinds) is { } block) Close(block);
  }

  static bool IsBlockIf(string s) =>
      StartsWithWord(s, "If") && FindKeyword(s, "Then", 2) is var then && then >= 0 && s[(then + 4)..].Trim().Length == 0;

  static bool SingleLineIf(string s, out string header, out string rest)
  {
    header = rest = "";
    if (!StartsWithWord(s, "If")) return false;
    var then = FindKeyword(s, "Then", 2);
    if (then < 0) return false;
    rest = s[(then + 4)..].Trim();
    header = s[..(then + 4)];
    return rest.Length > 0;
  }

  static bool StartsWithWord(string s, string word) =>
      s.StartsWith(word, StringComparison.OrdinalIgnoreCase) && (s.Length == word.Length || !IsNameChar(s[word.Length]));

  /// <summary>Where a keyword is as a whole word, outside string and date literals, from <paramref name="start"/>; -1 if it isn't.</summary>
  static int FindKeyword(string s, string keyword, int start)
  {
    for (int i = start; i < s.Length; i++)
    {
      int literalEnd = LogicalLineReader.LiteralEnd(s, i);
      if (literalEnd > i) { i = literalEnd - 1; continue; }
      if (string.Compare(s, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
          && (i == 0 || !IsNameChar(s[i - 1]))
          && (i + keyword.Length == s.Length || !IsNameChar(s[i + keyword.Length])))
        return i;
    }
    return -1;
  }

  static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
