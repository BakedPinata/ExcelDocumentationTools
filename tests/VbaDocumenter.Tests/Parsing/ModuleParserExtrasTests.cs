using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>Parser tests against the SampleVbaExtras fixture, which covers the declarations SampleVba lacks.</summary>
public class ModuleParserExtrasTests
{
    static readonly ModuleModel Declarations = Fixtures.Parse("SampleVbaExtras", "Declarations.bas");
    static readonly ModuleModel Settings = Fixtures.Parse("SampleVbaExtras", "Settings.cls");
    static readonly ModuleModel OrderForm = Fixtures.Parse("SampleVbaExtras", "OrderForm.frm");

    static Member Find(ModuleModel module, string name, MemberKind kind) =>
        module.Members.Single(m => m.Name == name && m.Kind == kind);

    [Fact]
    public void Parses_all_extras_without_warnings()
    {
        Assert.Empty(Declarations.Warnings);
        Assert.Empty(Settings.Warnings);
        Assert.Empty(OrderForm.Warnings);
    }

    [Fact]
    public void Reads_several_constants_on_one_line_with_types_and_values()
    {
        Assert.Equal(
        [
            ("APP_NAME", Visibility.Public, "String", "\"Order Tool\""),
            ("APP_VERSION", Visibility.Public, null, "2"),
            ("MAX_ROWS", Visibility.Private, "Long", "10000"),        // type from the & character
            ("DEFAULT_SEP", Visibility.Private, null, "\",\""),        // module-level Const is Private by default
        ], Declarations.Members.Where(m => m.Kind == MemberKind.Constant).Select(m => (m.Name, m.Visibility, m.Type, m.Value)));
    }

    [Fact]
    public void A_comment_directly_above_a_member_is_part_of_its_source()
    {
        Assert.Equal(5, Find(Declarations, "APP_NAME", MemberKind.Constant).StartLine);
        Assert.Equal(6, Find(Declarations, "APP_NAME", MemberKind.Constant).DeclarationLine);
        Assert.Equal(15, Find(Settings, "Target", MemberKind.PropertyGet).StartLine);
        // A blank line in between breaks the link: the Enum has no comment above it.
        Assert.Equal(10, Find(Declarations, "OrderStatus", MemberKind.Enum).StartLine);
    }

    [Fact]
    public void Rem_comments_count_as_a_comment_block()
    {
        var buildLabel = Find(Declarations, "BuildLabel", MemberKind.Function);

        Assert.Equal((34, 36, 40), (buildLabel.StartLine, buildLabel.DeclarationLine, buildLabel.EndLine));
    }

    [Fact]
    public void Reads_an_enum_and_each_value()
    {
        var status = Find(Declarations, "OrderStatus", MemberKind.Enum);
        var values = Declarations.Members.Where(m => m.Kind == MemberKind.EnumMember).ToList();

        Assert.Equal((10, 16, Visibility.Public), (status.StartLine, status.EndLine, status.Visibility));
        Assert.Equal(["osNew", "osHold", "osDone", "_Last"], values.Select(v => v.Name));
        Assert.All(values, v => Assert.Equal("OrderStatus", v.ParentName));
        Assert.Equal(["0", null, "2", null], values.Select(v => v.Value));
        Assert.Equal((12, 13), (values[1].StartLine, values[1].EndLine)); // osHold has a comment above it
        // The Enum comes before its values, in source order.
        Assert.Equal(Declarations.Members.ToList().IndexOf(status) + 1, Declarations.Members.ToList().IndexOf(values[0]));
    }

    [Fact]
    public void Reads_a_Type_and_its_fields()
    {
        var row = Find(Declarations, "OrderRow", MemberKind.Type);

        Assert.Equal((Visibility.Private, 18, 22), (row.Visibility, row.StartLine, row.EndLine));
        Assert.Equal(
            [new TypeField("Id", "String * 10", false), new TypeField("Amount", "Currency", false), new TypeField("Tags", "String", true)],
            row.Fields);
    }

    [Fact]
    public void Reads_declares_in_both_branches_of_an_If_directive()
    {
        var declares = Declarations.Members.Where(m => m.Kind == MemberKind.Declare).ToList();

        Assert.Equal([("GetTickCount", 25), ("GetTickCount", 27), ("Sleep", 29)], declares.Select(d => (d.Name, d.DeclarationLine)));
        Assert.True(declares[0].IsFunctionDeclare);
        Assert.Equal(("kernel32", (string?)null, "Long"), (declares[0].Library, declares[0].Alias, declares[0].Type));

        var sleep = declares[2];
        Assert.False(sleep.IsFunctionDeclare);
        Assert.Equal(("Sleep", Visibility.Public), (sleep.Alias, sleep.Visibility));
        Assert.Equal(new Parameter("dwMilliseconds", "Long", false, true, false, false, null), sleep.Parameters.Single());
    }

    [Fact]
    public void Maps_type_declaration_characters_to_types()
    {
        Assert.Equal("String", Find(Declarations, "g_LastError", MemberKind.Variable).Type);
        Assert.Equal("Integer", Find(Declarations, "g_Count", MemberKind.Variable).Type);
        Assert.Equal("String", Find(Declarations, "BuildLabel", MemberKind.Function).Type);
        Assert.Equal("Long", Find(Declarations, "FillRows", MemberKind.Sub).Parameters[1].Type);
    }

    [Fact]
    public void Reads_a_signature_split_over_continuation_lines_with_optional_defaults()
    {
        var parameters = Find(Declarations, "BuildLabel", MemberKind.Function).Parameters;

        Assert.Equal(
        [
            new Parameter("id", "String", false, true, false, false, null),
            new Parameter("status", "OrderStatus", true, true, false, false, "osNew"),
            new Parameter("sep", "String", true, true, false, false, "DEFAULT_SEP"),
        ], parameters);
    }

    [Fact]
    public void Reads_ParamArray_array_parameters_and_array_variables()
    {
        Assert.Equal(new Parameter("amounts", "Variant", false, false, true, true, null),
            Find(Declarations, "Total", MemberKind.Function).Parameters.Single());
        Assert.Equal(new Parameter("rows", "OrderRow", false, false, false, true, null),
            Find(Declarations, "FillRows", MemberKind.Sub).Parameters[0]);

        var rowsVariable = Find(Declarations, "m_Rows", MemberKind.Variable);
        Assert.Equal(("OrderRow", true), (rowsVariable.Type, rowsVariable.IsArray));
    }

    [Fact]
    public void Reads_Static_procedures()
    {
        Assert.True(Find(Declarations, "CountCalls", MemberKind.Sub).IsStatic);
    }

    [Fact]
    public void Body_handles_time_literals_strings_with_colons_labels_and_line_numbers()
    {
        var fillRows = Find(Declarations, "FillRows", MemberKind.Sub);

        Assert.Equal(
        [
            "ReDim rows(1 To count) As OrderRow",
            "Dim started As Date",
            "started = #12:30:00 PM#",
            "rows(1).Id = \"A:1\"",
            "rows(1).Amount = 0",
            "If count > MAX_ROWS Then GoTo TooMany",
            "Exit Sub",
            "Sleep GetTickCount Mod 10",
        ], fillRows.Body.Select(s => s.Text));
        // ReDim of a parameter doesn't declare a new local; Dim does.
        Assert.Equal([("started", "Date")], fillRows.Locals.Select(l => (l.Name, l.Type)));
    }

    [Fact]
    public void Reads_property_Get_Set_and_Let_as_separate_members_and_skips_member_attributes()
    {
        Assert.Equal(
        [
            ("m_Target", MemberKind.Variable), ("m_Values", MemberKind.Variable),
            ("Target", MemberKind.PropertyGet), ("Target", MemberKind.PropertySet),
            ("Item", MemberKind.PropertyGet), ("Item", MemberKind.PropertyLet),
            ("Reset", MemberKind.Sub), ("Class_Initialize", MemberKind.Sub),
        ], Settings.Members.Select(m => (m.Name, m.Kind)));

        Assert.Equal(["Set Target = m_Target"], Find(Settings, "Target", MemberKind.PropertyGet).Body.Select(s => s.Text));
        Assert.Equal(2, Find(Settings, "Item", MemberKind.PropertyLet).Parameters.Count);
    }

    [Fact]
    public void Reads_Friend_procedures_and_As_New_variables()
    {
        Assert.Equal(Visibility.Friend, Find(Settings, "Reset", MemberKind.Sub).Visibility);
        Assert.True(Find(Settings, "m_Values", MemberKind.Variable).IsAutoNew);
    }

    [Fact]
    public void Skips_the_class_header_but_counts_its_lines()
    {
        Assert.True(Settings.HasPredeclaredId);
        Assert.Equal((39, 41), (Find(Settings, "Class_Initialize", MemberKind.Sub).StartLine, Find(Settings, "Class_Initialize", MemberKind.Sub).EndLine));
    }

    [Fact]
    public void Skips_a_form_designer_block()
    {
        Assert.Equal(["cmdOK_Click", "UserForm_Initialize"], OrderForm.Members.Select(m => m.Name));
        Assert.Equal(18, OrderForm.Members[0].DeclarationLine);
        Assert.Equal("OrderForm", OrderForm.Attributes["VB_Name"]);
    }
}
