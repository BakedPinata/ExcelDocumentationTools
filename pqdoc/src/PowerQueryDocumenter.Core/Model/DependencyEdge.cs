namespace PowerQueryDocumenter.Core.Model;

/// <summary>
/// A directed dependency edge. It runs from the referenced (upstream) query to the
/// referencing (downstream) query, so data flows in the direction of the edge.
/// </summary>
/// <param name="Upstream">The query being referenced (the dependency).</param>
/// <param name="Downstream">The query doing the referencing (the dependent).</param>
public sealed record DependencyEdge(string Upstream, string Downstream);
