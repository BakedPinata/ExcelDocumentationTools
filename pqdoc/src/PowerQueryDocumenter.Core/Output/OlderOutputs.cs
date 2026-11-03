using System.Text;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Core.Output;

/// <summary>
/// Finds outputs pqdoc wrote earlier under another name: those of a renamed workbook, or the
/// <c>powerquery-dependencies</c> files from before 1.3.0. They're found through their page, which
/// carries pqdoc's generator tag, so files written by hand or by another tool are never included.
/// Every output that shares an older page's name goes with it.
/// </summary>
public static class OlderOutputs
{
  // The page template's generator tag starts with the tool's name.
  private const string GeneratorTag = "<meta name=\"generator\" content=\"pqdoc ";

  /// <summary>The older outputs in <paramref name="folder"/>, as full paths.</summary>
  /// <param name="folder">The output folder.</param>
  /// <param name="currentName">The base name of this run's outputs, whose files are never included.</param>
  public static IReadOnlyList<string> Find(string folder, string currentName)
  {
    ArgumentNullException.ThrowIfNull(folder);
    ArgumentNullException.ThrowIfNull(currentName);

    var found = new List<string>();
    if (!Directory.Exists(folder))
    {
      return found;
    }

    foreach (var page in Directory.GetFiles(folder, "*.html").Order(StringComparer.OrdinalIgnoreCase))
    {
      var name = Path.GetFileNameWithoutExtension(page);
      if (string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase) || !WrittenByPqdoc(page))
      {
        continue;
      }

      found.AddRange(Enum.GetValues<OutputFormat>()
        .Select(format => Path.Combine(folder, name + format.FileExtension()))
        .Where(File.Exists));
    }

    return found;
  }

  /// <summary>Whether a page carries pqdoc's generator tag, which is within its first lines.</summary>
  private static bool WrittenByPqdoc(string path)
  {
    try
    {
      using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      var head = new byte[2048];
      var length = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
      return Encoding.UTF8.GetString(head, 0, length).Contains(GeneratorTag, StringComparison.Ordinal);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return false; // a page that can't be read is left alone
    }
  }
}
