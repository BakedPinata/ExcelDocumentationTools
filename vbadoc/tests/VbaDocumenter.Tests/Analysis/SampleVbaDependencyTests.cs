using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;
using static VbaDocumenter.Core.Analysis.EdgeKind;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>The dependency model for SampleVba, checked against edges worked out by reading the source.</summary>
public class SampleVbaDependencyTests
{
  // Built the way the tool builds it: through a workbook, so line numbers include the class headers.
  static readonly DependencyModel Model =
      DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()))));

  static bool HasEdge(string from, string to, EdgeKind kind) => Model.MemberEdges.Any(e => e.From == from && e.To == to && e.Kind == kind);

  [Fact]
  public void Module_level_edges_are_exactly_the_expected_ones()
  {
    (string, string, EdgeKind)[] expected =
    [
        ("AppInit", "Logger", Uses),
            ("AppInit", "Sheet1", Uses),                       // Sheet1.Range(...)
            ("CsvOrderRepository", "IOrderRepository", Implements),
            ("CsvOrderRepository", "Logger", Uses),
            ("CsvOrderRepository", "Order", Uses),
            ("IOrderRepository", "Order", Uses),               // Save(ByVal o As Order)
            ("Main", "AppInit", Uses),                         // g_UseCsv
            ("Main", "CsvOrderRepository", Uses),
            ("Main", "IOrderRepository", Uses),
            ("Main", "Logger", Uses),
            ("Main", "Order", Uses),
            ("Main", "OrderService", Uses),
            ("Main", "ProgressReporter", Uses),
            ("Main", "SheetOrderRepository", Uses),
            ("Order", "Utils", Uses),
            ("OrderService", "IOrderRepository", Uses),
            ("OrderService", "Logger", Uses),
            ("OrderService", "Order", Uses),
            ("ProgressReporter", "Logger", Uses),
            ("ProgressReporter", "OrderService", Uses),        // Attach(ByVal svc As OrderService)
            ("ProgressReporter", "OrderService", Events),      // WithEvents m_svc, and the m_svc_Progress handler
            ("Sheet1", "Logger", Uses),
            ("Sheet1", "Main", Uses),
            ("SheetOrderRepository", "IOrderRepository", Implements),
            ("SheetOrderRepository", "Logger", Uses),
            ("SheetOrderRepository", "Order", Uses),
            ("SheetOrderRepository", "Sheet1", Uses),
            ("ThisWorkbook", "AppInit", Uses),
            ("Utils", "Logger", Uses),
        ];

    Assert.Equal(expected, Model.ModuleEdges.Select(e => (e.From, e.To, e.Kind)));
  }

  [Fact]
  public void IOrderRepository_is_the_only_interface()
  {
    Assert.Equal(["IOrderRepository"], Model.Modules.Where(m => m.IsInterface).Select(m => m.Id));
  }

  [Theory]
  [InlineData("Main.ProcessOrders", "IOrderRepository.LoadAll", "repo declared As IOrderRepository")]
  [InlineData("Main.ProcessOrders", "OrderService.Init", "svc.Init repo")]
  [InlineData("Main.ProcessOrders", "OrderService.Summarise", ".Summarise inside With svc")]
  [InlineData("Main.ProcessOrders", "Order.Status[Get]", "Select Case o.Status")]
  [InlineData("Main.ProcessOrders", "Order.Id[Get]", "o.Id inside a string concatenation")]
  [InlineData("Main.ProcessOrders", "AppInit.g_UseCsv", "public variable of a standard module")]
  [InlineData("Main.ProcessOrders", "Main.Factorial", "own module function")]
  [InlineData("Main.ProcessOrders", "OrderService", "New OrderService / Dim svc As OrderService")]
  [InlineData("Main.Factorial", "Main.Factorial", "recursion is a self-loop")]
  [InlineData("OrderService.Process", "Order.Status[Get]", "ElseIf o.Status = ... is a comparison")]
  [InlineData("OrderService.Process", "Order.Status[Let]", "o.Status = \"HOLD\" is an assignment")]
  [InlineData("OrderService.Process", "OrderService.m_repo", "module variable")]
  [InlineData("OrderService.Process", "IOrderRepository.Save", "m_repo declared As IOrderRepository")]
  [InlineData("OrderService.Process", "Order.Describe", "inside RaiseEvent arguments")]
  [InlineData("Order.Describe", "Utils.FormatMoney", "module-qualified call")]
  [InlineData("Order.Load", "Order.m_Amount", "second statement after ':'")]
  [InlineData("Logger.LogInfo", "Logger.WriteLine", "unqualified own-module call")]
  [InlineData("Sheet1.btnRun_Click", "Main.ProcessOrders", "unqualified global call")]
  [InlineData("Sheet1.Worksheet_Change", "Main.ProcessOrders", "module-qualified call")]
  [InlineData("ThisWorkbook.Workbook_Open", "AppInit.Startup", "module-qualified call")]
  [InlineData("AppInit.LoadSettings", "Sheet1", "Sheet1.Range is not a project member, so it uses the module")]
  [InlineData("SheetOrderRepository.IOrderRepository_LoadAll", "Order.Load", "o.Load Sheet1.Rows(r)")]
  [InlineData("CsvOrderRepository.IOrderRepository_LoadAll", "CsvOrderRepository.ReadFile", "own private function")]
  [InlineData("CsvOrderRepository.IOrderRepository_LoadAll", "Order.Parse", "o.Parse lines(i)")]
  public void Uses_edges(string from, string to, string because)
  {
    Assert.True(HasEdge(from, to, Uses), $"Expected {from} -> {to} ({because})");
  }

  [Fact]
  public void Implements_edges_link_implementations_to_interface_members()
  {
    Assert.True(HasEdge("CsvOrderRepository.IOrderRepository_LoadAll", "IOrderRepository.LoadAll", Implements));
    Assert.True(HasEdge("CsvOrderRepository.IOrderRepository_Save", "IOrderRepository.Save", Implements));
    Assert.True(HasEdge("SheetOrderRepository.IOrderRepository_LoadAll", "IOrderRepository.LoadAll", Implements));
    Assert.True(HasEdge("SheetOrderRepository.IOrderRepository_Save", "IOrderRepository.Save", Implements));
  }

  [Fact]
  public void Events_edges_cover_RaiseEvent_WithEvents_and_handlers()
  {
    Assert.True(HasEdge("OrderService.Process", "OrderService.Progress", Events));
    Assert.True(HasEdge("OrderService.Summarise", "OrderService.Progress", Events));
    Assert.True(HasEdge("ProgressReporter.m_svc", "OrderService", Events));
    Assert.True(HasEdge("ProgressReporter.m_svc_Progress", "OrderService.Progress", Events));
  }

  [Fact]
  public void Ignores_comments_named_arguments_labels_and_function_return_values()
  {
    // Logger.WriteLine's comment mentions LogInfo; Main has "repo:=Nothing" and "GoTo ErrHandler";
    // "Factorial = 1" assigns the return value rather than calling the function.
    Assert.DoesNotContain(Model.MemberEdges, e => e.From == "Logger.WriteLine");
    Assert.Equal(1, Model.MemberEdges.Single(e => e.From == "Main.Factorial").Count);
    Assert.DoesNotContain(Model.MemberEdges, e => e.To.Contains("ErrHandler"));
  }

  [Fact]
  public void Reports_late_bound_references()
  {
    Assert.Equal(
    [
        new LateBoundReference("CsvOrderRepository.ReadFile", "CreateObject(…).OpenTextFile", 27),
            new LateBoundReference("Main.ProcessOrders", "legacy.ExportAll", 37),
        ], Model.LateBound.OrderBy(l => l.FromId));
  }

  [Fact]
  public void Only_UnusedHelper_is_unreferenced()
  {
    Assert.Equal(["Utils.UnusedHelper"], Model.Members.Where(m => m.IsUnreferenced).Select(m => m.Id));
  }

  [Theory]
  [InlineData("Main.ProcessOrders", MemberRole.Macro)]
  [InlineData("Main.Factorial", MemberRole.WorksheetFunction)]
  [InlineData("Sheet1.Worksheet_Change", MemberRole.BuiltInEventHandler)]
  [InlineData("Sheet1.btnRun_Click", MemberRole.ControlEventHandler)]
  [InlineData("ThisWorkbook.Workbook_Open", MemberRole.BuiltInEventHandler)]
  [InlineData("Order.Class_Initialize", MemberRole.BuiltInEventHandler)]
  [InlineData("ProgressReporter.m_svc_Progress", MemberRole.WithEventsHandler)]
  [InlineData("CsvOrderRepository.IOrderRepository_Save", MemberRole.InterfaceImplementation)]
  [InlineData("Logger.WriteLine", MemberRole.None)]
  public void Recognises_member_roles(string id, MemberRole role)
  {
    Assert.Equal(role, Model.Members.Single(m => m.Id == id).Role);
  }

  [Fact]
  public void Every_module_has_a_node_and_every_edge_end_is_a_node()
  {
    var ids = Model.Members.Select(m => m.Id).ToHashSet();

    Assert.All(Model.Modules, m => Assert.Contains(m.Id, ids));
    Assert.All(Model.MemberEdges, e => Assert.True(ids.Contains(e.From) && ids.Contains(e.To), $"{e} has an unknown end"));
    Assert.Equal(ids.Count, Model.Members.Count); // ids are unique
  }

  [Fact]
  public void Is_deterministic()
  {
    var again = DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()))));

    Assert.Equal(Model.MemberEdges, again.MemberEdges);
    Assert.Equal(Model.ModuleEdges, again.ModuleEdges);
    Assert.Equal(Model.Members.Select(m => m.Id), again.Members.Select(m => m.Id));
  }
}
