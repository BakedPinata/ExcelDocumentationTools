using System.Text;
using System.Text.RegularExpressions;

namespace VbaDocumenter.Core.Parsing;

/// <summary>
/// What a module or member says about itself (step L5 of the generalisation plan), for the page's side panel and its
/// printed document. For a member: its <c>VB_Description</c> attribute (which the VBA editor's Procedure Attributes
/// dialog and Rubberduck write), otherwise a <c>'@Description</c> annotation or the comments directly above it,
/// otherwise a comment at the end of its declaration's line. For a module: its <c>VB_Description</c> attribute,
/// otherwise a <c>'@ModuleDescription</c> annotation or the first comments at its top, above its first member.
/// </summary>
public static partial class Descriptions
{
  /// <summary>The most a description holds, so a page doesn't carry a licence or a change history written as comments.</summary>
  public const int MaxLength = 1000;

  const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

  // Rubberduck's annotations: '@Description("text") or '@Description "text", and '@ModuleDescription for a module.
  [GeneratedRegex(@"^@(?<module>Module)?Description\b\s*\(?\s*""(?<text>(?:[^""]|"""")*)""", Opts)]
  private static partial Regex DescriptionAnnotation();

  /// <summary>An attribute's value as written, such as <c>"Says ""hello"""</c>, as text.</summary>
  public static string? FromAttribute(string? value)
  {
    if (value == null) return null;
    var text = value.Trim();
    if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
    return Tidy(text.Replace("\"\"", "\""));
  }

  /// <summary>A member's description from the module's lines: an annotation or the comments above it, or the comment
  /// ending its declaration's line.</summary>
  public static string? ForMember(IReadOnlyList<string> sourceLines, Member member)
  {
    ArgumentNullException.ThrowIfNull(sourceLines);
    ArgumentNullException.ThrowIfNull(member);
    // Only the run of comment lines straight above the declaration: a member declared again under #If starts at the
    // #If, and what's between may be another member's.
    int first = Math.Min(member.DeclarationLine, sourceLines.Count + 1);
    while (first - 1 >= Math.Max(1, member.StartLine) && CommentOf(sourceLines[first - 2]) != null) first--;
    var above = FromComments(Lines(sourceLines, first, member.DeclarationLine - 1), forModule: false);
    if (above != null) return above;
    return member.DeclarationLine >= 1 && member.DeclarationLine <= sourceLines.Count
        ? Tidy(TrailingComment(sourceLines[member.DeclarationLine - 1]) is { } comment && !comment.TrimStart().StartsWith('@') ? comment : null)
        : null;
  }

  /// <summary>
  /// A module's description from its lines above its first member (all of them when it has none): a
  /// <c>'@ModuleDescription</c> annotation, otherwise the first run of comment lines that says anything.
  /// </summary>
  public static string? ForModule(IReadOnlyList<string> sourceLines, int? firstMemberStart)
  {
    ArgumentNullException.ThrowIfNull(sourceLines);
    var header = Lines(sourceLines, 1, (firstMemberStart ?? sourceLines.Count + 1) - 1);
    string? firstText = null;
    var run = new List<string>();
    foreach (var line in header.Append("")) // the empty line ends a run that reaches the end
    {
      if (CommentOf(line) != null) { run.Add(line); continue; }
      if (run.Count == 0) continue;
      if (Annotation(run, forModule: true) is { } annotated) return annotated;
      firstText ??= FromComments(run, forModule: true);
      run.Clear();
    }
    return firstText;
  }

  static IEnumerable<string> Lines(IReadOnlyList<string> sourceLines, int first, int last)
  {
    for (int n = Math.Max(1, first); n <= Math.Min(last, sourceLines.Count); n++) yield return sourceLines[n - 1];
  }

  /// <summary>A comment-only line's text after its <c>'</c> or <c>Rem</c>, or null if the line isn't one.</summary>
  static string? CommentOf(string line)
  {
    var text = line.TrimStart();
    if (text.StartsWith('\'')) return text[1..];
    if (text.Equals("Rem", StringComparison.OrdinalIgnoreCase)) return "";
    return text.StartsWith("Rem ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Rem\t", StringComparison.OrdinalIgnoreCase) ? text[4..] : null;
  }

  static string? Annotation(IEnumerable<string> lines, bool forModule)
  {
    foreach (var line in lines)
    {
      if (CommentOf(line) is not { } comment) continue;
      var match = DescriptionAnnotation().Match(comment.Trim());
      if (match.Success && match.Groups["module"].Success == forModule) return Tidy(match.Groups["text"].Value.Replace("\"\"", "\""));
    }
    return null;
  }

  /// <summary>
  /// The text of a run of comment lines: an annotation if there is one, otherwise the lines as written, without other
  /// annotations (<c>'@Folder</c>) and with lines that only decorate (<c>'-----</c>) as breaks.
  /// </summary>
  static string? FromComments(IEnumerable<string> lines, bool forModule)
  {
    var all = lines.ToList();
    if (Annotation(all, forModule) is { } annotated) return annotated;

    var text = new StringBuilder();
    foreach (var line in all)
    {
      if (CommentOf(line) is not { } comment) continue;
      comment = comment.Trim();
      if (comment.StartsWith('@')) continue;
      if (!comment.Any(char.IsLetterOrDigit)) comment = "";
      text.Append(comment).Append('\n');
    }
    return Tidy(text.ToString());
  }

  /// <summary>The comment at the end of a line of code, outside any string literal, or null.</summary>
  static string? TrailingComment(string line)
  {
    bool inString = false;
    for (int i = 0; i < line.Length; i++)
    {
      if (line[i] == '"') inString = !inString; // a doubled quote inside a string toggles twice
      else if (line[i] == '\'' && !inString) return line[..i].Trim().Length == 0 ? null : line[(i + 1)..];
    }
    return null;
  }

  /// <summary>Trimmed, with runs of empty lines as one, cut to <see cref="MaxLength"/>; null when nothing's left.</summary>
  static string? Tidy(string? text)
  {
    if (text == null) return null;
    var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).ToList();
    var kept = new List<string>();
    foreach (var line in lines)
    {
      if (line.Length == 0 && (kept.Count == 0 || kept[^1].Length == 0)) continue;
      kept.Add(line);
    }
    var tidy = string.Join("\n", kept).Trim();
    if (tidy.Length == 0) return null;
    return tidy.Length > MaxLength ? tidy[..(MaxLength - 1)].TrimEnd() + "…" : tidy;
  }
}
