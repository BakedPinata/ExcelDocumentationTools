using ExcelDocumenter.Desktop.Running;

namespace ExcelDocumenter.Desktop.Documenting;

/// <summary>
/// Which tool reads what, to offer only the tools that apply. The tools tell a workbook by what's in it, not by its
/// name, so this follows what each tool's help says it reads; a test keeps the two the same.
/// </summary>
internal static class Readers
{
  /// <summary>vbadoc's workbooks, as its <c>&lt;workbook&gt;</c> help lists them.</summary>
  public static IReadOnlyList<string> VbadocExtensions { get; } = [".xlsm", ".xlsb", ".xlam", ".xls", ".xla"];

  /// <summary>pqdoc's workbooks, as its <c>&lt;input&gt;</c> help lists them. It reads a folder of .m files too.</summary>
  public static IReadOnlyList<string> PqdocExtensions { get; } = [".xlsx", ".xlsm", ".xlsb"];

  /// <summary>
  /// Whether a tool reads an input, as typed and tidied. Nothing chosen yet counts as yes, so every tool is offered. For
  /// pqdoc, a name with no extension that isn't a file is taken as a folder.
  /// </summary>
  public static bool Reads(Tool tool, string input)
  {
    ArgumentNullException.ThrowIfNull(input);
    if (input.Length == 0)
    {
      return true;
    }

    var extension = Path.GetExtension(input);
    return tool switch
    {
      Tool.Vbadoc => !Directory.Exists(input) && VbadocExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase),
      Tool.Pqdoc => Directory.Exists(input)
        || PqdocExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
        || (extension.Length == 0 && !File.Exists(input)),
      _ => false,
    };
  }

  /// <summary>Why a tool isn't offered for an input, for the window.</summary>
  public static string WhyNot(Tool tool, string input)
  {
    var extension = Path.GetExtension(input);
    return tool switch
    {
      Tool.Vbadoc when Directory.Exists(input) => "vbadoc reads a workbook, not a folder",
      _ when extension.Length > 0 => $"{tool.Name()} doesn't read {extension.ToLowerInvariant()} files",
      _ => $"{tool.Name()} doesn't read this",
    };
  }
}
