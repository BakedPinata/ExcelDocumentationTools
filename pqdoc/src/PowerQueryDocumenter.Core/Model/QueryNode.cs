namespace PowerQueryDocumenter.Core.Model;

/// <summary>A query in the dependency graph.</summary>
/// <param name="Name">The query name (the .m file name without its extension).</param>
/// <param name="Role">Root, leaf, isolated or intermediate.</param>
/// <param name="IsInCycle">True when the query is part of a circular dependency.</param>
/// <param name="UpstreamCount">Number of queries this query references directly.</param>
/// <param name="DownstreamCount">Number of queries that reference this query directly.</param>
public sealed record QueryNode(string Name, QueryRole Role, bool IsInCycle, int UpstreamCount, int DownstreamCount);
