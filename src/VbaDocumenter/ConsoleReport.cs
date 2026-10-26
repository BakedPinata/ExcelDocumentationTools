using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter;

/// <summary>Formats the parsed project for the console.</summary>
static class ConsoleReport
{
    public static void WriteModules(ProjectModel project, bool verbose)
    {
        int nameWidth = project.Modules.Count == 0 ? 0 : project.Modules.Max(m => m.Name.Length);
        foreach (var module in project.Modules)
        {
            Console.WriteLine($"    {module.Kind,-9} {module.Name.PadRight(nameWidth)}  {Counts(module)}");
            if (!verbose) continue;
            foreach (var member in module.Members)
                Console.WriteLine($"      {member.StartLine,5}-{member.EndLine,-5} {Describe(member)}");
        }

        var warnings = project.Modules.SelectMany(m => m.Warnings.Select(w => $"{m.Name} {w}")).ToList();
        if (warnings.Count == 0) return;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {warnings.Count} parser warning(s). These lines were skipped:");
        foreach (var warning in warnings) Console.WriteLine($"    {warning}");
        Console.ResetColor();
    }

    /// <summary>e.g. "7 procedures, 3 variables, 1 event".</summary>
    static string Counts(ModuleModel module)
    {
        var groups = new (string singular, string plural, Func<Member, bool> match)[]
        {
            ("procedure", "procedures", m => m.IsProcedure),
            ("variable", "variables", m => m.Kind == MemberKind.Variable),
            ("constant", "constants", m => m.Kind == MemberKind.Constant),
            ("enum", "enums", m => m.Kind == MemberKind.Enum),
            ("type", "types", m => m.Kind == MemberKind.Type),
            ("event", "events", m => m.Kind == MemberKind.Event),
            ("declare", "declares", m => m.Kind == MemberKind.Declare),
        };
        var parts = groups
            .Select(g => (g, count: module.Members.Count(g.match)))
            .Where(x => x.count > 0)
            .Select(x => $"{x.count} {(x.count == 1 ? x.g.singular : x.g.plural)}")
            .ToList();
        if (module.Implements.Count > 0) parts.Add($"implements {string.Join(", ", module.Implements)}");
        return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
    }

    /// <summary>A one-line, VBA-like description of a member, e.g. "Public Function IsValid() As Boolean".</summary>
    static string Describe(Member m)
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
            MemberKind.EnumMember => $"  {m.Name}{(m.Value == null ? "" : $" = {m.Value}")}",
            MemberKind.Type => $"{visibility} Type {m.Name} ({m.Fields.Count} fields)",
            MemberKind.Event => $"Public Event {m.Name}({parameters})",
            MemberKind.Declare => $"{visibility} Declare {(m.IsFunctionDeclare ? "Function" : "Sub")} {m.Name} Lib \"{m.Library}\"({parameters}){asType}",
            _ => m.Name,
        };
    }
}
