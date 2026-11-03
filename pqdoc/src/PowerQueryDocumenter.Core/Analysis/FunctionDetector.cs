namespace PowerQueryDocumenter.Core.Analysis;

/// <summary>
/// Whether a query is a function, from its code, for a folder of <c>.m</c> files, which has no metadata to say so. A
/// workbook's metadata records it, so this is only needed for folders. Three forms count: a function literal,
/// <c>(x as text) as text =&gt; …</c>; <c>each …</c>; and a <c>let</c> whose result is one of its own steps defined as a
/// function, <c>let Source = (x) =&gt; … in Source</c>, which is what Excel's Create Function writes.
/// </summary>
public static class FunctionDetector
{
  /// <summary>True when the expression is a function.</summary>
  public static bool IsFunction(string expression)
  {
    ArgumentNullException.ThrowIfNull(expression);
    var tokens = MTokeniser.Tokenise(expression);
    if (tokens.Count == 0)
    {
      return false;
    }

    return StartsFunction(tokens, 0) || IsLetReturningFunction(tokens);
  }

  /// <summary>Whether a function starts at <paramref name="start"/>: <c>each</c>, or a parameter list and <c>=&gt;</c>.</summary>
  private static bool StartsFunction(IReadOnlyList<MToken> tokens, int start)
  {
    if (start >= tokens.Count)
    {
      return false;
    }

    if (tokens[start].IsKeyword("each"))
    {
      return true;
    }

    if (!tokens[start].IsPunctuator("("))
    {
      return false;
    }

    var close = Matching(tokens, start);
    if (close < 0 || close + 1 >= tokens.Count)
    {
      return false;
    }

    if (tokens[close + 1].IsPunctuator("=>"))
    {
      return true;
    }

    // A return type: "as text =>" or "as nullable text =>".
    if (tokens[close + 1].IsKeyword("as"))
    {
      for (var i = close + 2; i < tokens.Count && i <= close + 4; i++)
      {
        if (tokens[i].IsPunctuator("=>"))
        {
          return true;
        }

        if (!tokens[i].IsIdentifier)
        {
          return false;
        }
      }
    }

    return false;
  }

  /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or -1.</summary>
  private static int Matching(IReadOnlyList<MToken> tokens, int open)
  {
    var depth = 0;
    for (var i = open; i < tokens.Count; i++)
    {
      depth += Nesting(tokens[i]);
      if (depth == 0)
      {
        return i;
      }
    }

    return -1;
  }

  /// <summary>How a token changes the nesting: brackets, and <c>let</c> … <c>in</c>.</summary>
  private static int Nesting(MToken token) =>
    token.Kind == MTokenKind.Punctuator && token.Text is "(" or "[" or "{" ? 1
    : token.Kind == MTokenKind.Punctuator && token.Text is ")" or "]" or "}" ? -1
    : token.IsKeyword("let") ? 1
    : token.IsKeyword("in") ? -1
    : 0;

  /// <summary>
  /// <c>let … in Name</c>, where <c>Name</c> is one of the let's own steps and that step is a function. Steps are found
  /// at the let's own level, so ones inside nested lets, records and lists don't count.
  /// </summary>
  private static bool IsLetReturningFunction(IReadOnlyList<MToken> tokens)
  {
    if (!tokens[0].IsKeyword("let"))
    {
      return false;
    }

    var steps = new Dictionary<string, int>(StringComparer.Ordinal);
    var depth = 0;
    var expectingStep = true;
    for (var i = 1; i < tokens.Count; i++)
    {
      var token = tokens[i];
      if (depth == 0 && token.IsKeyword("in"))
      {
        // The result must be a single name, ending the expression.
        return i == tokens.Count - 2
          && tokens[i + 1].IsIdentifier
          && steps.TryGetValue(tokens[i + 1].Text, out var stepStart)
          && StartsFunction(tokens, stepStart);
      }

      if (depth == 0 && expectingStep && token.IsIdentifier && i + 1 < tokens.Count && tokens[i + 1].IsPunctuator("="))
      {
        steps.TryAdd(token.Text, i + 2);
        expectingStep = false;
        i++;
        continue;
      }

      if (depth == 0 && token.IsPunctuator(","))
      {
        expectingStep = true;
        continue;
      }

      depth += Nesting(token);
      if (depth < 0)
      {
        return false;
      }
    }

    return false;
  }
}
