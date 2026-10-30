namespace PowerQueryDocumenter.Core.Model;

/// <summary>
/// A query's position in the dependency graph (FR-ANA-6).
/// Edges run from the referenced (upstream) query to the referencing (downstream) query.
/// </summary>
public enum QueryRole
{
  /// <summary>Has upstream queries and dependents.</summary>
  Intermediate,

  /// <summary>Has no upstream queries but at least one dependent (a starting point, such as a data source).</summary>
  Root,

  /// <summary>Has upstream queries but no dependents (an end point, such as a report table).</summary>
  Leaf,

  /// <summary>Has neither upstream queries nor dependents.</summary>
  Isolated,
}
