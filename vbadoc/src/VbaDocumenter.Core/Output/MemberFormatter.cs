using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Output;

/// <summary>Formats members as short VBA-like text, for the console report and the page.</summary>
public static class MemberFormatter
{
  /// <summary>
  /// A one-line description of a member, e.g. "Public Function IsValid() As Boolean". A member declared again under
  /// #If with other types shows them all: "Private m_Dictionary As Scripting.Dictionary | Object".
  /// </summary>
  public static string Signature(Member m)
  {
    string visibility = m.Visibility.ToString();
    string asType = TypeText(m) is { } type ? $" As {type}{(m.IsArray ? "()" : "")}" : "";
    string parameters = Parameters(m);

    return m.Kind switch
    {
      MemberKind.Sub => $"{visibility} {(m.IsStatic ? "Static " : "")}Sub {m.Name}({parameters})",
      MemberKind.Function => $"{visibility} {(m.IsStatic ? "Static " : "")}Function {m.Name}({parameters}){asType}",
      MemberKind.PropertyGet => $"{visibility} Property Get {m.Name}({parameters}){asType}",
      MemberKind.PropertyLet => $"{visibility} Property Let {m.Name}({parameters})",
      MemberKind.PropertySet => $"{visibility} Property Set {m.Name}({parameters})",
      MemberKind.Variable => $"{visibility} {(m.IsWithEvents ? "WithEvents " : "")}{m.Name}{(m.IsArray ? "()" : "")}" +
                             $"{(TypeText(m) is { } variableType ? $" As {(m.IsAutoNew ? "New " : "")}{variableType}" : "")}",
      MemberKind.Constant => $"{visibility} Const {m.Name}{asType} = {m.Value}",
      MemberKind.Enum => $"{visibility} Enum {m.Name}",
      MemberKind.EnumMember => $"{m.ParentName}.{m.Name}{(m.Value == null ? "" : $" = {m.Value}")}",
      MemberKind.Type => $"{visibility} Type {m.Name} ({m.Fields.Count} fields)",
      MemberKind.Event => $"Public Event {m.Name}({parameters})",
      MemberKind.Declare => $"{visibility} Declare {(m.IsFunctionDeclare ? "Function" : "Sub")} {m.Name} Lib \"{m.Library}\"({parameters}){asType}",
      _ => m.Name,
    };
  }

  /// <summary>
  /// A member's types as written, joined with " | " when its versions under #If differ ("Scripting.Dictionary |
  /// Object"; a version without a type is Variant), or null when it has none.
  /// </summary>
  public static string? TypeText(Member m) =>
      m.AlternativeTypes.Count == 0 ? m.Type : string.Join(" | ", m.Types.Select(t => t ?? "Variant"));

  /// <summary>
  /// A member's parameter list without the brackets, e.g. "Optional n As Long = 5, rows() As String". When its versions
  /// under #If have the same parameters with different types, each shows them all ("d As Scripting.Dictionary | Object").
  /// </summary>
  public static string Parameters(Member m)
  {
    var lists = m.AlternativeParameters;
    bool lineUp = lists.Count > 0 && lists.All(list => list.Count == m.Parameters.Count
        && list.Zip(m.Parameters).All(pair => pair.First.Name.Equals(pair.Second.Name, StringComparison.OrdinalIgnoreCase)));
    if (!lineUp) return Parameters(m.Parameters);
    return Parameters(m.Parameters.Select(p =>
    {
      var types = m.ParameterTypes(p.Name);
      return types.Count > 1 ? p with { Type = string.Join(" | ", types.Select(t => t ?? "Variant")) } : p;
    }));
  }

  public static string Parameters(IEnumerable<Parameter> parameters) => string.Join(", ", parameters.Select(p =>
      $"{(p.IsOptional ? "Optional " : "")}{(p.IsParamArray ? "ParamArray " : "")}{p.Name}{(p.IsArray ? "()" : "")}" +
      $"{(p.Type == null ? "" : $" As {p.Type}")}{(p.DefaultValue == null ? "" : $" = {p.DefaultValue}")}"));
}
