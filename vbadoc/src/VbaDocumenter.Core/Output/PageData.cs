using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeDocumenter.Shared.Importing;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;

namespace VbaDocumenter.Core.Output;

// The data the page's JavaScript works from. These records exist only to give the JSON a clear, stable shape;
// property names become camelCase in the JSON.

/// <param name="References">The project's library references, apart from the two every project has, for the Export tab.</param>
public sealed record PageProject(string Title, string Name, int CodePage, string Encoding, string SourceMode, IReadOnlyList<PageReference> References);

/// <summary>A library reference (see <see cref="VbaReference"/>).</summary>
public sealed record PageReference(string Name, string Description);

/// <summary>What a module relies on that doesn't come with it (see <see cref="ImportNeed"/>).</summary>
public sealed record PageNeed(string What, string Why);

/// <param name="Hash">First 16 hex digits of the SHA-256 of the exported file, so the page can tell whether a
/// loaded folder still matches.</param>
/// <param name="Folder">The module's <c>'@Folder</c>, such as <c>Import.Parsing</c>, or null.</param>
/// <param name="UsedBy">How many other modules use it, or null for none (see <see cref="Usage"/>).</param>
/// <param name="Predeclared">True for a class with <c>VB_PredeclaredId = True</c>, used by its name like a standard module;
/// null otherwise.</param>
/// <param name="WhyNot">Why the Export tab can't pick it, or null when it can (see <see cref="ModuleImport"/>).</param>
/// <param name="Needs">What it relies on that doesn't come with it, or null for nothing.</param>
public sealed record PageModule(
    string Id, string Kind, string File, bool IsInterface, int Lines, string Hash, string? Folder, int? UsedBy, bool? Predeclared,
    string? WhyNot, IReadOnlyList<PageNeed>? Needs);

/// <param name="Class">The member's line in the class diagram, or null if it isn't shown there.</param>
/// <param name="UsedBy">How many other modules use it, or null for none (see <see cref="Usage"/>).</param>
public sealed record PageMember(
    string Id, string Module, string Name, string Kind, string Visibility, string Signature,
    int Start, int End, int Declaration, string Role, bool Unreferenced, string? Parent, PageClassLine? Class, int? UsedBy);

/// <param name="Lines">For a member edge, the lines of the module's file where the references are (see
/// <see cref="Edge.Lines"/>); null for module edges.</param>
public sealed record PageEdge(string From, string To, string Kind, int Count, IReadOnlyList<int>? Lines);

public sealed record PageLateBound(string From, string Expression, int Line);

/// <summary>A call, for the Calls tab (see <see cref="Call"/>): what's called, or the late-bound expression, the line,
/// the innermost block it's in, or null, and for a call made by a block's own statement, that block.</summary>
public sealed record PageCall(string From, string? To, string? LateBound, int Line, int? Block, int? Condition);

/// <summary>A procedure's block statement, for the Calls tab (see <see cref="CodeBlock"/>).</summary>
public sealed record PageBlock(string From, int Id, string Kind, string Text, int Line, int? Parent);

public sealed record PageData(
    PageProject Project,
    IReadOnlyList<PageModule> Modules,
    IReadOnlyList<PageMember> Members,
    IReadOnlyList<PageEdge> ModuleEdges,
    IReadOnlyList<PageEdge> MemberEdges,
    IReadOnlyList<PageClassEdge> ClassEdges,
    IReadOnlyList<PageLateBound> LateBound,
    IReadOnlyList<PageCall> Calls,
    IReadOnlyList<PageBlock> Blocks)
{
  static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    // One property per line keeps git diffs of the page readable.
    WriteIndented = true,
    // The default encoder escapes <, > and &, so the JSON can't end the <script> element it sits in.
    Encoder = JavaScriptEncoder.Default,
  };

  public static PageData From(DependencyModel model, VbaProject project, string title, bool embedSource)
  {
    var filesByName = project.Modules.ToDictionary(m => m.Name, StringComparer.Ordinal);
    string Camel(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    var usedBy = Usage.UsedByOtherModules(model);
    int? UsedBy(string id) => usedBy.TryGetValue(id, out var count) ? count : null;
    var import = new ModuleImport(model);

    var modules = model.Modules.Select(node =>
    {
      var content = ProjectExporter.FileContent(filesByName[node.Id]);
      var needs = import.Needs(node.Id).Select(n => new PageNeed(n.What, n.Why)).ToList();
      return new PageModule(node.Id, Camel(node.Module.Kind), ProjectExporter.FileNameFor(filesByName[node.Id]),
              node.IsInterface, node.Module.LineCount, Convert.ToHexStringLower(SHA256.HashData(content))[..16],
              string.IsNullOrEmpty(node.Module.Folder) ? null : node.Module.Folder, UsedBy(node.Id),
              node.Module.Kind == VbaModuleKind.Class && !node.IsInterface && node.Module.HasPredeclaredId ? true : null,
              import.WhyNot(node.Id), needs.Count > 0 ? needs : null);
    }).ToList();

    var classLines = model.Modules.ToDictionary(m => m.Id, m => ClassDiagram.Lines(m.Module));
    var members = model.Members.Where(n => n.Member != null).Select(n => new PageMember(
        n.Id, n.Module.Name, n.Member!.Name, Camel(n.Member.Kind), Camel(n.Member.Visibility),
        MemberFormatter.Signature(n.Member), n.Member.StartLine, n.Member.EndLine, n.Member.DeclarationLine,
        Camel(n.Role), n.IsUnreferenced, n.Member.ParentName,
        classLines[n.Module.Name].GetValueOrDefault(n.Member), UsedBy(n.Id))).ToList();

    PageEdge Edge(Edge e) => new(e.From, e.To, Camel(e.Kind), e.Count, e.Lines.Count == 0 ? null : e.Lines);
    var callers = model.Calls.Select(c => c.From).ToHashSet(StringComparer.Ordinal);

    return new PageData(
        new PageProject(title, project.Name, project.CodePage, CodePages.Get(project.CodePage).WebName,
            embedSource ? "embedded" : "folder",
            project.References.Where(r => !r.IsStandard).Select(r => new PageReference(r.Name, r.Description)).ToList()),
        modules,
        members,
        model.ModuleEdges.Select(Edge).ToList(),
        model.MemberEdges.Select(Edge).ToList(),
        ClassDiagram.Edges(model),
        model.LateBound.Select(l => new PageLateBound(l.FromId, l.Expression, l.Line)).ToList(),
        model.Calls.Select(c => new PageCall(c.From, c.To, c.LateBound, c.Line, c.Block, c.Condition)).ToList(),
        // Only blocks with a call in them are drawn, so only procedures with calls need theirs.
        model.Members.Where(n => n.Member is { Blocks.Count: > 0 } && callers.Contains(n.Id))
            .SelectMany(n => n.Member!.Blocks.Select(b => new PageBlock(n.Id, b.Id, b.Kind, b.Text, b.Line, b.Parent))).ToList());
  }

  public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
