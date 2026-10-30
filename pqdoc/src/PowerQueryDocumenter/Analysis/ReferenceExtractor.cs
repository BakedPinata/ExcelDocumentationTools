namespace PowerQueryDocumenter.Analysis;

/// <summary>
/// Finds the identifiers an M document refers to that are not defined inside the document itself
/// (its "free" identifiers). Free identifiers are the candidates for references to other queries.
/// </summary>
/// <remarks>
/// <para>
/// This is a lightweight structural pass over the tokens, not a full M parser. It understands
/// enough of the language to avoid the common false positives of a plain text search:
/// </para>
/// <list type="bullet">
/// <item><c>let</c> step names and record field names are definitions, not references, and any
/// use of them within their <c>let</c> or record is local. A query step called <c>Customers</c>
/// therefore does not create a dependency on a query called <c>Customers</c>.</item>
/// <item>Function parameters, including the implicit <c>_</c> of <c>each</c>, are local to the
/// function body.</item>
/// <item>Field access and projection (<c>[Amount]</c>, <c>Source{0}[Data]</c>,
/// <c>[[A], [B]]</c>) name record fields, never queries.</item>
/// <item>Comments and text literals are already removed by <see cref="MTokeniser"/>.</item>
/// </list>
/// <para>
/// The scope rules are approximations: for example, a function or <c>let</c> body is assumed to
/// end at the next comma or closing bracket that belongs to an enclosing construct. Unusual code
/// can still produce a false positive or negative, which is acceptable for a dependency overview.
/// </para>
/// </remarks>
public static class ReferenceExtractor
{
  private enum FrameKind
  {
    Root,
    Let,
    LetBody,
    Record,
    Function,
    Group,
  }

  /// <summary>Tokenises <paramref name="mCode"/> and returns its free identifiers.</summary>
  public static IReadOnlySet<string> FindFreeIdentifiers(string mCode) =>
    FindFreeIdentifiers(MTokeniser.Tokenise(mCode));

  /// <summary>
  /// Returns the names of bare and quoted identifiers that are used but not defined locally,
  /// spelled as written (M is case-sensitive). Quoted identifiers are returned unescaped, so
  /// <c>#"Sales Data"</c> becomes <c>Sales Data</c>.
  /// </summary>
  public static IReadOnlySet<string> FindFreeIdentifiers(IReadOnlyList<MToken> tokens)
  {
    ArgumentNullException.ThrowIfNull(tokens);

    var uses = new List<(string Name, Scope Scope)>();
    var frames = new Stack<Frame>();
    frames.Push(new Frame(FrameKind.Root, new Scope(null), closer: null));

    for (var i = 0; i < tokens.Count; i++)
    {
      var token = tokens[i];

      // A comma, closing bracket, semicolon or "in" ends any function body or let body that is
      // still open, because those delimiters belong to an enclosing construct.
      if (IsBodyTerminator(token))
      {
        PopImplicitFrames(frames);
      }

      var frame = frames.Peek();

      if (token.IsKeyword("let"))
      {
        frames.Push(new Frame(FrameKind.Let, new Scope(frame.Scope), closer: null) { ExpectingName = true });
      }
      else if (token.IsKeyword("in"))
      {
        if (frame.Kind == FrameKind.Let)
        {
          // The body after "in" can see every step, so it keeps the let's scope.
          frame.Kind = FrameKind.LetBody;
          frame.ExpectingName = false;
        }
      }
      else if (token.IsKeyword("each"))
      {
        var scope = new Scope(frame.Scope);
        scope.Names.Add("_");
        frames.Push(new Frame(FrameKind.Function, scope, closer: null));
      }
      else if (token.IsKeyword("shared") && IsDefinition(tokens, i + 1))
      {
        // Section member definition ("shared Name = ..."): the name is not a reference.
        i += 2;
      }
      else if (token.IsKeyword("section"))
      {
        // Section document header ("section Section1;"): the section name is not a reference.
        if (i + 1 < tokens.Count && tokens[i + 1].IsIdentifier)
        {
          i++;
        }
      }
      else if (token.IsPunctuator("["))
      {
        var close = FindClosingBracket(tokens, i);
        if (IsRecordLiteral(tokens, i, close))
        {
          frames.Push(new Frame(FrameKind.Record, new Scope(frame.Scope), closer: "]") { ExpectingName = true });
        }
        else
        {
          // Field access or projection: everything inside names a field, never a query.
          i = close;
        }
      }
      else if (token.IsPunctuator("("))
      {
        var close = FindClosingBracket(tokens, i);
        var arrow = FindFunctionArrow(tokens, close);
        if (arrow >= 0)
        {
          var scope = new Scope(frame.Scope);
          scope.Names.UnionWith(ReadParameterNames(tokens, i, close));
          frames.Push(new Frame(FrameKind.Function, scope, closer: null));
          i = arrow;
        }
        else
        {
          frames.Push(new Frame(FrameKind.Group, frame.Scope, closer: ")"));
        }
      }
      else if (token.IsPunctuator("{"))
      {
        frames.Push(new Frame(FrameKind.Group, frame.Scope, closer: "}"));
      }
      else if (token.Kind == MTokenKind.Punctuator && token.Text is ")" or "]" or "}")
      {
        PopThrough(frames, token.Text);
      }
      else if (token.IsPunctuator(","))
      {
        if (frame.Kind is FrameKind.Let or FrameKind.Record)
        {
          frame.ExpectingName = true;
        }
      }
      else if (frame.ExpectingName && TryReadDefinition(tokens, i, frame.Kind, out var name, out var equalsIndex))
      {
        frame.ExpectingName = false;
        frame.Scope.Names.Add(name);
        i = equalsIndex;
      }
      else
      {
        frame.ExpectingName = false;
        if (token.IsIdentifier)
        {
          uses.Add((token.Text, frame.Scope));
        }
      }
    }

    var free = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (name, scope) in uses)
    {
      if (!scope.Defines(name))
      {
        free.Add(name);
      }
    }

    return free;
  }

  private static bool IsBodyTerminator(MToken token) =>
    token.IsKeyword("in")
    || (token.Kind == MTokenKind.Punctuator && token.Text is "," or ")" or "]" or "}" or ";");

  private static void PopImplicitFrames(Stack<Frame> frames)
  {
    while (frames.Count > 1 && frames.Peek().Kind is FrameKind.Function or FrameKind.LetBody)
    {
      frames.Pop();
    }
  }

  /// <summary>Pops frames up to and including the one opened by the bracket that <paramref name="closer"/> closes.</summary>
  private static void PopThrough(Stack<Frame> frames, string closer)
  {
    // Ignore an unbalanced closing bracket rather than unwinding everything.
    if (!frames.Any(f => f.Closer == closer))
    {
      return;
    }

    while (frames.Count > 1)
    {
      if (frames.Pop().Closer == closer)
      {
        return;
      }
    }
  }

  /// <summary>Index of the bracket matching the one at <paramref name="open"/>, or the last index if unbalanced.</summary>
  private static int FindClosingBracket(IReadOnlyList<MToken> tokens, int open)
  {
    var depth = 0;
    for (var i = open; i < tokens.Count; i++)
    {
      var token = tokens[i];
      if (token.Kind != MTokenKind.Punctuator)
      {
        continue;
      }

      if (token.Text is "(" or "[" or "{")
      {
        depth++;
      }
      else if (token.Text is ")" or "]" or "}")
      {
        depth--;
        if (depth == 0)
        {
          return i;
        }
      }
    }

    return tokens.Count - 1;
  }

  /// <summary>A "[" opens a record literal (or record type) when an "=" appears directly inside it.</summary>
  private static bool IsRecordLiteral(IReadOnlyList<MToken> tokens, int open, int close)
  {
    if (close == open + 1)
    {
      return true; // [] is the empty record
    }

    var depth = 0;
    for (var i = open + 1; i < close; i++)
    {
      var token = tokens[i];
      if (token.Kind != MTokenKind.Punctuator)
      {
        continue;
      }

      if (token.Text is "(" or "[" or "{")
      {
        depth++;
      }
      else if (token.Text is ")" or "]" or "}")
      {
        depth--;
      }
      else if (depth == 0 && token.Text == "=")
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// When the parenthesised list ending at <paramref name="close"/> is a function's parameter list
  /// ("(x) =>" or "(x) as number =>"), returns the index of the "=>"; otherwise -1.
  /// </summary>
  private static int FindFunctionArrow(IReadOnlyList<MToken> tokens, int close)
  {
    var next = close + 1;
    if (next >= tokens.Count)
    {
      return -1;
    }

    if (tokens[next].IsPunctuator("=>"))
    {
      return next;
    }

    if (!tokens[next].IsKeyword("as"))
    {
      return -1;
    }

    // Return type: "as number =>", "as nullable text =>", "as Int64.Type =>".
    for (var i = next + 1; i < tokens.Count && i <= next + 3; i++)
    {
      if (tokens[i].IsPunctuator("=>"))
      {
        return i;
      }

      if (tokens[i].Kind == MTokenKind.Punctuator)
      {
        return -1;
      }
    }

    return -1;
  }

  private static List<string> ReadParameterNames(IReadOnlyList<MToken> tokens, int open, int close)
  {
    var names = new List<string>();
    var depth = 0;
    var atParameterStart = true;

    for (var i = open + 1; i < close; i++)
    {
      var token = tokens[i];
      if (token.Kind == MTokenKind.Punctuator)
      {
        if (token.Text is "(" or "[" or "{")
        {
          depth++;
        }
        else if (token.Text is ")" or "]" or "}")
        {
          depth--;
        }
        else if (depth == 0 && token.Text == ",")
        {
          atParameterStart = true;
        }

        continue;
      }

      if (depth != 0 || !atParameterStart || !token.IsIdentifier)
      {
        continue;
      }

      // "optional x as text": skip the modifier and take the name that follows it.
      if (token.Text == "optional" && i + 1 < close && tokens[i + 1].IsIdentifier)
      {
        continue;
      }

      names.Add(token.Text);
      atParameterStart = false;
    }

    return names;
  }

  /// <summary>True when the tokens at <paramref name="index"/> are "Name =".</summary>
  private static bool IsDefinition(IReadOnlyList<MToken> tokens, int index) =>
    index + 1 < tokens.Count && tokens[index].IsIdentifier && tokens[index + 1].IsPunctuator("=");

  /// <summary>
  /// Reads a definition name at <paramref name="start"/>. A let step is a single (possibly quoted)
  /// identifier. A record field name may be a generalized identifier made of several words
  /// ("[Customer Name = ...]").
  /// </summary>
  private static bool TryReadDefinition(
    IReadOnlyList<MToken> tokens,
    int start,
    FrameKind kind,
    out string name,
    out int equalsIndex)
  {
    name = string.Empty;
    equalsIndex = -1;

    if (kind == FrameKind.Let)
    {
      if (!IsDefinition(tokens, start))
      {
        return false;
      }

      name = tokens[start].Text;
      equalsIndex = start + 1;
      return true;
    }

    if (kind != FrameKind.Record)
    {
      return false;
    }

    var end = start;
    while (end < tokens.Count
      && tokens[end].Kind is MTokenKind.Identifier or MTokenKind.QuotedIdentifier or MTokenKind.Keyword or MTokenKind.NumberLiteral)
    {
      end++;
    }

    if (end == start || end >= tokens.Count || !tokens[end].IsPunctuator("="))
    {
      return false;
    }

    name = string.Join(' ', Enumerable.Range(start, end - start).Select(i => tokens[i].Text));
    equalsIndex = end;
    return true;
  }

  private sealed class Scope(Scope? parent)
  {
    public Scope? Parent { get; } = parent;

    public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

    public bool Defines(string name)
    {
      for (var scope = this; scope is not null; scope = scope.Parent)
      {
        if (scope.Names.Contains(name))
        {
          return true;
        }
      }

      return false;
    }
  }

  private sealed class Frame(FrameKind kind, Scope scope, string? closer)
  {
    public FrameKind Kind { get; set; } = kind;

    public Scope Scope { get; } = scope;

    /// <summary>The bracket that closes this frame, or null for frames that end implicitly.</summary>
    public string? Closer { get; } = closer;

    /// <summary>True when the next tokens may be a definition ("Name =").</summary>
    public bool ExpectingName { get; set; }
  }
}
