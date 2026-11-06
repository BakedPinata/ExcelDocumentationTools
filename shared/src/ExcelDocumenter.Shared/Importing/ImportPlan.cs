namespace ExcelDocumenter.Shared.Importing;

/// <summary>Something an item relies on that doesn't come with it, so the target may need it.</summary>
/// <param name="What">What it relies on, such as a sheet's code name or a data source.</param>
/// <param name="Why">What that is, in a phrase that follows the name, such as <c>a sheet's code name</c>.</param>
public sealed record ImportNeed(string What, string Why);

/// <summary>An <see cref="ImportNeed"/> of the chosen set.</summary>
/// <param name="Need">What it is.</param>
/// <param name="UsedBy">The items that rely on it.</param>
public sealed record ImportNeedOf(ImportNeed Need, IReadOnlyList<string> UsedBy);

/// <summary>An item that comes because something chosen uses it.</summary>
/// <param name="Id">The item.</param>
/// <param name="UsedBy">The items in the set that use it.</param>
public sealed record BroughtItem(string Id, IReadOnlyList<string> UsedBy);

/// <summary>An item that isn't in the set but may be wanted: nothing uses an interface's implementations, say.</summary>
/// <param name="Id">The item.</param>
/// <param name="For">The item in the set it goes with.</param>
public sealed record OfferedItem(string Id, string For);

/// <summary>
/// What a choice of items comes to: what was picked, what that brings along by following what uses what, what none of it
/// brings but relies on, and what may be wanted as well. Lists are in name order, ignoring case, so a plan reads the
/// same every time. The page works out the same thing from its data, so the two say the same before anything is written.
/// </summary>
public sealed record ImportPlan(
  IReadOnlyList<string> Picked,
  IReadOnlyList<BroughtItem> Brought,
  IReadOnlyList<ImportNeedOf> Needs,
  IReadOnlyList<OfferedItem> Offers)
{
  /// <summary>Everything that comes: what was picked, then what it brings.</summary>
  public IReadOnlyList<string> Items => [.. Picked, .. Brought.Select(b => b.Id)];

  /// <summary>Follows what the picks use, to the end of each chain; a cycle is simply followed once.</summary>
  /// <param name="picks">The items chosen.</param>
  /// <param name="uses">The items an item uses that can come along.</param>
  /// <param name="needs">What an item relies on that can't.</param>
  /// <param name="offers">What may be wanted with an item, or null for nothing.</param>
  public static ImportPlan Build(
    IEnumerable<string> picks,
    Func<string, IEnumerable<string>> uses,
    Func<string, IEnumerable<ImportNeed>> needs,
    Func<string, IEnumerable<string>>? offers = null)
  {
    ArgumentNullException.ThrowIfNull(picks);
    ArgumentNullException.ThrowIfNull(uses);
    ArgumentNullException.ThrowIfNull(needs);

    var comparer = StringComparer.OrdinalIgnoreCase;
    var picked = picks.Distinct(comparer).ToList();
    var taken = new HashSet<string>(picked, comparer);
    var usedBy = new Dictionary<string, List<string>>(comparer);
    var waiting = new Queue<string>(picked);
    while (waiting.TryDequeue(out var item))
    {
      foreach (var used in uses(item))
      {
        if (comparer.Equals(used, item))
        {
          continue;
        }

        if (!usedBy.TryGetValue(used, out var users))
        {
          usedBy[used] = users = [];
        }

        users.Add(item);
        if (taken.Add(used))
        {
          waiting.Enqueue(used);
        }
      }
    }

    var brought = taken.Except(picked, comparer).OrderBy(id => id, comparer)
      .Select(id => new BroughtItem(id, [.. usedBy[id].OrderBy(user => user, comparer)]))
      .ToArray();

    var needsOf = new Dictionary<ImportNeed, List<string>>();
    foreach (var item in taken)
    {
      foreach (var need in needs(item))
      {
        if (!needsOf.TryGetValue(need, out var users))
        {
          needsOf[need] = users = [];
        }

        users.Add(item);
      }
    }

    var offered = offers is null
      ? []
      : taken.SelectMany(item => offers(item).Where(id => !taken.Contains(id)).Select(id => new OfferedItem(id, item)))
        .OrderBy(o => o.For, comparer).ThenBy(o => o.Id, comparer).ToArray();

    return new ImportPlan(
      [.. picked.OrderBy(id => id, comparer)],
      brought,
      [.. needsOf.OrderBy(n => n.Key.What, comparer).ThenBy(n => n.Key.Why, comparer)
        .Select(n => new ImportNeedOf(n.Key, [.. n.Value.OrderBy(user => user, comparer)]))],
      offered);
  }
}
