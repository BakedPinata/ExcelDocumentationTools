using System.Text;

namespace CodeDocumenter.Shared.Files;

/// <summary>What writing a source folder did, so the tool can report it.</summary>
/// <param name="Written">Files written because they were new or had changed.</param>
/// <param name="Unchanged">Files left alone because they already held the right bytes.</param>
/// <param name="Deleted">The tool's files deleted because what they held is no longer in the workbook.</param>
/// <param name="CreatedGitAttributes">Whether a <c>.gitattributes</c> was created.</param>
public sealed record SourceFolderResult(
  IReadOnlyList<string> Written,
  IReadOnlyList<string> Unchanged,
  IReadOnlyList<string> Deleted,
  bool CreatedGitAttributes);

/// <summary>
/// Keeps a folder of source files in step with a workbook: vbadoc's module files, pqdoc's <c>.m</c> files. The tool
/// works out every file's name and bytes first, so a problem stops it before anything on disk changes; this then makes
/// the folder match.
/// </summary>
/// <remarks>
/// There's no manifest: every top-level file with one of the tool's extensions is taken to be the tool's. One that
/// isn't in the new set is deleted. Other files, and subfolders, are never touched. Unchanged files aren't rewritten, so
/// they keep their timestamps. A <c>.gitattributes</c> is created if the folder has none, and never overwritten.
/// </remarks>
public static class SourceFolderWriter
{
  /// <summary>Makes <paramref name="folder"/> hold exactly <paramref name="files"/>, creating it if need be.</summary>
  /// <param name="folder">The folder.</param>
  /// <param name="files">Each file's name and bytes. No two names may differ only by case.</param>
  /// <param name="ownedExtensions">The extensions of the files the tool owns, such as ".bas".</param>
  /// <param name="gitAttributes">The text of the <c>.gitattributes</c> to create in a folder that has none.</param>
  /// <exception cref="IOException">A file can't be written or deleted.</exception>
  /// <exception cref="UnauthorizedAccessException">A file can't be written or deleted.</exception>
  public static SourceFolderResult Write(
    string folder,
    IReadOnlyDictionary<string, byte[]> files,
    IReadOnlyCollection<string> ownedExtensions,
    string gitAttributes)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(folder);
    ArgumentNullException.ThrowIfNull(files);
    ArgumentNullException.ThrowIfNull(ownedExtensions);
    ArgumentNullException.ThrowIfNull(gitAttributes);
    if (files.Keys.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } clash)
    {
      throw new ArgumentException($"Two files would share the name {clash.Key} (file names aren't case-sensitive).", nameof(files));
    }

    Directory.CreateDirectory(folder);

    // Old files go first. Names are compared exactly, so something renamed only by case gets its new spelling even on
    // Windows, where the file system ignores case.
    var deleted = new List<string>();
    foreach (var path in Directory.GetFiles(folder))
    {
      var fileName = Path.GetFileName(path);
      if (ownedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase) && !files.ContainsKey(fileName))
      {
        File.Delete(path);
        deleted.Add(fileName);
      }
    }

    // Once started, the files are written to the end: half a folder would be worse than waiting a moment.
    var written = new List<string>();
    var unchanged = new List<string>();
    foreach (var (fileName, content) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
    {
      var path = Path.Combine(folder, fileName);
      if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
      {
        unchanged.Add(fileName);
        continue;
      }

      AtomicFileWriter.Write(path, content);
      written.Add(fileName);
    }

    var gitAttributesPath = Path.Combine(folder, ".gitattributes");
    var createdGitAttributes = !File.Exists(gitAttributesPath);
    if (createdGitAttributes)
    {
      File.WriteAllText(gitAttributesPath, gitAttributes, new UTF8Encoding(false));
    }

    deleted.Sort(StringComparer.OrdinalIgnoreCase);
    return new SourceFolderResult(written, unchanged, deleted, createdGitAttributes);
  }
}
