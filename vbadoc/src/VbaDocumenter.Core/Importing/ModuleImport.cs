using System.Text.RegularExpressions;
using ExcelDocumenter.Shared.Importing;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Importing;

/// <summary>
/// What can be imported from a project and what it relies on: the rules behind both the page's Export tab and the
/// import command, which is why the page gets them as data rather than working them out itself.
/// </summary>
/// <remarks>
/// Standard modules and classes can come. Depending on the workbook isn't a reason to leave something out: plain
/// <c>ThisWorkbook</c> just means the new workbook. What's listed is what the target may not have (a sheet's code name,
/// a procedure of the source's own on ThisWorkbook, a form) and what can't be followed (a procedure run by name, or a
/// late-bound call). None of it stops an import.
/// </remarks>
public sealed partial class ModuleImport
{
  const string WorkbookBase = "00020819"; // the start of the document module base GUID of a workbook, in VB_Base

  readonly Dictionary<string, ModuleNode> _modules;
  readonly ILookup<string, Edge> _usedFrom;
  readonly ILookup<string, Edge> _implementationsOf;
  readonly ILookup<string, MemberNode> _workbookMembersUsed; // by the module using them
  readonly HashSet<string> _lateBound;                       // the modules making late-bound calls

  /// <summary>Works out the lookups for a project.</summary>
  public ModuleImport(DependencyModel model)
  {
    ArgumentNullException.ThrowIfNull(model);
    _modules = model.Modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    _usedFrom = model.ModuleEdges.ToLookup(e => e.From, StringComparer.OrdinalIgnoreCase);
    _implementationsOf = model.ModuleEdges.Where(e => e.Kind == EdgeKind.Implements).ToLookup(e => e.To, StringComparer.OrdinalIgnoreCase);

    var members = model.Members.ToDictionary(m => m.Id, StringComparer.Ordinal);
    _workbookMembersUsed = model.MemberEdges
      .Where(e => members.ContainsKey(e.From) && members.TryGetValue(e.To, out var to) && to.Member is not null && IsWorkbook(to.Module))
      .ToLookup(e => members[e.From].Module.Name, e => members[e.To], StringComparer.OrdinalIgnoreCase);
    _lateBound = model.LateBound.Where(l => members.ContainsKey(l.FromId)).Select(l => members[l.FromId].Module.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>The module's name as the project spells it, or null if it hasn't one by that name.</summary>
  public string? Find(string name) => _modules.TryGetValue(name, out var node) ? node.Id : null;

  /// <summary>Why a module can't be picked, or null when it can.</summary>
  public string? WhyNot(string id) => _modules[id].Module.Kind switch
  {
    VbaModuleKind.Document => "A document module belongs to its workbook",
    VbaModuleKind.Form => "A UserForm can't be imported yet",
    _ => null,
  };

  /// <summary>The modules a module uses that can come with it.</summary>
  public IEnumerable<string> Uses(string id) => _usedFrom[id].Select(e => e.To).Where(to => WhyNot(to) is null).Distinct();

  /// <summary>The classes that implement a module, if it's an interface: nothing uses them, so nothing brings them.</summary>
  public IEnumerable<string> Implementations(string id) => _implementationsOf[id].Select(e => e.From).Where(from => WhyNot(from) is null).Distinct();

  /// <summary>What a module relies on that can't come with it, or can't be followed.</summary>
  public IReadOnlyList<ImportNeed> Needs(string id)
  {
    var needs = new List<ImportNeed>();
    foreach (var edge in _usedFrom[id].DistinctBy(e => e.To))
    {
      var used = _modules[edge.To].Module;
      if (used.Kind == VbaModuleKind.Form) needs.Add(new(used.Name, "a UserForm, which can't come along"));
      else if (used.Kind == VbaModuleKind.Document && !IsWorkbook(used)) needs.Add(new(used.Name, "a sheet's code name, so the target needs a sheet called that"));
    }

    // Plain ThisWorkbook means the target's own. A procedure added to it is the source's, and won't be there.
    needs.AddRange(_workbookMembersUsed[id].Select(m => new ImportNeed($"{m.Module.Name}.{m.Member!.Name}", "a procedure on the source's workbook module, which stays there")));

    var byName = _modules[id].Module.Members.SelectMany(m => m.Body).SelectMany(s => RunsByName().Matches(s.Text)).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase);
    needs.AddRange(byName.Select(name => new ImportNeed(name, "runs a procedure by name, which can't be followed")));
    if (_lateBound.Contains(id)) needs.Add(new("Late-bound calls", "made on an Object or Variant, so what they reach can't be followed"));

    return needs.Distinct().ToList();
  }

  /// <summary>Every module, in name order, with what it is and why it can't be taken if it can't.</summary>
  public IReadOnlyList<ImportableItem> Items() =>
  [
    .. _modules.Values
      .OrderBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
      .Select(node => new ImportableItem(node.Id, KindOf(node.Module.Kind), WhyNot(node.Id))),
  ];

  static string KindOf(VbaModuleKind kind) => kind switch
  {
    VbaModuleKind.Standard => "module",
    VbaModuleKind.Class => "class",
    VbaModuleKind.Form => "UserForm",
    _ => "document module",
  };

  /// <summary>What a choice of modules comes to. Each pick must be one that <see cref="WhyNot"/> allows.</summary>
  public ImportPlan Plan(IEnumerable<string> picks) => ImportPlan.Build(picks, Uses, Needs, Implementations);

  // The workbook's own document module, whatever it's been renamed; the fixtures' hand-written ones have no VB_Base.
  static bool IsWorkbook(ModuleModel module) =>
      module.Kind == VbaModuleKind.Document && (module.Attributes.TryGetValue("VB_Base", out var vbBase)
        ? vbBase.Contains(WorkbookBase, StringComparison.Ordinal)
        : module.Name.Equals("ThisWorkbook", StringComparison.OrdinalIgnoreCase));

  [GeneratedRegex(@"\bApplication\.Run\b|\bCallByName\b|\.OnAction\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex RunsByName();
}
