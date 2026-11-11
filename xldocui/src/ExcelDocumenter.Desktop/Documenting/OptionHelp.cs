using System.Text.RegularExpressions;

namespace ExcelDocumenter.Desktop.Documenting;

/// <summary>Reads what a tool's help says about an option, so the window says the same as <c>--help</c>.</summary>
internal static partial class OptionHelp
{
  /// <summary>The default an option's description gives in its "[default: …]", or null if it gives none.</summary>
  public static string? Default(string? description) =>
    description is not null && DefaultPattern().Match(description) is { Success: true } match ? match.Groups["text"].Value.Trim() : null;

  /// <summary>The description without its "[default: …]", which the window shows on its own.</summary>
  public static string WithoutDefault(string? description) =>
    description is null ? "" : DefaultPattern().Replace(description, "").Trim();

  [GeneratedRegex(@"\[default: (?<text>[^\]]*)\]")]
  private static partial Regex DefaultPattern();
}
