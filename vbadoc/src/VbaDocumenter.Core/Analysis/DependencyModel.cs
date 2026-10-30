using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Analysis;

public enum EdgeKind
{
    /// <summary>Ordinary references, calls and type references.</summary>
    Uses,
    /// <summary>A class implementing an interface, or a procedure implementing an interface member.</summary>
    Implements,
    /// <summary>RaiseEvent, WithEvents variables, and event handlers.</summary>
    Events,
}

/// <summary>Why a procedure may be called without any reference in the code. Used to avoid flagging it as unreferenced.</summary>
public enum MemberRole
{
    None,
    /// <summary>Public parameterless Sub in a standard module: can be run as a macro.</summary>
    Macro,
    /// <summary>Public Function in a standard module: may be used in worksheet formulas.</summary>
    WorksheetFunction,
    /// <summary>Handler for the host object's own events: Workbook_Open, Worksheet_Change, UserForm_Initialize, Class_Initialize, ...</summary>
    BuiltInEventHandler,
    /// <summary>Handler for an event of a WithEvents variable, e.g. m_svc_Progress.</summary>
    WithEventsHandler,
    /// <summary>Handler for a control, e.g. cmdOK_Click. For sheets, the control isn't part of the VBA project.</summary>
    ControlEventHandler,
    /// <summary>Implementation of an interface member, e.g. IOrderRepository_Save; called through the interface.</summary>
    InterfaceImplementation,
}

/// <summary>A module in the module-level graph. Its id is the module name.</summary>
public sealed record ModuleNode(string Id, ModuleModel Module, bool IsInterface);

/// <summary>
/// A node in the member-level graph. Every module also gets one node with <see cref="Member"/> = null and the
/// module's name as id, for references to the module itself (e.g. "Dim o As Order" or "Sheet1.Range").
/// </summary>
public sealed record MemberNode(string Id, ModuleModel Module, Member? Member, MemberRole Role, bool IsUnreferenced)
{
    public bool IsModuleNode => Member == null;
}

/// <param name="Count">How many references the edge stands for.</param>
public sealed record Edge(string From, string To, EdgeKind Kind, int Count);

/// <summary>A member access on an Object, Variant or untyped value, which can't be resolved without running the code.</summary>
/// <param name="Expression">e.g. "legacy.ExportAll" or "CreateObject(…).OpenTextFile".</param>
public sealed record LateBoundReference(string FromId, string Expression, int Line);

/// <summary>The project's dependencies at module and member level.</summary>
public sealed class DependencyModel
{
    public required ProjectModel Project { get; init; }

    /// <summary>In project order.</summary>
    public required IReadOnlyList<ModuleNode> Modules { get; init; }

    /// <summary>Per module in project order: the module node, then its members in source order.</summary>
    public required IReadOnlyList<MemberNode> Members { get; init; }

    /// <summary>Edges between different modules, sorted.</summary>
    public required IReadOnlyList<Edge> ModuleEdges { get; init; }

    /// <summary>Edges between member nodes, sorted. A recursive procedure has an edge to itself.</summary>
    public required IReadOnlyList<Edge> MemberEdges { get; init; }

    /// <summary>In project and source order.</summary>
    public required IReadOnlyList<LateBoundReference> LateBound { get; init; }
}
