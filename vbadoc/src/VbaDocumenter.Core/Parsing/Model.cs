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
public sealed record TypeField(string Name, string? Type, bool IsArray);

/// <summary>One statement inside a procedure, after splitting at ":" separators. Comments are removed.</summary>
/// <param name="Line">Physical line where the statement's logical line starts.</param>
public sealed record Statement(string Text, int Line);

/// <summary>
/// Anything declared at module level: a procedure, variable, constant, Enum (and each of its values), Type, Event or Declare.
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

    /// <summary>First line of the member's source: the comment block directly above it if there is one, otherwise the declaration.</summary>
    public required int StartLine { get; init; }

    /// <summary>Last line of the member's source: e.g. "End Sub", "End Enum", or the end of the declaration line.</summary>
    public int EndLine { get; internal set; }

    /// <summary>Procedures only: variables and constants declared in the body. Parameters are in <see cref="Parameters"/>.</summary>
    public IReadOnlyList<LocalVariable> Locals => LocalsList;

    /// <summary>Procedures only: every statement between the header and the End line, with labels removed.</summary>
    public IReadOnlyList<Statement> Body => BodyList;

    // The parser fills these in as it reads the lines after the declaration.
    internal List<LocalVariable> LocalsList { get; } = [];
    internal List<Statement> BodyList { get; } = [];
    internal List<TypeField> FieldsList { get; } = [];

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

    /// <summary>Number of physical lines in the file.</summary>
    public required int LineCount { get; init; }

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
