using System.Text;

namespace ExcelDocumenter.Desktop.Running;

/// <summary>Writes a command so it can be pasted at a Command Prompt or PowerShell prompt and run as it is.</summary>
internal static class CommandLineText
{
  /// <summary>The tool's name and its arguments, each quoted if it needs to be.</summary>
  public static string Format(string tool, IEnumerable<string> arguments) =>
    string.Join(' ', arguments.Select(Quote).Prepend(tool));

  /// <summary>
  /// An argument as written on a command line: as it is when it's plain, otherwise in double quotes, following the
  /// rules a .NET program reads its arguments by. Backslashes only need doubling before a quote, as at the end of a
  /// folder written <c>C:\docs\</c>.
  /// </summary>
  public static string Quote(string argument)
  {
    ArgumentNullException.ThrowIfNull(argument);
    if (argument.Length > 0 && argument.All(IsPlain))
    {
      return argument;
    }

    var quoted = new StringBuilder("\"");
    var backslashes = 0;
    foreach (var character in argument)
    {
      if (character == '\\')
      {
        backslashes++;
        continue;
      }

      quoted.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes).Append(character);
      backslashes = 0;
    }

    return quoted.Append('\\', backslashes * 2).Append('"').ToString();
  }

  // Characters no shell gives a meaning to. A comma is quoted, since PowerShell reads a, b as a list.
  private static bool IsPlain(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '\\' or '/';
}
