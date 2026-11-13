using System.Text;
using CodeDocumenter.Shared.Files;

namespace CodeDocumenter.Shared.Importing;

/// <summary>What's written for a bundle, for <c>--on-exists</c>: its README stands for the folder.</summary>
/// <param name="Path">The bundle's README.</param>
public sealed record PlannedBundle(string Path) : IPlannedOutput
{
  /// <inheritdoc />
  public string Name => "bundle";
}

/// <summary>
/// A folder of chosen source files with a README saying how to import them by hand, which needs nothing installed or
/// switched on. Writing one deletes the tool's files that aren't in it, so a folder is only written if it's empty or an
/// earlier bundle, which its README says. Pointing it at a folder of someone's own modules can't cost them anything.
/// </summary>
public static class BundleFolder
{
  /// <summary>The README's name.</summary>
  public const string ReadmeName = "README.md";

  /// <summary>The first line of a bundle's README, which marks the folder as a bundle.</summary>
  public static string Marker(string tool) => $"<!-- A bundle written by {tool}. Writing another here replaces it. -->";

  /// <summary>Whether the folder is free to be written: missing, empty, or an earlier bundle from this tool.</summary>
  public static bool CanHold(string folder, string tool)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(folder);
    if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
    {
      return true;
    }

    var readme = Path.Combine(folder, ReadmeName);
    return File.Exists(readme) && File.ReadLines(readme).FirstOrDefault() == Marker(tool);
  }

  /// <summary>Whether writing the bundle would change the folder; one that wouldn't has nothing to overwrite.</summary>
  public static bool WouldChange(string folder, IReadOnlyDictionary<string, byte[]> files, string readme)
  {
    ArgumentNullException.ThrowIfNull(files);
    return files.Append(new KeyValuePair<string, byte[]>(ReadmeName, Bytes(readme)))
      .Any(f => !File.Exists(Path.Combine(folder, f.Key)) || !File.ReadAllBytes(Path.Combine(folder, f.Key)).AsSpan().SequenceEqual(f.Value));
  }

  /// <summary>Makes the folder hold the bundle: the files, and the README, whose first line is the tool's marker.</summary>
  /// <param name="folder">The folder, which <see cref="CanHold"/> has said is free.</param>
  /// <param name="files">Each file's name and bytes.</param>
  /// <param name="readme">The README's text, starting with <see cref="Marker"/>.</param>
  /// <param name="ownedExtensions">The extensions of the tool's source files.</param>
  /// <param name="gitAttributes">The text of the <c>.gitattributes</c> to create in a folder that has none.</param>
  public static SourceFolderResult Write(
    string folder,
    IReadOnlyDictionary<string, byte[]> files,
    string readme,
    IReadOnlyCollection<string> ownedExtensions,
    string gitAttributes)
  {
    ArgumentNullException.ThrowIfNull(readme);
    var result = SourceFolderWriter.Write(folder, files, ownedExtensions, gitAttributes);
    AtomicFileWriter.Write(Path.Combine(folder, ReadmeName), Bytes(readme));
    return result;
  }

  private static byte[] Bytes(string readme) => new UTF8Encoding(false).GetBytes(readme);
}
