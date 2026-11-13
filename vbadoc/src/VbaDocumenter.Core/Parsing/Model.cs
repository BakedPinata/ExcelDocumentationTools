using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Parsing;

public enum MemberKind
{
  Sub,
  Function,
  PropertyGet,
  PropertyLet,
  PropertySet,
  /// <summary>Module-level variable (Dim, Private, Public, Global).</summary>
  Variable,
  Constant,
  Enum,
  /// <summary>One value of an Enum; <see cref="Member.ParentName"/> is the Enum.</summary>
  EnumMember,
  Type,
  Event,
  Declare,
}

public enum Visibility { Public, Private, Friend }

/// <param name="Type">The declared type as written (e.g. "Excel.Range"), or null if untyped (Variant).</param>
public sealed record Parameter(
    string Name, string? Type, bool IsOptional, bool IsByVal, bool IsParamArray, bool IsArray, string? DefaultValue);

/// <summary>A variable or constant declared inside a procedure (Dim, Static, Const, or ReDim with a type).</summary>
public sealed record LocalVariable(string Name, string? Type, bool IsArray, bool IsConstant, int Line);

/// <summary>A field of a user-defined Type.</summary>
/// <param name="Line">The field's line, where its type is referred to.</param>
public sealed record TypeField(string Name, string? Type, bool IsArray, int Line);

/// <summary>One statement inside a procedure, after splitting at ":" separators. Comments are removed.</summary>
/// <param name="Line">Physical line where the statement's logical line starts.</param>
public sealed record Statement(string Text, int Line)
{
  /// <summary>How many more physical lines it runs onto with " _" continuations; 0 for most.</summary>
  public int ContinuedLines { get; init; }

  /// <summary>
  /// The innermost block it's in, as its place in the procedure's <see cref="Member.Blocks"/>, or null at the top of the
  /// procedure. A block's own statement (its condition, say) is in the block around it.
  /// </summary>
  public int? Block { get; init; }

  /// <summary>
  /// The block this statement opens with something of its own to work out: an If's or ElseIf's condition, a Select
  /// Case's value, a Case's values, a loop's bounds or condition, or a With's object. Null for every other statement,
  /// an Else among them. What it calls is called for that block (step L3 of the generalisation plan).
  /// </summary>
  public int? Opens { get; init; }
}

/// <summary>
/// A block statement in a procedure (step E3 of the feedback plan): an If, ElseIf or Else branch, a Select Case or one of
/// its Cases, a For, Do or While loop, a With, or an On Error handler.
/// </summary>
/// <param name="Id">Its place in the procedure's <see cref="Member.Blocks"/>.</param>
/// <param name="Kind">"if", "elseIf", "else", "select", "case", "for", "do", "while", "with" or "onError".</param>
/// <param name="Text">Its statement as the parser has it, such as <c>If Not ready Then</c> or <c>Case "NEW"</c>; for a
/// handler, "On error: " and its label.</param>
/// <param name="Line">Its line.</param>
/// <param name="Parent">The block it's in, or null at the top of the procedure. ElseIf and Else are beside their If.</param>
public sealed record CodeBlock(int Id, string Kind, string Text, int Line, int? Parent);

/// <summary>
/// Anything declared at module level: a procedure, variable, constant, Enum (and each of its values), Type, Event or Declare.
/// A name declared again under <c>#If</c> is one member: VBA compiles one branch, so its declarations are versions of the
/// same thing. <see cref="Type"/> and <see cref="Parameters"/> are the first version's; the others' are in
/// <see cref="AlternativeTypes"/> and <see cref="AlternativeParameters"/>.
/// </summary>
public sealed class Member
{
  public required string Name { get; init; }
  public required MemberKind Kind { get; init; }
  public required Visibility Visibility { get; init; }

  /// <summary>
  /// Declared type as written: a variable's or constant's type, a Function's or Property Get's return type, or a
  /// Declare Function's return type. Null when there is no "As" clause or type-declaration character.
  /// </summary>
  public string? Type { get; init; }

  /// <summary>True for array variables (Dim a() As Long) and functions returning arrays (As Long()).</summary>
  public bool IsArray { get; init; }

  public bool IsWithEvents { get; init; }

  /// <summary>True for "As New", which creates the object automatically on first use.</summary>
  public bool IsAutoNew { get; init; }

  public bool IsStatic { get; init; }

  /// <summary>Declare Function (returns a value) rather than Declare Sub.</summary>
  public bool IsFunctionDeclare { get; init; }

  /// <summary>A constant's value or an enum value's explicit value, as written.</summary>
  public string? Value { get; init; }

  /// <summary>For an <see cref="MemberKind.EnumMember"/>, the name of its Enum.</summary>
  public string? ParentName { get; init; }

  /// <summary>For a Declare: the DLL and the optional Alias.</summary>
  public string? Library { get; init; }
  public string? Alias { get; init; }

  public IReadOnlyList<Parameter> Parameters { get; init; } = [];

  /// <summary>For a Type: its fields.</summary>
  public IReadOnlyList<TypeField> Fields => FieldsList;

  /// <summary>First physical line of the declaration itself.</summary>
  public required int DeclarationLine { get; init; }

  /// <summary>
  /// First line of the member's source: the comment block directly above it if there is one, otherwise the declaration;
  /// for a member declared again under #If, the #If line, if that's earlier.
  /// </summary>
  public required int StartLine { get => _startLine; init => _startLine = value; }

  int _startLine;

  /// <summary>Last line of the member's source: e.g. "End Sub", "End Enum", or the end of the declaration line.</summary>
  public int EndLine { get; internal set; }

  /// <summary>
  /// What the member says about itself (see <see cref="Descriptions"/>): its <c>VB_Description</c> attribute, otherwise
  /// the comments directly above it; null when it has neither.
  /// </summary>
  public string? Description { get; internal set; }

  /// <summary>Procedures only: variables and constants declared in the body. Parameters are in <see cref="Parameters"/>.</summary>
  public IReadOnlyList<LocalVariable> Locals => LocalsList;

  /// <summary>Procedures only: every statement between the header and the End line, with labels removed.</summary>
  public IReadOnlyList<Statement> Body => BodyList;

  /// <summary>For a procedure: its block statements, in order (see <see cref="CodeBlock"/>).</summary>
  public IReadOnlyList<CodeBlock> Blocks => BlocksList;

  /// <summary>
  /// The types of the member's other versions under #If that differ from <see cref="Type"/> and each other, in source
  /// order; null is no type (Variant). Empty for a member declared once.
  /// </summary>
  public IReadOnlyList<string?> AlternativeTypes => AlternativeTypesList;

  /// <summary>Every type the member is declared with: <see cref="Type"/>, then <see cref="AlternativeTypes"/>.</summary>
  public IReadOnlyList<string?> Types => [Type, .. AlternativeTypesList];

  /// <summary>
  /// The parameter lists of the member's other versions under #If that differ from <see cref="Parameters"/> and each
  /// other, in source order. Empty for a member declared once.
  /// </summary>
  public IReadOnlyList<IReadOnlyList<Parameter>> AlternativeParameters => AlternativeParametersList;

  /// <summary>Every type a parameter is declared with across the member's versions, first version first.</summary>
  public IReadOnlyList<string?> ParameterTypes(string name)
  {
    var types = new List<string?>();
    foreach (var list in AlternativeParametersList.Prepend(Parameters))
      foreach (var p in list)
        if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !types.Any(t => SameType(t, p.Type))) types.Add(p.Type);
    return types;
  }

  // The parser fills these in as it reads the lines after the declaration.
  internal List<LocalVariable> LocalsList { get; } = [];
  internal List<Statement> BodyList { get; } = [];
  internal List<CodeBlock> BlocksList { get; } = [];
  internal List<TypeField> FieldsList { get; } = [];
  internal List<string?> AlternativeTypesList { get; } = [];
  internal List<IReadOnlyList<Parameter>> AlternativeParametersList { get; } = [];

  /// <summary>Adds another version of the member, declared under #If: its type and parameters, where they differ.</summary>
  internal void AddVersion(string? type, IReadOnlyList<Parameter> parameters)
  {
    if (!Types.Any(t => SameType(t, type))) AlternativeTypesList.Add(type);
    if (!SameParameters(Parameters, parameters) && !AlternativeParametersList.Any(l => SameParameters(l, parameters)))
      AlternativeParametersList.Add(parameters);
  }

  /// <summary>Moves the start back, to take in an earlier line such as the #If above the first version.</summary>
  internal void StartNoLaterThan(int line) => _startLine = Math.Min(_startLine, line);

  static bool SameType(string? a, string? b) => string.Equals(a ?? "Variant", b ?? "Variant", StringComparison.OrdinalIgnoreCase);

  static bool SameParameters(IReadOnlyList<Parameter> a, IReadOnlyList<Parameter> b) =>
      a.Count == b.Count && a.Zip(b).All(pair => pair.First.Name.Equals(pair.Second.Name, StringComparison.OrdinalIgnoreCase)
          && SameType(pair.First.Type, pair.Second.Type) && pair.First.IsArray == pair.Second.IsArray
          && pair.First.DefaultValue == pair.Second.DefaultValue);

  public bool IsProcedure => Kind is MemberKind.Sub or MemberKind.Function
      or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

  public bool IsProperty => Kind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

  public override string ToString() => $"{Kind} {Name} [{StartLine}-{EndLine}]";
}

/// <summary>The parsed structure of one module.</summary>
public sealed class ModuleModel
{
  public required string Name { get; init; }
  public required VbaModuleKind Kind { get; init; }

  /// <summary>Members in source order.</summary>
  public required IReadOnlyList<Member> Members { get; init; }

  /// <summary>Interfaces named in "Implements" statements, as written.</summary>
  public required IReadOnlyList<string> Implements { get; init; }

  /// <summary>Module attributes such as VB_PredeclaredId, with quotes removed from string values.</summary>
  public required IReadOnlyDictionary<string, string> Attributes { get; init; }

  /// <summary>"Option" statements, e.g. "Option Explicit".</summary>
  public required IReadOnlyList<string> Options { get; init; }

  /// <summary>
  /// The folder from a <c>'@Folder("Parent.Child")</c> comment in the declarations (the Rubberduck convention), as
  /// written, or null. The page groups modules by it, splitting it at the dots.
  /// </summary>
  public string? Folder { get; init; }

  /// <summary>
  /// What the module says about itself (see <see cref="Descriptions"/>): its <c>VB_Description</c> attribute, otherwise
  /// the comments at its top; null when it has neither.
  /// </summary>
  public string? Description { get; init; }

  /// <summary>Number of physical lines in the file.</summary>
  public required int LineCount { get; init; }

  /// <summary>The file's physical lines (line 1 at index 0), to tell which line of a continued statement a reference is on.</summary>
  public IReadOnlyList<string> SourceLines { get; init; } = [];

  /// <summary>Lines the parser didn't understand or that look malformed. Parsing carries on regardless.</summary>
  public required IReadOnlyList<string> Warnings { get; init; }

  /// <summary>True if the class has a default instance that can be used by its name (VB_PredeclaredId = True).</summary>
  public bool HasPredeclaredId => Attributes.TryGetValue("VB_PredeclaredId", out var v) && v.Equals("True", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The parsed structure of the whole project.</summary>
public sealed class ProjectModel
{
  public required string Name { get; init; }
  public required int CodePage { get; init; }

  /// <summary>Modules in the same order as the <see cref="VbaProject"/> they came from (sorted by name).</summary>
  public required IReadOnlyList<ModuleModel> Modules { get; init; }
}
