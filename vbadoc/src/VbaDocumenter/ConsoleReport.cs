using Serilog;
using Serilog.Events;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter;

/// <summary>Reports the parsed project and its dependencies through the logger. Detail is at Debug level, so --verbose shows it.</summary>
static class ConsoleReport
{
  /// <summary>Lists longer than this are cut short unless --verbose is given.</summary>
  public const int ShortListLength = 20;

  public static void WriteDependencies(DependencyModel model, ILogger logger)
  {
    var interfaces = model.Modules.Where(m => m.IsInterface).Select(m => m.Id).ToList();
    logger.Information("  {ModuleEdges} module dependencies, {MemberEdges} member dependencies{Interfaces}",
        model.ModuleEdges.Count,
        model.MemberEdges.Count,
        interfaces.Count > 0 ? $"; interfaces: {string.Join(", ", interfaces)}" : "");

    foreach (var group in model.ModuleEdges.GroupBy(e => (e.From, e.Kind)))
      logger.Debug("    {From} {Verb} {To}", group.Key.From, KindVerb(group.Key.Kind), string.Join(", ", group.Select(e => e.To)));

    WriteList(
        logger,
        $"{model.LateBound.Count} late-bound reference(s), which can't be resolved without running the code:",
        model.LateBound.Select(l => $"{l.FromId} (line {l.Line}): {l.Expression}").ToList());

    var unreferenced = model.Members.Where(m => m.IsUnreferenced).ToList();
    WriteList(
        logger,
        $"{unreferenced.Count} unreferenced member(s). They may still be used via Application.Run, buttons or late-bound calls:",
        unreferenced.Select(m => $"{m.Id} (line {m.Member!.DeclarationLine})").ToList());
  }

  /// <summary>Items past the short length are at Debug level, with a note saying how to see them.</summary>
  public static void WriteList(ILogger logger, string heading, List<string> items)
  {
    if (items.Count == 0) return;
    logger.Information("  {Heading}", heading);
    foreach (var item in items.Take(ShortListLength)) logger.Information("    {Item}", item);
    foreach (var item in items.Skip(ShortListLength)) logger.Debug("    {Item}", item);

    int hidden = items.Count - ShortListLength;
    if (hidden > 0 && !logger.IsEnabled(LogEventLevel.Debug))
      logger.Information("    ... and {Hidden} more (use --verbose to see all)", hidden);
  }

  static string KindVerb(EdgeKind kind) => kind switch
  {
    EdgeKind.Implements => "implements",
    EdgeKind.Events => "handles events of",
    _ => "uses",
  };

  public static void WriteModules(ProjectModel project, ILogger logger)
  {
    int nameWidth = project.Modules.Count == 0 ? 0 : project.Modules.Max(m => m.Name.Length);
    foreach (var module in project.Modules)
    {
      logger.Information("    {Kind,-9} {Name}  {Counts}", module.Kind.ToString(), module.Name.PadRight(nameWidth), Counts(module));
      foreach (var member in module.Members)
        logger.Debug("      {Start,5}-{End,-5} {Signature}", member.StartLine, member.EndLine, MemberFormatter.Signature(member));
    }

    var warnings = project.Modules.SelectMany(m => m.Warnings.Select(w => $"{m.Name} {w}")).ToList();
    if (warnings.Count == 0) return;
    logger.Warning("  {Count} parser warning(s). These lines were skipped:", warnings.Count);
    foreach (var warning in warnings) logger.Warning("    {ParserWarning}", warning);
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
