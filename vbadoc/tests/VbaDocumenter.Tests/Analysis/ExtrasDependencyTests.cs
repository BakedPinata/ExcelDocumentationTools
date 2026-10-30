using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;
using static VbaDocumenter.Core.Analysis.EdgeKind;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>Dependency rules for declarations that only SampleVbaExtras has: constants, enums, Types, Declares.</summary>
public class ExtrasDependencyTests
{
  static readonly DependencyModel Model = DependencyAnalyser.Analyse(new ProjectModel
  {
    Name = "Extras",
    CodePage = 1252,
    Modules =
      [
          Fixtures.Parse("SampleVbaExtras", "Declarations.bas"),
            Fixtures.Parse("SampleVbaExtras", "OrderForm.frm"),
            Fixtures.Parse("SampleVbaExtras", "Settings.cls"),
        ],
  });

  static bool HasEdge(string from, string to) => Model.MemberEdges.Any(e => e.From == from && e.To == to && e.Kind == Uses);

  [Theory]
  [InlineData("Declarations.BuildLabel", "Declarations.OrderStatus", "parameter As OrderStatus")]
  [InlineData("Declarations.BuildLabel", "Declarations.OrderStatus.osNew", "default value of a parameter")]
  [InlineData("Declarations.BuildLabel", "Declarations.DEFAULT_SEP", "default value of a parameter")]
  [InlineData("Declarations.BuildLabel", "Declarations.APP_NAME", "constant in the body")]
  [InlineData("Declarations.FillRows", "Declarations.OrderRow", "parameter As OrderRow (a Type)")]
  [InlineData("Declarations.FillRows", "Declarations.MAX_ROWS", "constant in an If")]
  [InlineData("Declarations.FillRows", "Declarations.Sleep", "Declare Sub after a line number")]
  [InlineData("Declarations.FillRows", "Declarations.GetTickCount", "Declare Function as an argument")]
  [InlineData("Declarations.m_Rows", "Declarations.OrderRow", "module variable of a Type")]
  [InlineData("Declarations.CountCalls", "Declarations.g_Count", "variable with a type character")]
  [InlineData("Settings.Class_Initialize", "Settings.Reset", "Friend Sub")]
  [InlineData("Settings.Item[Let]", "Settings.m_Values", "As New Collection variable")]
  public void Uses_edges(string from, string to, string because)
  {
    Assert.True(HasEdge(from, to), $"Expected {from} -> {to} ({because})");
  }

  [Fact]
  public void Declares_repeated_under_If_get_distinct_ids()
  {
    var ids = Model.Members.Where(m => m.Member?.Name == "GetTickCount").Select(m => m.Id);

    Assert.Equal(["Declarations.GetTickCount", "Declarations.GetTickCount~2"], ids);
  }

  [Fact]
  public void Flags_unreferenced_members_but_not_entry_points_enum_values_or_repeated_declares()
  {
    // CountCalls is a macro and Total/BuildLabel may be worksheet functions. GetTickCount~2 is the #Else copy of
    // a Declare that is used. The Enum counts as used because its values are. Settings' properties are
    // genuinely unused in this fixture.
    Assert.Equal(
    [
        "Declarations.APP_VERSION", "Declarations.g_LastError", "Declarations.m_Rows", "Declarations.FillRows",
            "Settings.Target[Get]", "Settings.Target[Set]", "Settings.Item[Get]", "Settings.Item[Let]",
        ], Model.Members.Where(m => m.IsUnreferenced).Select(m => m.Id));
  }

  [Theory]
  [InlineData("OrderForm.cmdOK_Click", MemberRole.ControlEventHandler)]
  [InlineData("OrderForm.UserForm_Initialize", MemberRole.BuiltInEventHandler)]
  [InlineData("Declarations.CountCalls", MemberRole.Macro)]
  [InlineData("Declarations.FillRows", MemberRole.None)]
  public void Recognises_form_roles(string id, MemberRole role)
  {
    Assert.Equal(role, Model.Members.Single(m => m.Id == id).Role);
  }

  [Fact]
  public void Has_no_module_level_edges_between_unrelated_modules()
  {
    Assert.Empty(Model.ModuleEdges);
  }
}
