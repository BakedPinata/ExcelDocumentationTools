using ExcelDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Analysis;

namespace PowerQueryDocumenter.Core.Extraction;

/// <summary>A query in a section document, and where its member is, from its attributes to its <c>;</c>.</summary>
/// <param name="Query">The query.</param>
/// <param name="Start">Where the member starts.</param>
/// <param name="End">Just after the member's <c>;</c>.</param>
internal sealed record SectionMember(WorkbookQuery Query, int Start, int End);

/// <summary>
/// Splits a section document (a workbook's <c>Section1.m</c>) into its queries. It uses pqdoc's tokeniser rather than
/// a regular expression, so a <c>;</c> or a line starting <c>shared</c> inside a text literal or a comment can't be
/// mistaken for the end or start of a query.
/// </summary>
/// <remarks>
/// A section document is <c>section Section1;</c> followed by members, each <c>[attributes] shared Name = expression;</c>.
/// The attribute record is optional, and is where Excel keeps a query's description. A member should always be
/// <c>shared</c>, but one without it is still kept as a query. Outside text and comments, M only uses <c>;</c> to end a
/// section member, so each <c>;</c> token ends one. A section that can't be split stops the run rather than leaving
/// queries out, because a missing query would later delete its <c>.m</c> file.
/// </remarks>
public static class SectionSplitter
{
  /// <summary>Splits <paramref name="section"/> into its queries, in the order they appear.</summary>
  /// <exception cref="ExtractionException">The section can't be split, or names a query twice.</exception>
  public static IReadOnlyList<WorkbookQuery> Split(string section) => [.. Members(section).Select(m => m.Query)];

  /// <summary>The section's queries, with where each member is in its text.</summary>
  /// <exception cref="ExtractionException">The section can't be split, or names a query twice.</exception>
  internal static IReadOnlyList<SectionMember> Members(string section)
  {
    ArgumentNullException.ThrowIfNull(section);
    var tokens = MTokeniser.Tokenise(section);
    var i = 0;

    if (!At(tokens, i, t => t.IsKeyword("section")) || !At(tokens, i + 1, t => t.IsIdentifier) || !At(tokens, i + 2, t => t.IsPunctuator(";")))
    {
      throw CantSplit(section, 0, "it doesn't start with a section declaration such as \"section Section1;\"");
    }

    i += 3;
    var members = new List<SectionMember>();
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    while (i < tokens.Count)
    {
      var memberStart = tokens[i].Position;
      string? description = null;
      if (tokens[i].IsPunctuator("["))
      {
        var close = ClosingBracket(tokens, i)
          ?? throw CantSplit(section, tokens[i].Position, "a query's attributes have no closing \"]\"");
        description = Description(tokens, i + 1, close);
        i = close + 1;
      }

      if (At(tokens, i, t => t.IsKeyword("shared")))
      {
        i++;
      }

      if (!At(tokens, i, t => t.IsIdentifier))
      {
        throw CantSplit(section, Position(tokens, i, section), "a query's name is missing");
      }

      var name = tokens[i].Text;
      if (!At(tokens, i + 1, t => t.IsPunctuator("=")))
      {
        throw CantSplit(section, tokens[i].Position, $"there's no \"=\" after the name of query {name}");
      }

      var start = tokens[i + 1].Position + 1;
      var end = i + 2;
      while (end < tokens.Count && !tokens[end].IsPunctuator(";"))
      {
        end++;
      }

      if (end == tokens.Count)
      {
        throw CantSplit(section, start, $"query {name} has no \";\" at its end");
      }

      var expression = section[start..tokens[end].Position].Trim();
      if (expression.Length == 0)
      {
        throw CantSplit(section, start, $"query {name} is empty");
      }

      if (!names.Add(name))
      {
        throw new ExtractionException($"The workbook has two queries called {name} (query names aren't case-sensitive).");
      }

      members.Add(new SectionMember(new WorkbookQuery(name, expression, description), memberStart, tokens[end].Position + 1));
      i = end + 1;
    }

    return members;
  }

  private static bool At(IReadOnlyList<MToken> tokens, int index, Func<MToken, bool> test) =>
    index < tokens.Count && test(tokens[index]);

  private static int Position(IReadOnlyList<MToken> tokens, int index, string section) =>
    index < tokens.Count ? tokens[index].Position : section.Length;

  /// <summary>The index of the "]" that closes the "[" at <paramref name="open"/>, or null if there's none.</summary>
  private static int? ClosingBracket(IReadOnlyList<MToken> tokens, int open)
  {
    var depth = 0;
    for (var i = open; i < tokens.Count; i++)
    {
      if (tokens[i].IsPunctuator("["))
      {
        depth++;
      }
      else if (tokens[i].IsPunctuator("]") && --depth == 0)
      {
        return i;
      }
    }

    return null;
  }

  /// <summary>The text of a top-level <c>Description = "…"</c> field in an attribute record, or null.</summary>
  private static string? Description(IReadOnlyList<MToken> tokens, int from, int to)
  {
    var depth = 0;
    for (var i = from; i < to; i++)
    {
      var token = tokens[i];
      if (token.IsPunctuator("[") || token.IsPunctuator("(") || token.IsPunctuator("{"))
      {
        depth++;
      }
      else if (token.IsPunctuator("]") || token.IsPunctuator(")") || token.IsPunctuator("}"))
      {
        depth--;
      }
      else if (depth == 0 && token.IsIdentifier && token.Text == "Description"
        && i + 2 < to && tokens[i + 1].IsPunctuator("=") && tokens[i + 2].Kind == MTokenKind.TextLiteral)
      {
        return tokens[i + 2].Text;
      }
    }

    return null;
  }

  private static ExtractionException CantSplit(string section, int position, string detail)
  {
    var line = 1 + section.AsSpan(0, Math.Min(position, section.Length)).Count('\n');
    return new ExtractionException($"The workbook's queries couldn't be separated: {detail} (line {line} of Section1.m).");
  }
}
