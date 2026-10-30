using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Output;

/// <summary>
/// One line in a class diagram box, as plain text. The page turns it into Mermaid, escaping as it goes.
/// A line with <see cref="Parameters"/> (possibly empty) is drawn as a method, e.g. "+Describe(x As Long) String";
/// one without is drawn as an attribute, e.g. "-m_Id : String". Mermaid puts attributes and methods in separate
/// compartments.
/// </summary>
/// <param name="Text">Visibility and name, plus the type for attributes: "+Describe", "-Const SEP : String = ",""</param>
/// <param name="Parameters">For methods, the parameter list without the brackets.</param>
/// <param name="Type">For methods, the return type.</param>
public sealed record PageClassLine(string Text, string? Parameters = null, string? Type = null);

/// <summary>A relationship between two boxes in the class diagram.</summary>
/// <param name="Kind">"realization" (From implements To), "association" (From holds a To in a module-level
/// variable) or "dependency" (any other use of To).</param>
/// <param name="Label">For associations, the variable name ("WithEvents m_svc" for event sources).</param>
public sealed record PageClassEdge(string From, string To, string Kind, string? Label);

/// <summary>Works out the class diagram's box contents and relationships from the parsed project.</summary>
public static class ClassDiagram
{
  const int MaxValueLength = 40;

  /// <summary>
  /// The line shown for each member of a module. Members without a line aren't shown: enum values (the Enum is),
  /// the second and third accessors of a property, which is shown once on its first accessor, and a declaration
  /// repeated under #If/#Else that would give exactly the same line.
  /// </summary>
  public static Dictionary<Member, PageClassLine> Lines(ModuleModel module)
  {
    var lines = new Dictionary<Member, PageClassLine>();
    var propertiesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var linesSeen = new HashSet<PageClassLine>();
    foreach (var m in module.Members)
    {
      if (m.Kind == MemberKind.EnumMember) continue;
      PageClassLine line;
      if (IsProperty(m))
      {
        if (!propertiesSeen.Add(m.Name)) continue;
        line = PropertyLine(m, module.Members.Where(o => IsProperty(o) && string.Equals(o.Name, m.Name, StringComparison.OrdinalIgnoreCase)).ToList());
      }
      else
      {
        line = Line(m);
      }
      if (linesSeen.Add(line)) lines[m] = line;
    }
    return lines;
  }

  static bool IsProperty(Member m) => m.Kind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

  static string Visibility(Member m) => m.Visibility switch
  {
    Parsing.Visibility.Private => "-",
    Parsing.Visibility.Friend => "~",
    _ => "+",
  };

  static string? TypeText(string? type, bool isArray) => type == null ? null : type + (isArray ? "()" : "");

  static PageClassLine Line(Member m)
  {
    string v = Visibility(m);
    string parameters = MemberFormatter.Parameters(m);
    string? returnType = TypeText(m.Type, m.IsArray);
    return m.Kind switch
    {
      MemberKind.Sub => new($"{v}{m.Name}", parameters),
      MemberKind.Function => new($"{v}{m.Name}", parameters, returnType),
      MemberKind.Event => new($"+Event {m.Name}", parameters),
      MemberKind.Declare => new($"{v}Declare {(m.IsFunctionDeclare ? "Function" : "Sub")} {m.Name}", parameters, returnType),
      MemberKind.Variable => new($"{v}{(m.IsWithEvents ? "WithEvents " : "")}{m.Name}{(m.IsArray ? "()" : "")}" +
                                 (m.Type == null ? "" : $" : {(m.IsAutoNew ? "New " : "")}{m.Type}")),
      MemberKind.Constant => new($"{v}Const {m.Name}{(m.Type == null ? "" : $" : {m.Type}")}" +
                                 (m.Value == null ? "" : $" = {Shorten(m.Value)}")),
      MemberKind.Enum => new($"{v}Enum {m.Name}"),
      MemberKind.Type => new($"{v}Type {m.Name}"),
      _ => new($"{v}{m.Name}"),
    };
  }

  // A property is shown once, with the accessors it has: "+Property Status : String «Get/Let»". The type is
  // the Get's return type, or else the value parameter of the Let or Set. An indexed property (one with
  // parameters other than the value) is drawn as a method so its parameters can be shown.
  static PageClassLine PropertyLine(Member first, List<Member> accessors)
  {
    var get = accessors.FirstOrDefault(a => a.Kind == MemberKind.PropertyGet);
    var setter = accessors.FirstOrDefault(a => a.Kind != MemberKind.PropertyGet);
    string? type = get != null ? TypeText(get.Type, get.IsArray) : setter?.Parameters.LastOrDefault()?.Type;
    string kinds = string.Join("/", new[] { MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet }
        .Where(k => accessors.Any(a => a.Kind == k))
        .Select(k => k.ToString()["Property".Length..]));
    string text = $"{Visibility(first)}Property {first.Name}";

    // The index parameters: all of a Get's, or all but the last (the value) of a Let's or Set's.
    var index = get != null ? get.Parameters : setter!.Parameters.Take(Math.Max(0, setter.Parameters.Count - 1)).ToList();
    if (index.Count > 0)
      return new(text, MemberFormatter.Parameters(index), $"{type ?? "Variant"} «{kinds}»");
    return new($"{text}{(type == null ? "" : $" : {type}")} «{kinds}»");
  }

  static string Shorten(string value) => value.Length <= MaxValueLength ? value : value[..(MaxValueLength - 1)] + "…";

  /// <summary>
  /// The diagram's relationships, in the module edges' order. Implements edges become realisations. A module-level
  /// variable typed as another module becomes an association labelled with the variable's name (marked for
  /// WithEvents). Every other module edge becomes a dependency, unless an association already links the two modules.
  /// </summary>
  public static List<PageClassEdge> Edges(DependencyModel model)
  {
    var modulesByName = model.Modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    var associations = new List<PageClassEdge>();
    foreach (var node in model.Modules)
    {
      foreach (var v in node.Module.Members.Where(m => m.Kind == MemberKind.Variable))
      {
        if (v.Type is not { } type) continue;
        var typeName = type[(type.LastIndexOf('.') + 1)..]; // "VBAProject.Order" names the same module as "Order"
        if (!modulesByName.TryGetValue(typeName, out var target) || target.Id == node.Id) continue;
        if (target.Module.Kind == VbaModuleKind.Standard) continue; // not a type you can hold
        associations.Add(new PageClassEdge(node.Id, target.Id, "association", (v.IsWithEvents ? "WithEvents " : "") + v.Name));
      }
    }

    var associated = associations.Select(a => (a.From, a.To)).ToHashSet();
    var edges = new List<PageClassEdge>();
    var dependencies = new HashSet<(string, string)>();
    foreach (var e in model.ModuleEdges)
    {
      if (e.Kind == EdgeKind.Implements)
        edges.Add(new PageClassEdge(e.From, e.To, "realization", null));
      else if (!associated.Contains((e.From, e.To)) && dependencies.Add((e.From, e.To)))
        edges.Add(new PageClassEdge(e.From, e.To, "dependency", null));
    }
    edges.AddRange(associations);
    return edges;
  }
}
