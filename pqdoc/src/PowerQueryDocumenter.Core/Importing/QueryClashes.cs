using CodeDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Model;
using Serilog;

namespace PowerQueryDocumenter.Core.Importing;

/// <summary>
/// Finds what's in the way of a choice of queries, and says what's at stake with each: what else in the target uses what
/// would be replaced, and what's coming that would use the target's version instead.
/// </summary>
public static class QueryClashes
{
  /// <summary>Compares the plan's queries with the target's.</summary>
  /// <param name="incoming">The source's queries, by name.</param>
  /// <param name="sourceName">The source's name.</param>
  /// <param name="plan">What's coming.</param>
  /// <param name="import">What uses what in the source.</param>
  /// <param name="target">The target.</param>
  /// <param name="logger">Where the analysis of the target's queries logs.</param>
  public static TargetComparison Compare(
    IReadOnlyDictionary<string, QueryToWrite> incoming,
    string sourceName,
    ImportPlan plan,
    QueryImport import,
    TargetWorkbook target,
    ILogger logger)
  {
    ArgumentNullException.ThrowIfNull(incoming);
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(import);
    ArgumentNullException.ThrowIfNull(target);

    var theirs = target.Queries.ToDictionary(q => q.Name, DependencyGraph.NameComparer);
    var graph = new Lazy<DependencyGraph>(() => new DependencyAnalyser(logger).Analyse(target.Queries.Select(q => new QuerySource(q.Name, q.Expression))));

    var added = new List<string>();
    var identical = new List<string>();
    var clashes = new List<ImportClash>();
    foreach (var id in plan.Items)
    {
      if (!theirs.TryGetValue(id, out var existing))
      {
        added.Add(id);
        continue;
      }

      var query = incoming[id];
      var sameText = QueryWriter.SameText(existing.Expression, query.Expression);
      if (sameText && existing.Name.Equals(id, StringComparison.Ordinal))
      {
        identical.Add(id);
        continue;
      }

      var comingUsers = plan.Items
        .Where(item => !DependencyGraph.NameComparer.Equals(item, id) && import.Uses(item).Contains(id, DependencyGraph.NameComparer))
        .ToList();
      clashes.Add(new ImportClash(
        id,
        Problem(id, sourceName, query, existing, sameText),
        ImportClash.Overwriting([.. graph.Value.GetDownstream(existing.Name)]),
        ImportClash.Skipping(comingUsers)));
    }

    return new TargetComparison(added, identical, clashes);
  }

  private static string Problem(string id, string sourceName, QueryToWrite query, Extraction.WorkbookQuery existing, bool sameText)
  {
    // M's names are case-sensitive though Excel's are not, so a query can be there and still not be found.
    if (!existing.Name.Equals(id, StringComparison.Ordinal))
    {
      var what = sameText ? "the same query" : "a different query";
      return $"The target has {what} called {existing.Name}, and M's names are case-sensitive, so what uses {id} won't find it by that spelling.";
    }

    var isFunction = query.ResultType == "Function";
    if (existing.ResultType is { } theirs && (theirs == "Function") != isFunction)
    {
      return $"The target's {id} is {(isFunction ? "a query" : "a function")}, and the one in {sourceName} is {(isFunction ? "a function" : "a query")}.";
    }

    return $"The target already has {id}, and it differs from the one in {sourceName}.";
  }
}
