using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Output;

/// <summary>Formats members as short VBA-like text, for the console report and the page.</summary>
public static class MemberFormatter
{
    /// <summary>A one-line description of a member, e.g. "Public Function IsValid() As Boolean".</summary>
    public static string Signature(Member m)
    {
        string visibility = m.Visibility.ToString();
        string asType = m.Type == null ? "" : $" As {m.Type}{(m.IsArray ? "()" : "")}";
        string parameters = string.Join(", ", m.Parameters.Select(p =>
            $"{(p.IsOptional ? "Optional " : "")}{(p.IsParamArray ? "ParamArray " : "")}{p.Name}{(p.IsArray ? "()" : "")}" +
            $"{(p.Type == null ? "" : $" As {p.Type}")}{(p.DefaultValue == null ? "" : $" = {p.DefaultValue}")}"));

        return m.Kind switch
        {
            MemberKind.Sub => $"{visibility} {(m.IsStatic ? "Static " : "")}Sub {m.Name}({parameters})",
            MemberKind.Function => $"{visibility} {(m.IsStatic ? "Static " : "")}Function {m.Name}({parameters}){asType}",
            MemberKind.PropertyGet => $"{visibility} Property Get {m.Name}({parameters}){asType}",
            MemberKind.PropertyLet => $"{visibility} Property Let {m.Name}({parameters})",
            MemberKind.PropertySet => $"{visibility} Property Set {m.Name}({parameters})",
            MemberKind.Variable => $"{visibility} {(m.IsWithEvents ? "WithEvents " : "")}{m.Name}{(m.IsArray ? "()" : "")}" +
                                   $"{(m.Type == null ? "" : $" As {(m.IsAutoNew ? "New " : "")}{m.Type}")}",
            MemberKind.Constant => $"{visibility} Const {m.Name}{asType} = {m.Value}",
            MemberKind.Enum => $"{visibility} Enum {m.Name}",
            MemberKind.EnumMember => $"{m.ParentName}.{m.Name}{(m.Value == null ? "" : $" = {m.Value}")}",
            MemberKind.Type => $"{visibility} Type {m.Name} ({m.Fields.Count} fields)",
            MemberKind.Event => $"Public Event {m.Name}({parameters})",
            MemberKind.Declare => $"{visibility} Declare {(m.IsFunctionDeclare ? "Function" : "Sub")} {m.Name} Lib \"{m.Library}\"({parameters}){asType}",
            _ => m.Name,
        };
    }
}
