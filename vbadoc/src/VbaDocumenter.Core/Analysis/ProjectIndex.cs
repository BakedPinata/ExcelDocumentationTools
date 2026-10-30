using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Analysis;

/// <summary>How a member is being used. Decides between Property Get, Let and Set.</summary>
public enum Access { Read, Let, Set }

public enum ResolvedTypeKind
{
  /// <summary>Not a project type (String, Range, Collection, ...): references through it are ignored.</summary>
  Unknown,
  /// <summary>Object, Variant or untyped: references through it can't be resolved and are reported as late-bound.</summary>
  LateBound,
  /// <summary>A class, document or form module of the project.</summary>
  Module,
  /// <summary>A user-defined Type: "x.Field" is a field access.</summary>
  UserType,
  /// <summary>An Enum name used as a qualifier: "OrderStatus.osNew".</summary>
  EnumQualifier,
}

/// <summary>What the resolver knows about the type of an expression.</summary>
public sealed record ResolvedType(ResolvedTypeKind Kind, ModuleModel? Module = null, Member? Declaration = null)
{
  public static readonly ResolvedType Unknown = new(ResolvedTypeKind.Unknown);
  public static readonly ResolvedType LateBound = new(ResolvedTypeKind.LateBound);
}

/// <summary>
/// Lookups over the whole project: modules by name, globally visible members, types, and a stable id for every member.
/// Name lookups ignore case, like VBA.
/// </summary>
public sealed class ProjectIndex
{
  static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

  readonly IReadOnlyList<ModuleModel> _moduleList;
  readonly Dictionary<string, ModuleModel> _modules = new(Names);
  readonly Dictionary<Member, string> _ids = new(ReferenceEqualityComparer.Instance);
  readonly Dictionary<Member, ModuleModel> _owners = new(ReferenceEqualityComparer.Instance);
  readonly Dictionary<string, Member> _globals = new(Names);
  readonly HashSet<string> _interfaces = new(Names);

  public ProjectIndex(ProjectModel project)
  {
    _moduleList = project.Modules;
    foreach (var module in project.Modules)
    {
      _modules.TryAdd(module.Name, module);
      AssignIds(module);
    }

    // Names usable without qualification from anywhere: public members of standard modules, and the values
    // of public Enums in any module. If two modules declare the same name, VBA reports an ambiguous name;
    // here the first module (in name order) wins.
    foreach (var module in project.Modules)
      foreach (var member in module.Members)
      {
        bool isGlobal = member.Visibility == Visibility.Public &&
            (member.Kind == MemberKind.EnumMember || (module.Kind == VbaModuleKind.Standard && IsValueLike(member)));
        if (isGlobal) _globals.TryAdd(member.Name, member);
      }

    foreach (var module in project.Modules)
      foreach (var name in module.Implements)
        if (FindModule(LastSegment(name)) is { Kind: VbaModuleKind.Class } iface)
          _interfaces.Add(iface.Name);
  }

  public string IdOf(Member member) => _ids[member];

  public ModuleModel OwnerOf(Member member) => _owners[member];

  /// <summary>
  /// Finds a module by name. "ThisWorkbook" also finds the workbook's document module when it has another name,
  /// as in non-English Excel (e.g. "DieseArbeitsmappe"); it is recognised by the Workbook class id in VB_Base.
  /// </summary>
  public ModuleModel? FindModule(string name)
  {
    if (_modules.TryGetValue(name, out var module)) return module;
    if (!name.Equals("ThisWorkbook", StringComparison.OrdinalIgnoreCase)) return null;
    return _moduleList.FirstOrDefault(m => m.Kind == VbaModuleKind.Document
        && m.Attributes.TryGetValue("VB_Base", out var classId)
        && classId.StartsWith(WorkbookClassId, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>VB_Base of the workbook's document module ("0" followed by Excel's Workbook class id).</summary>
  const string WorkbookClassId = "0{00020819-0000-0000-C000-000000000046}";

  /// <summary>A class is an interface if another module implements it.</summary>
  public bool IsInterface(ModuleModel module) => _interfaces.Contains(module.Name);

  /// <summary>Member ids: "Module.Name", "Module.Name[Get]" for properties, "Module.Enum.Value" for enum values.
  /// A name declared twice (e.g. a Declare in both branches of #If) gets "~2" on the second id.</summary>
  void AssignIds(ModuleModel module)
  {
    var used = new HashSet<string>(Names);
    foreach (var member in module.Members)
    {
      var id = member.Kind == MemberKind.EnumMember
          ? $"{module.Name}.{member.ParentName}.{member.Name}"
          : $"{module.Name}.{member.Name}";
      id += member.Kind switch
      {
        MemberKind.PropertyGet => "[Get]",
        MemberKind.PropertyLet => "[Let]",
        MemberKind.PropertySet => "[Set]",
        _ => "",
      };
      var unique = id;
      for (int n = 2; !used.Add(unique); n++) unique = $"{id}~{n}";
      _ids[member] = unique;
      _owners[member] = module;
    }
  }

  /// <summary>
  /// Finds a member of <paramref name="module"/> that can be referenced by name: anything except Enum, Type and
  /// Event declarations. Private members are only found when <paramref name="includePrivate"/> is set.
  /// Several properties can share a name; <paramref name="access"/> picks Get, Let or Set.
  /// </summary>
  public Member? FindMember(ModuleModel module, string name, Access access, bool includePrivate)
  {
    var candidates = module.Members
        .Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && IsValueLike(m))
        .Where(m => includePrivate || m.Visibility != Visibility.Private)
        .ToList();
    if (candidates.Count <= 1) return candidates.FirstOrDefault();

    var wanted = access switch
    {
      Access.Let => MemberKind.PropertyLet,
      Access.Set => MemberKind.PropertySet,
      _ => MemberKind.PropertyGet,
    };
    return candidates.FirstOrDefault(m => m.Kind == wanted)
        ?? candidates.FirstOrDefault(m => m.Kind == MemberKind.PropertyGet)
        ?? candidates[0];
  }

  public Member? FindGlobal(string name) => _globals.GetValueOrDefault(name);

  /// <summary>Finds an Enum or Type declaration visible from <paramref name="context"/>: its own, or a Public one elsewhere.</summary>
  public Member? FindTypeDeclaration(string name, ModuleModel context, MemberKind kind)
  {
    bool Matches(Member m) => m.Kind == kind && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
    return context.Members.FirstOrDefault(Matches)
        ?? _moduleList.SelectMany(m => m.Members).FirstOrDefault(m => Matches(m) && m.Visibility == Visibility.Public);
  }

  /// <summary>Turns a declared type name into what the resolver needs to know about it.</summary>
  public ResolvedType TypeFromName(string? typeName, ModuleModel context)
  {
    if (typeName == null) return ResolvedType.LateBound; // untyped means Variant
    var name = LastSegment(typeName);
    if (name.Equals("Object", StringComparison.OrdinalIgnoreCase) || name.Equals("Variant", StringComparison.OrdinalIgnoreCase))
      return ResolvedType.LateBound;
    if (FindModule(name) is { Kind: not VbaModuleKind.Standard } module)
      return new ResolvedType(ResolvedTypeKind.Module, module);
    if (FindTypeDeclaration(name, context, MemberKind.Type) is { } userType)
      return new ResolvedType(ResolvedTypeKind.UserType, Declaration: userType);
    return ResolvedType.Unknown;
  }

  /// <summary>
  /// The node a type name in a declaration points to: a class/document/form module, or a Type or Enum
  /// declaration. Null for types outside the project (String, Range, ...).
  /// </summary>
  public string? TypeTargetId(string? typeName, ModuleModel context)
  {
    if (typeName == null) return null;
    var name = LastSegment(typeName);
    if (FindModule(name) is { Kind: not VbaModuleKind.Standard } module) return module.Name;
    var declaration = FindTypeDeclaration(name, context, MemberKind.Type) ?? FindTypeDeclaration(name, context, MemberKind.Enum);
    return declaration == null ? null : IdOf(declaration);
  }

  /// <summary>"Excel.Range" -> "Range". Also drops a fixed-length suffix: "String * 10" -> "String".</summary>
  public static string LastSegment(string typeName)
  {
    var name = typeName.Split('*')[0].Trim();
    return name[(name.LastIndexOf('.') + 1)..];
  }

  static bool IsValueLike(Member m) => m.Kind is not (MemberKind.Enum or MemberKind.Type or MemberKind.Event);
}
