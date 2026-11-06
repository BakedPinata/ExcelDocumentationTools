using ExcelDocumenter.Shared.Importing;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Importing;

/// <summary>How a choice of modules compares with what a target has.</summary>
/// <param name="New">The modules it doesn't have.</param>
/// <param name="Identical">The modules it has, word for word, which are left alone without asking.</param>
/// <param name="Clashes">The names it has for something else.</param>
public sealed record TargetComparison(IReadOnlyList<string> New, IReadOnlyList<string> Identical, IReadOnlyList<ImportClash> Clashes);

/// <summary>
/// Finds what's in the way of a choice of modules, and says what's at stake with each: what else in the target uses
/// what would be replaced, and what's coming that would use the target's version instead.
/// </summary>
public static class ModuleClashes
{
  /// <summary>Compares the plan's modules with the target's.</summary>
  /// <param name="source">The project they come from.</param>
  /// <param name="sourceName">The source's file name.</param>
  /// <param name="plan">What's coming.</param>
  /// <param name="import">What uses what in the source.</param>
  /// <param name="target">The target.</param>
  public static TargetComparison Compare(VbaProject source, string sourceName, ImportPlan plan, ModuleImport import, TargetWorkbook target)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(import);
    ArgumentNullException.ThrowIfNull(target);

    var incoming = source.Modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    var theirs = (target.Project?.Modules ?? []).ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    var codeNames = target.CodeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var usedInTarget = new Lazy<ILookup<string, string>>(() => UsedBy(target.Project));

    var added = new List<string>();
    var identical = new List<string>();
    var clashes = new List<ImportClash>();
    foreach (var id in plan.Items)
    {
      var module = incoming[id];
      var comingUsers = plan.Items.Where(item => !item.Equals(id, StringComparison.OrdinalIgnoreCase) && import.Uses(item).Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
      if (codeNames.Contains(id))
      {
        clashes.Add(CantBeReplaced(id, "a sheet's or the workbook's module", comingUsers));
      }
      else if (!theirs.TryGetValue(id, out var existing))
      {
        added.Add(id);
      }
      else if (existing.Kind == module.Kind && Same(existing, module))
      {
        identical.Add(id);
      }
      else if (existing.Kind is VbaModuleKind.Document or VbaModuleKind.Form)
      {
        clashes.Add(CantBeReplaced(id, existing.Kind == VbaModuleKind.Form ? "a UserForm" : "a sheet's or the workbook's module", comingUsers));
      }
      else
      {
        var problem = existing.Kind == module.Kind
          ? $"The target already has {id}, and it differs from the one in {sourceName}."
          : $"The target's {id} is {KindOf(existing.Kind)}, and the one in {sourceName} is {KindOf(module.Kind)}.";
        clashes.Add(new ImportClash(id, problem, IfOverwritten(usedInTarget.Value[id].ToList()), IfSkipped(comingUsers)));
      }
    }

    return new TargetComparison(added, identical, clashes);
  }

  // Only what's at the end can differ without meaning anything: the VBA editor keeps or drops a last line as it likes.
  static bool Same(VbaModule a, VbaModule b) => a.Source.TrimEnd().Equals(b.Source.TrimEnd(), StringComparison.Ordinal);

  static ILookup<string, string> UsedBy(VbaProject? project) =>
    (project is null ? [] : DependencyAnalyser.Analyse(ProjectParser.Parse(project)).ModuleEdges)
      .Where(e => !e.From.Equals(e.To, StringComparison.OrdinalIgnoreCase))
      .Select(e => (e.To, e.From)).Distinct()
      .ToLookup(e => e.To, e => e.From, StringComparer.OrdinalIgnoreCase);

  static ImportClash CantBeReplaced(string id, string what, List<string> comingUsers) => new(
    id,
    $"{id} is {what} in the target, which can't be replaced.",
    null,
    comingUsers.Count == 0 ? "Leaves this one out." : $"Leaves this one out, so {Names(comingUsers)} will find the target's {id} instead.");

  static string IfOverwritten(List<string> users) => users.Count == 0
    ? "Replaces the target's version, which nothing else there uses."
    : $"Replaces the target's version, used there by {Names(users)}.";

  static string IfSkipped(List<string> comingUsers) => comingUsers.Count == 0
    ? "Keeps the target's version."
    : $"Keeps the target's version, which may differ, for {Names(comingUsers)} to use.";

  static string Names(IEnumerable<string> names) => string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase));

  static string KindOf(VbaModuleKind kind) => kind switch
  {
    VbaModuleKind.Standard => "a standard module",
    VbaModuleKind.Class => "a class",
    VbaModuleKind.Form => "a UserForm",
    _ => "a sheet's or the workbook's module",
  };
}
