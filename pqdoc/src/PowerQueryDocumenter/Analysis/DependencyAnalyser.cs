using System.Text;
using PowerQueryDocumenter.Model;
using Serilog;

namespace PowerQueryDocumenter.Analysis;

/// <summary>The text of one query.</summary>
/// <param name="Name">The query name (the .m file name without its extension).</param>
/// <param name="Content">The M code.</param>
public sealed record QuerySource(string Name, string Content);

/// <summary>The outcome of analysing a folder.</summary>
/// <param name="Graph">The dependency graph.</param>
/// <param name="QueryFileCount">Number of .m files found, including unreadable and duplicate ones.</param>
/// <param name="UnreadableFiles">Files that could not be read. Their queries are in the graph, but without their own references.</param>
/// <param name="DuplicateFiles">Files ignored because another file has the same query name (names are case-insensitive).</param>
public sealed record AnalysisResult(
  DependencyGraph Graph,
  int QueryFileCount,
  IReadOnlyList<string> UnreadableFiles,
  IReadOnlyList<string> DuplicateFiles);

/// <summary>
/// Reads Power Query .m files and builds the <see cref="DependencyGraph"/> between them.
/// Analysis is separate from exporting: the result is an immutable graph (FR-ANA-7).
/// </summary>
public sealed class DependencyAnalyser
{
  /// <summary>The file extension of exported Power Query queries.</summary>
  public const string QueryFileExtension = ".m";

  private readonly ILogger logger;
  private readonly Func<string, CancellationToken, Task<string>> readFile;

  /// <param name="logger">Destination for progress and diagnostics.</param>
  /// <param name="readFile">Reads a file's text. Defaults to <see cref="ReadQueryFileAsync"/>; replaceable for testing.</param>
  public DependencyAnalyser(ILogger logger, Func<string, CancellationToken, Task<string>>? readFile = null)
  {
    ArgumentNullException.ThrowIfNull(logger);
    this.logger = logger;
    this.readFile = readFile ?? ReadQueryFileAsync;
  }

  /// <summary>
  /// Reads a query file as UTF-8. A byte order mark, if present, takes precedence, so UTF-16 and
  /// UTF-32 files with a BOM are also read correctly (FR-ANA-8).
  /// </summary>
  public static Task<string> ReadQueryFileAsync(string path, CancellationToken cancellationToken) =>
    File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

  /// <summary>
  /// Analyses every .m file directly inside <paramref name="folderPath"/> (subfolders are not scanned).
  /// A file that cannot be read is logged and skipped (FR-ANA-8). If the folder has no .m files the
  /// result has a <see cref="AnalysisResult.QueryFileCount"/> of zero.
  /// </summary>
  /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">The folder cannot be listed.</exception>
  /// <exception cref="IOException">The folder cannot be listed.</exception>
  public async Task<AnalysisResult> AnalyseFolderAsync(string folderPath, CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

    var enumeration = new EnumerationOptions
    {
      RecurseSubdirectories = false,
      MatchCasing = MatchCasing.CaseInsensitive,
      MatchType = MatchType.Simple,
      IgnoreInaccessible = false,
    };

    var files = Directory
      .EnumerateFiles(folderPath, "*" + QueryFileExtension, enumeration)
      .Where(f => string.Equals(Path.GetExtension(f), QueryFileExtension, StringComparison.OrdinalIgnoreCase))
      .Order(StringComparer.OrdinalIgnoreCase)
      .ToArray();

    logger.Debug("Found {Count} query file(s) in {Folder}", files.Length, folderPath);

    var pathsByName = new Dictionary<string, string>(DependencyGraph.NameComparer);
    var duplicates = new List<string>();
    foreach (var file in files)
    {
      var name = Path.GetFileNameWithoutExtension(file);
      if (!pathsByName.TryAdd(name, file))
      {
        duplicates.Add(file);
        logger.Warning(
          "Ignoring {File}: query {Query} is already defined by {ExistingFile} (query names are not case-sensitive)",
          file,
          name,
          pathsByName[name]);
      }
    }

    var sources = new List<QuerySource>();
    var unreadable = new List<string>();
    foreach (var (name, path) in pathsByName)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        sources.Add(new QuerySource(name, await readFile(path, cancellationToken).ConfigureAwait(false)));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
      {
        unreadable.Add(path);
        logger.Error(
          "Could not read {File}: {Reason}. Query {Query} is still included, but its own references are missing",
          path,
          ex.Message,
          name);
        logger.Debug(ex, "Read failure detail for {File}", path);
      }
    }

    var graph = Analyse(pathsByName.Keys, sources);
    return new AnalysisResult(graph, files.Length, unreadable, duplicates);
  }

  /// <summary>Builds a graph from in-memory query sources.</summary>
  public DependencyGraph Analyse(IEnumerable<QuerySource> sources)
  {
    ArgumentNullException.ThrowIfNull(sources);
    var list = sources.ToList();
    return Analyse(list.Select(s => s.Name), list);
  }

  /// <summary>
  /// Builds a graph. <paramref name="queryNames"/> is the full set of known queries; a query can
  /// be known (and referenced) even when its own source is unavailable.
  /// </summary>
  public DependencyGraph Analyse(IEnumerable<string> queryNames, IEnumerable<QuerySource> sources)
  {
    ArgumentNullException.ThrowIfNull(queryNames);
    ArgumentNullException.ThrowIfNull(sources);

    var sourceList = sources.ToList();
    var knownQueries = new Dictionary<string, string>(DependencyGraph.NameComparer);
    foreach (var name in queryNames.Concat(sourceList.Select(s => s.Name)))
    {
      knownQueries.TryAdd(name, name);
    }

    var edges = new List<DependencyEdge>();
    foreach (var source in sourceList)
    {
      var current = knownQueries[source.Name];
      var references = FindReferencedQueries(source.Content, current, knownQueries);
      if (references.Count > 0)
      {
        logger.Debug("{Query} references {References}", current, references);
      }

      edges.AddRange(references.Select(upstream => new DependencyEdge(upstream, current)));
    }

    return DependencyGraph.Create(knownQueries.Values, edges);
  }

  /// <summary>
  /// Returns the known queries that <paramref name="content"/> refers to.
  /// </summary>
  /// <remarks>
  /// Only identifiers that match a known query name count as references, so built-in functions
  /// and keywords can never produce a false dependency, and no built-in name filter is needed.
  /// That is what lets a query named <c>Source</c>, <c>Table</c> or <c>Value</c> be detected
  /// (FR-ANA-2). Dotted names such as <c>Table.SelectRows</c> are single identifiers, so they do
  /// not match a query named <c>Table</c>.
  /// </remarks>
  private static SortedSet<string> FindReferencedQueries(
    string content,
    string currentQueryName,
    Dictionary<string, string> knownQueries)
  {
    var referenced = new SortedSet<string>(DependencyGraph.NameComparer);

    foreach (var identifier in ReferenceExtractor.FindFreeIdentifiers(content))
    {
      if (!knownQueries.TryGetValue(identifier, out var query))
      {
        continue;
      }

      // Lower-case M type names ("as table", "type [A = text]") are types, so they only refer to
      // a query whose name is spelled exactly the same way, not to "Table" or "Text".
      if (MLanguage.IsTypeWord(identifier) && !string.Equals(identifier, query, StringComparison.Ordinal))
      {
        continue;
      }

      // Self-references never create an edge (FR-ANA-4).
      if (DependencyGraph.NameComparer.Equals(query, currentQueryName))
      {
        continue;
      }

      referenced.Add(query);
    }

    return referenced;
  }
}
