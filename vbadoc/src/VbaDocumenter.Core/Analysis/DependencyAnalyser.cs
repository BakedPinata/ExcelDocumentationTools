using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Analysis;

/// <summary>Builds the module- and member-level dependency graphs from the parsed project.</summary>
public static class DependencyAnalyser
{
  public static DependencyModel Analyse(ProjectModel project)
  {
    var index = new ProjectIndex(project);
    var edges = new EdgeCollector();
    var lateBound = new List<LateBoundReference>();
    var resolver = new ReferenceResolver(index, edges, lateBound);
    var moduleImplements = new List<Edge>();

    foreach (var module in project.Modules)
    {
      foreach (var member in module.Members)
        AddDeclarationReferences(module, member, index, edges, resolver);
      AddImplements(module, index, edges, moduleImplements);
      AddWithEventsHandlers(module, index, edges);
    }

    var memberEdges = Sorted(edges.Edges);
    var members = BuildMemberNodes(project, index, memberEdges);
    var moduleEdges = Sorted(ModuleEdges(memberEdges, members, moduleImplements));

    return new DependencyModel
    {
      Project = project,
      Modules = project.Modules.Select(m => new ModuleNode(m.Name, m, index.IsInterface(m))).ToList(),
      Members = members,
      ModuleEdges = moduleEdges,
      MemberEdges = memberEdges,
      LateBound = lateBound,
    };
  }

  /// <summary>References made by a declaration itself: its types, constant values, defaults, and (for procedures) its body.</summary>
  static void AddDeclarationReferences(ModuleModel module, Member member, ProjectIndex index, EdgeCollector edges, ReferenceResolver resolver)
  {
    var from = index.IdOf(member);
    void TypeReference(string? typeName, EdgeKind kind = EdgeKind.Uses)
    {
      if (index.TypeTargetId(typeName, module) is { } target) edges.Add(from, module, target, kind);
    }

    switch (member.Kind)
    {
      case MemberKind.Variable:
        // A WithEvents variable links its module to the class whose events it receives.
        TypeReference(member.Type, member.IsWithEvents ? EdgeKind.Events : EdgeKind.Uses);
        break;
      case MemberKind.Constant:
        TypeReference(member.Type);
        resolver.ResolveExpression(member.Value!, module, member);
        break;
      case MemberKind.EnumMember:
        if (member.Value != null) resolver.ResolveExpression(member.Value, module, member);
        break;
      case MemberKind.Type:
        foreach (var field in member.Fields) TypeReference(field.Type);
        break;
      case MemberKind.Enum:
        break;
      default: // procedures, Event, Declare
        TypeReference(member.Type);
        foreach (var parameter in member.Parameters)
        {
          TypeReference(parameter.Type);
          if (parameter.DefaultValue != null) resolver.ResolveExpression(parameter.DefaultValue, module, member);
        }
        foreach (var local in member.Locals) TypeReference(local.Type);
        if (member.IsProcedure) resolver.ResolveBody(module, member);
        break;
    }
  }

  /// <summary>
  /// "Implements IFoo" links the class to IFoo, and each procedure named "IFoo_Member" to IFoo's Member
  /// (matching Sub, Function or Property kind).
  /// </summary>
  static void AddImplements(ModuleModel module, ProjectIndex index, EdgeCollector edges, List<Edge> moduleImplements)
  {
    foreach (var name in module.Implements)
    {
      var iface = index.FindModule(ProjectIndex.LastSegment(name));
      if (iface == null) continue; // an interface from a referenced library, not this project
      moduleImplements.Add(new Edge(module.Name, iface.Name, EdgeKind.Implements, 1));

      var prefix = iface.Name + "_";
      foreach (var procedure in module.Members.Where(m => m.IsProcedure && m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
      {
        var memberName = procedure.Name[prefix.Length..];
        var target = iface.Members.FirstOrDefault(m => m.Kind == procedure.Kind && Same(m.Name, memberName))
                     ?? iface.Members.FirstOrDefault(m => m.IsProcedure && Same(m.Name, memberName));
        edges.Add(index.IdOf(procedure), module, target == null ? iface.Name : index.IdOf(target), EdgeKind.Implements);
      }
    }
  }

  /// <summary>A handler such as "m_svc_Progress" for "WithEvents m_svc As OrderService" links to OrderService's Progress event.</summary>
  static void AddWithEventsHandlers(ModuleModel module, ProjectIndex index, EdgeCollector edges)
  {
    foreach (var variable in module.Members.Where(m => m.Kind == MemberKind.Variable && m.IsWithEvents))
    {
      if (index.TypeFromName(variable.Type, module) is not { Kind: ResolvedTypeKind.Module, Module: { } source }) continue;

      var prefix = variable.Name + "_";
      foreach (var handler in module.Members.Where(m => m.IsProcedure && m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
      {
        var eventName = handler.Name[prefix.Length..];
        var declaration = source.Members.FirstOrDefault(m => m.Kind == MemberKind.Event && Same(m.Name, eventName));
        if (declaration != null) edges.Add(index.IdOf(handler), module, index.IdOf(declaration), EdgeKind.Events);
      }
    }
  }

  static List<MemberNode> BuildMemberNodes(ProjectModel project, ProjectIndex index, IReadOnlyList<Edge> memberEdges)
  {
    // A node counts as referenced if something other than itself points to it (recursion doesn't count).
    var referenced = memberEdges.Where(e => e.From != e.To).Select(e => e.To).ToHashSet(StringComparer.Ordinal);

    var nodes = new List<MemberNode>();
    foreach (var module in project.Modules)
    {
      nodes.Add(new MemberNode(module.Name, module, null, MemberRole.None, IsUnreferenced: false));
      foreach (var member in module.Members)
      {
        var role = RoleOf(module, member);
        bool unreferenced = role == MemberRole.None
            && member.Kind != MemberKind.EnumMember
            && !IsReferenced(module, member, index, referenced);
        nodes.Add(new MemberNode(index.IdOf(member), module, member, role, unreferenced));
      }
    }
    return nodes;
  }

  /// <summary>
  /// Whether anything refers to the member. Declarations repeated under #If / #Else share one answer, and an
  /// Enum counts as used if any of its values is.
  /// </summary>
  static bool IsReferenced(ModuleModel module, Member member, ProjectIndex index, HashSet<string> referenced)
  {
    var sameDeclarations = module.Members.Where(m => m.Kind == member.Kind && Same(m.Name, member.Name));
    if (sameDeclarations.Any(m => referenced.Contains(index.IdOf(m)))) return true;

    return member.Kind == MemberKind.Enum && module.Members.Any(m =>
        m.Kind == MemberKind.EnumMember && Same(m.ParentName!, member.Name) && referenced.Contains(index.IdOf(m)));
  }

  /// <summary>Recognises procedures that are called by Excel, VBA or the user rather than by other code.</summary>
  public static MemberRole RoleOf(ModuleModel module, Member member)
  {
    if (!member.IsProcedure) return MemberRole.None;
    bool StartsWith(string prefix) => member.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    if (module.Implements.Any(i => StartsWith(ProjectIndex.LastSegment(i) + "_"))) return MemberRole.InterfaceImplementation;
    if (module.Members.Any(v => v.Kind == MemberKind.Variable && v.IsWithEvents && StartsWith(v.Name + "_"))) return MemberRole.WithEventsHandler;

    bool isSubWithUnderscore = member.Kind == MemberKind.Sub && member.Name.Contains('_');
    return module.Kind switch
    {
      VbaModuleKind.Class when StartsWith("Class_Initialize") || StartsWith("Class_Terminate") => MemberRole.BuiltInEventHandler,
      VbaModuleKind.Document when StartsWith("Workbook_") || StartsWith("Worksheet_") || StartsWith("Chart_") => MemberRole.BuiltInEventHandler,
      VbaModuleKind.Document when isSubWithUnderscore => MemberRole.ControlEventHandler,
      VbaModuleKind.Form when StartsWith("UserForm_") => MemberRole.BuiltInEventHandler,
      VbaModuleKind.Form when isSubWithUnderscore => MemberRole.ControlEventHandler,
      VbaModuleKind.Standard when member.Visibility == Visibility.Public && member.Kind == MemberKind.Sub && member.Parameters.Count == 0 => MemberRole.Macro,
      VbaModuleKind.Standard when member.Visibility == Visibility.Public && member.Kind == MemberKind.Function => MemberRole.WorksheetFunction,
      _ => MemberRole.None,
    };
  }

  /// <summary>
  /// Module-level edges: member edges between different modules added up, plus one Implements edge per
  /// Implements statement. Member-level Implements edges are left out so the module count isn't doubled.
  /// </summary>
  static IEnumerable<Edge> ModuleEdges(IReadOnlyList<Edge> memberEdges, List<MemberNode> members, List<Edge> moduleImplements)
  {
    var moduleOf = members.ToDictionary(n => n.Id, n => n.Module.Name, StringComparer.Ordinal);
    var counts = new Dictionary<(string, string, EdgeKind), int>();
    foreach (var edge in memberEdges.Where(e => e.Kind != EdgeKind.Implements).Concat(moduleImplements))
    {
      var from = moduleOf.GetValueOrDefault(edge.From, edge.From);
      var to = moduleOf.GetValueOrDefault(edge.To, edge.To);
      if (from == to) continue;
      counts[(from, to, edge.Kind)] = counts.GetValueOrDefault((from, to, edge.Kind)) + edge.Count;
    }
    return counts.Select(c => new Edge(c.Key.Item1, c.Key.Item2, c.Key.Item3, c.Value));
  }

  /// <summary>Sorts edges so the output never depends on dictionary order.</summary>
  static List<Edge> Sorted(IEnumerable<Edge> edges) =>
      edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ThenBy(e => e.Kind).ToList();

  static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
