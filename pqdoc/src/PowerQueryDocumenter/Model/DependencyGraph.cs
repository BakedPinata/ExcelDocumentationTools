using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using PowerQueryDocumenter.Analysis;

namespace PowerQueryDocumenter.Model;

/// <summary>
/// Immutable dependency graph produced by the analyser and consumed by every exporter (FR-ANA-7).
/// Query names are compared case-insensitively throughout (FR-ANA-3), and nodes and edges are
/// always returned in a stable, case-insensitive name order.
/// </summary>
public sealed class DependencyGraph
{
  /// <summary>The comparer used for every query-name comparison and sort.</summary>
  public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

  private readonly FrozenDictionary<string, QueryNode> nodesByName;
  private readonly FrozenDictionary<string, string[]> upstreamByName;
  private readonly FrozenDictionary<string, string[]> downstreamByName;

  private DependencyGraph(
    QueryNode[] nodes,
    DependencyEdge[] edges,
    DependencyCycle[] cycles,
    Dictionary<string, string[]> upstreamByName,
    Dictionary<string, string[]> downstreamByName)
  {
    Nodes = Array.AsReadOnly(nodes);
    Edges = Array.AsReadOnly(edges);
    Cycles = Array.AsReadOnly(cycles);
    nodesByName = nodes.ToFrozenDictionary(n => n.Name, NameComparer);
    this.upstreamByName = upstreamByName.ToFrozenDictionary(NameComparer);
    this.downstreamByName = downstreamByName.ToFrozenDictionary(NameComparer);
  }

  /// <summary>An empty graph.</summary>
  public static DependencyGraph Empty { get; } = Create([], []);

  /// <summary>Every query, sorted by name.</summary>
  public IReadOnlyList<QueryNode> Nodes { get; }

  /// <summary>Every dependency edge (upstream to downstream), sorted by upstream name then downstream name.</summary>
  public IReadOnlyList<DependencyEdge> Edges { get; }

  /// <summary>Circular dependency groups, sorted by their first member's name.</summary>
  public IReadOnlyList<DependencyCycle> Cycles { get; }

  /// <summary>
  /// Builds a graph. Names that differ only by case are treated as the same query (the first
  /// spelling wins). Self-references are ignored (FR-ANA-4) and duplicate edges are collapsed.
  /// </summary>
  /// <exception cref="ArgumentException">A name is blank, or an edge refers to an unknown query.</exception>
  public static DependencyGraph Create(IEnumerable<string> queryNames, IEnumerable<DependencyEdge> edges)
  {
    ArgumentNullException.ThrowIfNull(queryNames);
    ArgumentNullException.ThrowIfNull(edges);

    var canonicalNames = new Dictionary<string, string>(NameComparer);
    foreach (var name in queryNames)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        throw new ArgumentException("Query names must not be empty.", nameof(queryNames));
      }

      canonicalNames.TryAdd(name, name);
    }

    var sortedNames = canonicalNames.Values.Order(NameComparer).ToArray();
    var upstream = sortedNames.ToDictionary(n => n, _ => new SortedSet<string>(NameComparer), NameComparer);
    var downstream = sortedNames.ToDictionary(n => n, _ => new SortedSet<string>(NameComparer), NameComparer);

    foreach (var edge in edges)
    {
      var from = Canonicalise(edge.Upstream);
      var to = Canonicalise(edge.Downstream);
      if (NameComparer.Equals(from, to))
      {
        continue;
      }

      downstream[from].Add(to);
      upstream[to].Add(from);
    }

    var edgeArray = sortedNames
      .SelectMany(from => downstream[from].Select(to => new DependencyEdge(from, to)))
      .ToArray();

    var cycles = CycleDetector.FindCycles(sortedNames, name => downstream[name]);
    var inCycle = cycles.SelectMany(c => c.Members).ToHashSet(NameComparer);

    var nodes = sortedNames
      .Select(name => new QueryNode(
        name,
        Classify(upstream[name].Count, downstream[name].Count),
        inCycle.Contains(name),
        upstream[name].Count,
        downstream[name].Count))
      .ToArray();

    return new DependencyGraph(
      nodes,
      edgeArray,
      [.. cycles],
      upstream.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), NameComparer),
      downstream.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), NameComparer));

    string Canonicalise(string name) =>
      canonicalNames.TryGetValue(name, out var canonical)
        ? canonical
        : throw new ArgumentException($"Edge refers to unknown query '{name}'.", nameof(edges));
  }

  /// <summary>Classifies a query from its direct upstream and downstream counts (FR-ANA-6).</summary>
  public static QueryRole Classify(int upstreamCount, int downstreamCount) => (upstreamCount, downstreamCount) switch
  {
    (0, 0) => QueryRole.Isolated,
    (0, _) => QueryRole.Root,
    (_, 0) => QueryRole.Leaf,
    _ => QueryRole.Intermediate,
  };

  /// <summary>Number of queries with the given role.</summary>
  public int CountByRole(QueryRole role) => Nodes.Count(n => n.Role == role);

  /// <summary>True when a query with this name (any case) exists.</summary>
  public bool Contains(string name) => nodesByName.ContainsKey(name);

  /// <summary>Looks up a query by name (any case).</summary>
  public bool TryGetNode(string name, [NotNullWhen(true)] out QueryNode? node) => nodesByName.TryGetValue(name, out node);

  /// <summary>Looks up a query by name (any case).</summary>
  /// <exception cref="KeyNotFoundException">No query has this name.</exception>
  public QueryNode GetNode(string name) =>
    nodesByName.TryGetValue(name, out var node) ? node : throw new KeyNotFoundException($"Unknown query '{name}'.");

  /// <summary>The queries that <paramref name="name"/> references directly, sorted by name.</summary>
  public IReadOnlyList<string> GetUpstream(string name) => Lookup(upstreamByName, name);

  /// <summary>The queries that reference <paramref name="name"/> directly, sorted by name.</summary>
  public IReadOnlyList<string> GetDownstream(string name) => Lookup(downstreamByName, name);

  private static IReadOnlyList<string> Lookup(FrozenDictionary<string, string[]> map, string name) =>
    map.TryGetValue(name, out var names)
      ? Array.AsReadOnly(names)
      : throw new KeyNotFoundException($"Unknown query '{name}'.");
}
