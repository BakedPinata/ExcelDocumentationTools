namespace PowerQueryDocumenter.Core.Model;

/// <summary>
/// A circular dependency: a strongly connected group of queries that all (directly or indirectly)
/// reference each other.
/// </summary>
/// <param name="Members">Every query in the group, sorted by name.</param>
/// <param name="Path">
/// One concrete cycle through the group, starting and ending at the same query
/// (for example A, B, C, A), following edge direction (upstream to downstream).
/// </param>
public sealed record DependencyCycle(IReadOnlyList<string> Members, IReadOnlyList<string> Path)
{
  /// <summary>The cycle path formatted for display, for example "A -> B -> A".</summary>
  public string Describe() => string.Join(" -> ", Path);
}
