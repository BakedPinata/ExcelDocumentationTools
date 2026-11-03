using System.Globalization;
using System.Text;

namespace PowerQueryDocumenter.Core.Output;

/// <summary>
/// Turns query names into <c>.m</c> file names and back (FR-QF-2). A query name can hold characters Windows doesn't
/// allow in a file name, such as <c>/</c> in <c>Sales/Returns</c>, and can be a name Windows keeps for devices, such as
/// <c>CON</c>. Those characters, and <c>%</c> itself, become <c>%XX</c> (the character's code in hex), and a device name
/// has its first letter escaped, so <c>Sales/Returns</c> is <c>Sales%2FReturns.m</c> and <c>CON</c> is <c>%43ON.m</c>.
/// </summary>
/// <remarks>
/// Turning a file name back reverses exactly those escapes and nothing else, so the round trip always gives the
/// original name, and a file made by hand with some other <c>%</c> in its name (<c>Sales%20Data.m</c>) keeps its name.
/// vbadoc's <c>_</c> substitution doesn't suit queries: for VBA it's a safety net that never runs, but real query names
/// need it, and two names could end up the same.
/// </remarks>
public static class QueryFileNames
{
  /// <summary>The extension of a query file.</summary>
  public const string Extension = ".m";

  // Characters Windows doesn't allow in a file name, and % because it starts an escape.
  private const string EscapedCharacters = "<>:\"/\\|?*%";

  private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
  {
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
  };

  /// <summary>The file name for a query, with its extension.</summary>
  public static string FileName(string queryName)
  {
    ArgumentNullException.ThrowIfNull(queryName);
    var name = new StringBuilder(queryName.Length + 8);
    foreach (var c in queryName)
    {
      name.Append(MustEscape(c) ? Escape(c) : c.ToString());
    }

    if (DeviceNames.Contains(queryName))
    {
      name.Remove(0, 1).Insert(0, Escape(queryName[0]));
    }

    return name + Extension;
  }

  /// <summary>The query name for a file name given without its extension, reversing only the escapes above.</summary>
  public static string QueryName(string fileNameWithoutExtension)
  {
    ArgumentNullException.ThrowIfNull(fileNameWithoutExtension);
    var text = fileNameWithoutExtension;
    var name = new StringBuilder(text.Length);
    for (var i = 0; i < text.Length; i++)
    {
      if (text[i] == '%' && TryHex(text, i + 1, out var c))
      {
        var isEscape = MustEscape(c) || (i == 0 && DeviceNames.Contains(c + text[(i + 3)..]));
        if (isEscape)
        {
          name.Append(c);
          i += 2;
          continue;
        }
      }

      name.Append(text[i]);
    }

    return name.ToString();
  }

  private static bool MustEscape(char c) => c < ' ' || EscapedCharacters.Contains(c);

  private static string Escape(char c) => "%" + ((int)c).ToString("X2", CultureInfo.InvariantCulture);

  /// <summary>Reads two upper-case hex digits, as <see cref="Escape"/> writes them.</summary>
  private static bool TryHex(string text, int start, out char c)
  {
    c = '\0';
    if (start + 2 > text.Length || !IsHexDigit(text[start]) || !IsHexDigit(text[start + 1]))
    {
      return false;
    }

    c = (char)int.Parse(text.AsSpan(start, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    return true;
  }

  private static bool IsHexDigit(char c) => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F';
}
