using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Output;

/// <summary>The class diagram's box contents and relationships, on SampleVba and SampleVbaExtras.</summary>
public class ClassDiagramTests
{
  static readonly DependencyModel Sample =
      DependencyAnalyzer.Analyze(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()))));

  /// <summary>Each shown member's line as Mermaid would display it, in declaration order.</summary>
  static string[] Lines(ModuleModel module) =>
      ClassDiagram.Lines(module).OrderBy(p => p.Key.DeclarationLine).Select(p =>
          p.Value.Text + (p.Value.Parameters == null ? "" : $"({p.Value.Parameters})" + (p.Value.Type == null ? "" : " " + p.Value.Type)))
      .ToArray();

  static ModuleModel SampleModule(string name) => Sample.Modules.Single(m => m.Id == name).Module;

  [Fact]
  public void A_class_shows_variables_properties_once_and_procedures_with_visibility_and_types()
  {
    Assert.Equal(
    [
        "-m_Id : String",
            "-m_Status : String",
            "-m_Amount : Double",
            "-Class_Initialize()",
            "+Property Id : String «Get»",
            "+Property Status : String «Get/Let»",  // Get and Let shown once
            "+Load(row As Range)",
            "+Parse(line As String)",
            "+IsValid() Boolean",
            "+Describe() String",
        ], Lines(SampleModule("Order")));
  }

  [Fact]
  public void Events_and_WithEvents_variables_are_marked()
  {
    Assert.Contains("+Event Progress(message As String)", Lines(SampleModule("OrderService")));
    Assert.Contains("-WithEvents m_svc : OrderService", Lines(SampleModule("ProgressReporter")));
  }

  [Fact]
  public void Constants_enums_types_and_declares_are_distinguishable_and_enum_values_are_left_out()
  {
    var lines = Lines(Fixtures.Parse("SampleVbaExtras", "Declarations.bas"));

    Assert.Contains("+Const APP_NAME : String = \"Order Tool\"", lines);
    Assert.Contains("-Const MAX_ROWS : Long = 10000", lines); // type-declaration character mapped
    Assert.Contains("+Enum OrderStatus", lines);
    Assert.DoesNotContain(lines, l => l.StartsWith("+osNew"));
    Assert.Equal(1, lines.Count(l => l.StartsWith("-Declare Function GetTickCount"))); // declared under #If and #Else: once per declaration line
    Assert.Contains("-Type OrderRow", lines);
    Assert.Contains("-Declare Function GetTickCount() Long", lines);
    Assert.Contains("+Declare Sub Sleep(dwMilliseconds As Long)", lines);
    Assert.Contains("+Total(ParamArray amounts() As Variant) Double", lines);
    Assert.Contains("-m_Rows() : OrderRow", lines);
  }

  [Fact]
  public void Object_properties_take_their_type_from_Get_and_indexed_properties_are_drawn_as_methods()
  {
    var lines = Lines(Fixtures.Parse("SampleVbaExtras", "Settings.cls"));

    Assert.Contains("+Property Target : Object «Get/Set»", lines);
    Assert.Contains("+Property Item(key As String) Variant «Get/Let»", lines);
    Assert.Contains("~Reset()", lines); // Friend
    Assert.Contains("-m_Values : New Collection", lines);
  }

  [Fact]
  public void Long_constant_values_are_shortened()
  {
    var module = new ModuleModel
    {
      Name = "M",
      Kind = VbaModuleKind.Standard,
      Implements = [],
      Attributes = new Dictionary<string, string>(),
      Options = [],
      LineCount = 1,
      Warnings = [],
      Members =
        [
            new Member
                {
                    Name = "LONG_TEXT", Kind = MemberKind.Constant, Visibility = Visibility.Public, Type = "String",
                    Value = "\"" + new string('x', 60) + "\"", DeclarationLine = 1, StartLine = 1,
                },
            ],
    };

    var text = ClassDiagram.Lines(module).Single().Value.Text;

    Assert.EndsWith("…", text);
    Assert.Equal("+Const LONG_TEXT : String = ".Length + 40, text.Length);
  }

  [Fact]
  public void Relationships_are_realisations_associations_and_dependencies()
  {
    var edges = ClassDiagram.Edges(Sample).Select(e => (e.From, e.To, e.Kind, e.Label)).ToList();

    Assert.Contains(("CsvOrderRepository", "IOrderRepository", "realization", null), edges);
    Assert.Contains(("SheetOrderRepository", "IOrderRepository", "realization", null), edges);
    Assert.Contains(("OrderService", "IOrderRepository", "association", "m_repo"), edges);
    Assert.Contains(("ProgressReporter", "OrderService", "association", "WithEvents m_svc"), edges);
    Assert.Contains(("Main", "Order", "dependency", null), edges);
    Assert.Contains(("Sheet1", "Main", "dependency", null), edges);

    // An association replaces the dependency between the same two modules, including the WithEvents events edge.
    Assert.DoesNotContain(("OrderService", "IOrderRepository", "dependency", null), edges);
    Assert.DoesNotContain(("ProgressReporter", "OrderService", "dependency", null), edges);
    Assert.Equal(2, edges.Count(e => e.Kind == "realization"));
    Assert.Equal(2, edges.Count(e => e.Kind == "association"));
    Assert.Equal(24, edges.Count(e => e.Kind == "dependency")); // 26 Uses edges, less the 2 the associations cover
  }

  [Fact]
  public void Variables_typed_as_standard_modules_or_non_project_types_are_not_associations()
  {
    // Settings has "As Object" and "As New Collection"; Declarations has an array of a Type.
    var model = DependencyAnalyzer.Analyze(new ProjectModel
    {
      Name = "Extras",
      CodePage = 1252,
      Modules = [Fixtures.Parse("SampleVbaExtras", "Declarations.bas"), Fixtures.Parse("SampleVbaExtras", "Settings.cls")],
    });

    Assert.DoesNotContain(ClassDiagram.Edges(model), e => e.Kind == "association");
  }
}
