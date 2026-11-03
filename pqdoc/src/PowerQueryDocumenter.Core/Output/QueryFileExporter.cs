using System.Text;
using PowerQueryDocumenter.Core.Extraction;

namespace PowerQueryDocumenter.Core.Output;

/// <summary>What happened to the query files in one run.</summary>
/// <param name="Written">Files written because they were new or had changed.</param>
/// <param name="Unchanged">Files left alone because they already held the query's text.</param>
/// <param name="Deleted">Files deleted because their query is no longer in the workbook.</param>
/// <param name="CreatedGitAttributes">Whether a <c>.gitattributes</c> was created.</param>
public sealed record QueryFileResult(
  IReadOnlyList<string> Written,
  IReadOnlyList<string> Unchanged,
  IReadOnlyList<string> Deleted,
  bool CreatedGitAttributes);

/// <summary>
/// Writes a workbook's queries as <c>.m</c> files, one per query, and keeps the folder in step with the workbook as
/// vbadoc keeps its module files (FR-QF-1 to FR-QF-4). Each file holds the expression as Excel stores it, which is what
/// the Advanced Editor shows and what a manual export contains, so pqdoc reads both the same way.
/// </summary>
/// <remarks>
/// There's no manifest: every top-level <c>.m</c> file in the folder is taken to be pqdoc's. One whose query is no
/// longer in the workbook is deleted, so don't keep hand-written <c>.m</c> files there. Other files are never touched.
/// Unchanged files aren't rewritten, so they keep their timestamps. <c>--on-exists</c> doesn't apply: like the
/// workbook, these files are pqdoc's to keep in step.
/// </remarks>
public static class QueryFileExporter
{
  /// <summary>The <c>.gitattributes</c> created in a folder that has none.</summary>
  public const string GitAttributesContent =
    "# Created by pqdoc. Keep exported Power Query exactly as written, with no conversion by git: CRLF line endings,\n" +
    "# except inside text literals, which keep the line breaks Excel stored. (\"-text\" turns conversion off; \"diff\"\n" +
    "# keeps line-by-line diffs.)\n" +
    "*.m -text diff\n" +
    "*.html -text diff\n";

  /// <summary>Writes <paramref name="queries"/> into <paramref name="folder"/>, creating it if need be.</summary>
  /// <exception cref="ExtractionException">Two queries would have the same file name.</exception>
  /// <exception cref="IOException">A file can't be written or deleted.</exception>
  /// <exception cref="UnauthorizedAccessException">A file can't be written or deleted.</exception>
  public static async Task<QueryFileResult> ExportAsync(IReadOnlyList<WorkbookQuery> queries, string folder)
  {
    ArgumentNullException.ThrowIfNull(queries);
    ArgumentException.ThrowIfNullOrWhiteSpace(folder);

    // Work out every file first, so a problem stops the run before anything on disk changes.
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    var namesIgnoringCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var query in queries)
    {
      var fileName = QueryFileNames.FileName(query.Name);
      if (!namesIgnoringCase.Add(fileName))
      {
        throw new ExtractionException($"Two queries would both be written to {fileName} (file names aren't case-sensitive).");
      }

      files[fileName] = QueryFileText.Bytes(query.Expression);
    }

    Directory.CreateDirectory(folder);

    // Old files go first. Names are compared exactly, so a query renamed only by case gets its new spelling even on
    // Windows, where the file system ignores case.
    var deleted = new List<string>();
    foreach (var path in Directory.GetFiles(folder))
    {
      var fileName = Path.GetFileName(path);
      if (string.Equals(Path.GetExtension(fileName), QueryFileNames.Extension, StringComparison.OrdinalIgnoreCase)
        && !files.ContainsKey(fileName))
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
      if (File.Exists(path) && (await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false)).AsSpan().SequenceEqual(content))
      {
        unchanged.Add(fileName);
        continue;
      }

      await AtomicFileWriter.WriteAsync(path, (stream, token) => stream.WriteAsync(content, token).AsTask(), CancellationToken.None).ConfigureAwait(false);
      written.Add(fileName);
    }

    var gitAttributes = Path.Combine(folder, ".gitattributes");
    var createdGitAttributes = !File.Exists(gitAttributes);
    if (createdGitAttributes)
    {
      await File.WriteAllTextAsync(gitAttributes, GitAttributesContent, new UTF8Encoding(false), CancellationToken.None).ConfigureAwait(false);
    }

    deleted.Sort(StringComparer.OrdinalIgnoreCase);
    return new QueryFileResult(written, unchanged, deleted, createdGitAttributes);
  }
}
