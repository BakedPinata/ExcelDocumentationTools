using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Output;

// The data the page's JavaScript works from. These records exist only to give the JSON a clear, stable shape;
// property names become camelCase in the JSON.

public sealed record PageProject(string Title, string Name, int CodePage, string Encoding, string SourceMode);

/// <param name="Hash">First 16 hex digits of the SHA-256 of the exported file, so the page can tell whether a
/// loaded folder still matches.</param>
public sealed record PageModule(string Id, string Kind, string File, bool IsInterface, int Lines, string Hash);

/// <param name="Class">The member's line in the class diagram, or null if it isn't shown there.</param>
public sealed record PageMember(
    string Id, string Module, string Name, string Kind, string Visibility, string Signature,
    int Start, int End, int Declaration, string Role, bool Unreferenced, string? Parent, PageClassLine? Class);

public sealed record PageEdge(string From, string To, string Kind, int Count);

public sealed record PageLateBound(string From, string Expression, int Line);

public sealed record PageData(
    PageProject Project,
    IReadOnlyList<PageModule> Modules,
    IReadOnlyList<PageMember> Members,
    IReadOnlyList<PageEdge> ModuleEdges,
    IReadOnlyList<PageEdge> MemberEdges,
    IReadOnlyList<PageClassEdge> ClassEdges,
    IReadOnlyList<PageLateBound> LateBound)
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

    var modules = model.Modules.Select(node =>
    {
      var content = ProjectExporter.FileContent(filesByName[node.Id]);
      return new PageModule(node.Id, Camel(node.Module.Kind), ProjectExporter.FileNameFor(filesByName[node.Id]),
              node.IsInterface, node.Module.LineCount, Convert.ToHexStringLower(SHA256.HashData(content))[..16]);
    }).ToList();

    var classLines = model.Modules.ToDictionary(m => m.Id, m => ClassDiagram.Lines(m.Module));
    var members = model.Members.Where(n => n.Member != null).Select(n => new PageMember(
        n.Id, n.Module.Name, n.Member!.Name, Camel(n.Member.Kind), Camel(n.Member.Visibility),
        MemberFormatter.Signature(n.Member), n.Member.StartLine, n.Member.EndLine, n.Member.DeclarationLine,
        Camel(n.Role), n.IsUnreferenced, n.Member.ParentName,
        classLines[n.Module.Name].GetValueOrDefault(n.Member))).ToList();

    PageEdge Edge(Edge e) => new(e.From, e.To, Camel(e.Kind), e.Count);

    return new PageData(
        new PageProject(title, project.Name, project.CodePage, CodePages.Get(project.CodePage).WebName,
            embedSource ? "embedded" : "folder"),
        modules,
        members,
        model.ModuleEdges.Select(Edge).ToList(),
        model.MemberEdges.Select(Edge).ToList(),
        ClassDiagram.Edges(model),
        model.LateBound.Select(l => new PageLateBound(l.FromId, l.Expression, l.Line)).ToList());
  }

  public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
