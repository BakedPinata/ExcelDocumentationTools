using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter;

/// <summary>Formats the parsed project and its dependencies for the console.</summary>
static class ConsoleReport
{
  /// <summary>Lists longer than this are cut short unless --verbose is given.</summary>
  const int ShortListLength = 20;

  public static void WriteDependencies(DependencyModel model, bool verbose)
  {
    var interfaces = model.Modules.Where(m => m.IsInterface).Select(m => m.Id).ToList();
    Console.WriteLine($"  {model.ModuleEdges.Count} module dependencies, {model.MemberEdges.Count} member dependencies" +
                      (interfaces.Count > 0 ? $"; interfaces: {string.Join(", ", interfaces)}" : ""));

    if (verbose)
    {
      foreach (var group in model.ModuleEdges.GroupBy(e => (e.From, e.Kind)))
        Console.WriteLine($"    {group.Key.From} {KindVerb(group.Key.Kind)} {string.Join(", ", group.Select(e => e.To))}");
    }

    WriteList(
        $"{model.LateBound.Count} late-bound reference(s), which can't be resolved without running the code:",
        model.LateBound.Select(l => $"{l.FromId} (line {l.Line}): {l.Expression}").ToList(),
        verbose);

    var unreferenced = model.Members.Where(m => m.IsUnreferenced).ToList();
    WriteList(
        $"{unreferenced.Count} unreferenced member(s). They may still be used via Application.Run, buttons or late-bound calls:",
        unreferenced.Select(m => $"{m.Id} (line {m.Member!.DeclarationLine})").ToList(),
        verbose);
  }

  static void WriteList(string heading, List<string> items, bool verbose)
  {
    if (items.Count == 0) return;
    Console.WriteLine($"  {heading}");
    int shown = verbose ? items.Count : Math.Min(items.Count, ShortListLength);
    foreach (var item in items.Take(shown)) Console.WriteLine($"    {item}");
    if (shown < items.Count) Console.WriteLine($"    ... and {items.Count - shown} more (use --verbose to see all)");
  }

  static string KindVerb(EdgeKind kind) => kind switch
  {
    EdgeKind.Implements => "implements",
    EdgeKind.Events => "handles events of",
    _ => "uses",
  };

  public static void WriteModules(ProjectModel project, bool verbose)
  {
    int nameWidth = project.Modules.Count == 0 ? 0 : project.Modules.Max(m => m.Name.Length);
    foreach (var module in project.Modules)
    {
      Console.WriteLine($"    {module.Kind,-9} {module.Name.PadRight(nameWidth)}  {Counts(module)}");
      if (!verbose) continue;
      foreach (var member in module.Members)
        Console.WriteLine($"      {member.StartLine,5}-{member.EndLine,-5} {MemberFormatter.Signature(member)}");
    }

    var warnings = project.Modules.SelectMany(m => m.Warnings.Select(w => $"{m.Name} {w}")).ToList();
    if (warnings.Count == 0) return;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  {warnings.Count} parser warning(s). These lines were skipped:");
    foreach (var warning in warnings) Console.WriteLine($"    {warning}");
    Console.ResetColor();
  }

  /// <summary>e.g. "7 procedures, 3 variables, 1 event".</summary>
  static string Counts(ModuleModel module)
  {
    var groups = new (string singular, string plural, Func<Member, bool> match)[]
    {
            ("procedure", "procedures", m => m.IsProcedure),
            ("variable", "variables", m => m.Kind == MemberKind.Variable),
            ("constant", "constants", m => m.Kind == MemberKind.Constant),
            ("enum", "enums", m => m.Kind == MemberKind.Enum),
            ("type", "types", m => m.Kind == MemberKind.Type),
            ("event", "events", m => m.Kind == MemberKind.Event),
            ("declare", "declares", m => m.Kind == MemberKind.Declare),
    };
    var parts = groups
        .Select(g => (g, count: module.Members.Count(g.match)))
        .Where(x => x.count > 0)
        .Select(x => $"{x.count} {(x.count == 1 ? x.g.singular : x.g.plural)}")
        .ToList();
    if (module.Implements.Count > 0) parts.Add($"implements {string.Join(", ", module.Implements)}");
    return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
  }
}
