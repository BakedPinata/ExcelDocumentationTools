using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using static VbaDocumenter.Core.Analysis.EdgeKind;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>Resolution rules that SampleVba doesn't exercise, each on a small inline project.</summary>
public class ReferenceResolverTests
{
    const string Widget = """
        Attribute VB_Name = "Widget"
        Option Explicit
        Public Name As String
        Private m_Secret As Long
        Public Sub Spin()
        End Sub
        Public Property Get Child() As Widget
        End Property
        Public Property Get Target() As Object
        End Property
        Public Property Let Target(ByVal value As Object)
        End Property
        Public Property Set Target(ByVal value As Object)
        End Property
        """;

    static DependencyModel Analyze(string mainCode, params (string Name, VbaModuleKind Kind, string Code)[] others)
    {
        var modules = new List<ModuleModel> { ModuleParser.Parse("Main", VbaModuleKind.Standard, mainCode) };
        modules.Add(ModuleParser.Parse("Widget", VbaModuleKind.Class, Widget));
        modules.AddRange(others.Select(o => ModuleParser.Parse(o.Name, o.Kind, o.Code)));
        var project = new ProjectModel { Name = "Test", CodePage = 1252, Modules = modules };
        Assert.All(project.Modules, m => Assert.Empty(m.Warnings));
        return DependencyAnalyzer.Analyze(project);
    }

    static string[] Targets(DependencyModel model, string from) =>
        model.MemberEdges.Where(e => e.From == from && e.Kind == Uses).Select(e => $"{e.To} x{e.Count}").ToArray();

    [Fact]
    public void Follows_nested_With_blocks_and_With_New()
    {
        var model = Analyze("""
            Sub Test()
                With New Widget
                    .Spin
                    With .Child
                        .Spin
                    End With
                    .Spin
                End With
            End Sub
            """);

        Assert.Equal(["Widget x1", "Widget.Child[Get] x1", "Widget.Spin x3"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Follows_chains_through_function_and_property_results()
    {
        var model = Analyze("""
            Function MakeWidget() As Widget
            End Function
            Sub Test()
                MakeWidget().Spin
                MakeWidget.Child.Child.Spin
            End Sub
            """);

        Assert.Equal(["Main.MakeWidget x2", "Widget.Child[Get] x2", "Widget.Spin x2"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Follows_fields_of_user_defined_types()
    {
        var model = Analyze("""
            Private Type Holder
                Item As Widget
            End Type
            Sub Test()
                Dim h As Holder
                h.Item.Spin
            End Sub
            """);

        Assert.Equal(["Main.Holder x1", "Widget.Spin x1"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Resolves_enum_values_qualified_or_not()
    {
        var model = Analyze("""
            Public Enum Colour
                Red
                Green
            End Enum
            Sub Test()
                Debug.Print Colour.Red, Green
            End Sub
            """);

        Assert.Equal(["Main.Colour.Green x1", "Main.Colour.Red x1"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Chooses_Property_Get_Let_or_Set_from_how_the_property_is_used()
    {
        var model = Analyze("""
            Sub Test(ByVal w As Widget, ByVal x As Object)
                Set w.Target = x
                w.Target = x
                If w.Target Is Nothing Then Exit Sub
            End Sub
            """);

        Assert.Equal(["Widget x1", "Widget.Target[Get] x1", "Widget.Target[Let] x1", "Widget.Target[Set] x1"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Private_members_of_other_modules_count_as_using_the_module()
    {
        var model = Analyze("""
            Sub Test(ByVal w As Widget)
                w.m_Secret = 1
            End Sub
            """);

        Assert.Equal(["Widget x2"], Targets(model, "Main.Test")); // parameter type + the inaccessible member
    }

    [Fact]
    public void Default_instances_of_forms_and_classes_resolve_by_module_name()
    {
        var model = Analyze("""
            Sub Test()
                Widget.Spin
                OrderForm.Show
            End Sub
            """, ("OrderForm", VbaModuleKind.Form, "Attribute VB_Name = \"OrderForm\"\r\n"));

        Assert.Equal(["OrderForm x1", "Widget.Spin x1"], Targets(model, "Main.Test"));
    }

    [Fact]
    public void Locals_and_parameters_hide_module_members_with_the_same_name()
    {
        var model = Analyze("""
            Public Total As Long
            Sub Test(ByVal Name As String)
                Dim Total As Long
                Total = Len(Name)
            End Sub
            """);

        Assert.Empty(Targets(model, "Main.Test"));
    }

    [Fact]
    public void Ignores_names_inside_strings()
    {
        var model = Analyze("""
            Sub Test()
                MsgBox "Widget.Spin and Test"
            End Sub
            """);

        Assert.Empty(Targets(model, "Main.Test"));
    }

    [Fact]
    public void Reports_late_bound_access_on_untyped_Variant_Object_and_With_blocks()
    {
        var model = Analyze("""
            Sub Test(ByVal w As Widget)
                Dim v, o As Object, r As Range
                v.Foo
                o.Bar.Baz
                w.Target.Qux
                With o
                    .Quux
                End With
                r.Value = 1
            End Sub
            """);

        Assert.Equal([".Quux", "o.Bar", "v.Foo", "w.Target.Qux"], model.LateBound.Select(l => l.Expression).Order());
        Assert.Contains("Widget.Target[Get] x1", Targets(model, "Main.Test")); // resolved up to the Object it returns
    }

    [Fact]
    public void Type_declaration_characters_do_not_hide_names()
    {
        var model = Analyze("""
            Function Label$()
            End Function
            Sub Test()
                Dim s$
                s$ = Label$() & Label
            End Sub
            """);

        Assert.Equal(["Main.Label x2"], Targets(model, "Main.Test"));
    }
}
