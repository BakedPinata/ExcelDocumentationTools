namespace VbaDocumenter.Core.Analysis;

/// <summary>
/// How widely each module and member is used: by how many other modules (step D4 of the feedback plan). The page marks
/// those used by at least a share of the other modules, with the share a setting there, so the counts are worked out
/// here once.
/// </summary>
public static class Usage
{
  /// <summary>
  /// For each module and member that another module uses, through an edge of any kind, how many other modules use it.
  /// A module counts every module that uses it or any of its members; a member, every module with a member (or the
  /// module itself) that uses it. Its own module never counts, and nor does anything nothing else uses.
  /// </summary>
  public static IReadOnlyDictionary<string, int> UsedByOtherModules(DependencyModel model)
  {
    var moduleOf = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var node in model.Members)
    {
      moduleOf.TryAdd(node.Id, node.Module.Name);
    }

    var users = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    void Add(string target, string fromModule, string toModule)
    {
      if (fromModule == toModule)
      {
        return;
      }

      if (!users.TryGetValue(target, out var set))
      {
        users[target] = set = new HashSet<string>(StringComparer.Ordinal);
      }

      set.Add(fromModule);
    }

    foreach (var e in model.ModuleEdges)
    {
      Add(e.To, e.From, e.To);
    }

    // At member level, an edge to a module's own node is a reference to the module, which the module edges count.
    foreach (var e in model.MemberEdges)
    {
      if (moduleOf.TryGetValue(e.From, out var from) && moduleOf.TryGetValue(e.To, out var to) && e.To != to)
      {
        Add(e.To, from, to);
      }
    }

    return users.ToDictionary(p => p.Key, p => p.Value.Count, StringComparer.Ordinal);
  }
}
