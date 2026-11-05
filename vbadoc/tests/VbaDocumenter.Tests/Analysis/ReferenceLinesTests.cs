using System.Text;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;
using static VbaDocumenter.Core.Analysis.EdgeKind;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>
/// Where each reference is (step E1 of the feedback plan): every member edge has the lines of its module's file, as the
/// page shows it, where its references are, so Uses / Used by can link to them.
/// </summary>
public class ReferenceLinesTests
{
  static readonly VbaProject Project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()));
  static readonly DependencyModel Model = DependencyAnalyser.Analyse(ProjectParser.Parse(Project));

  static IReadOnlyList<int> Lines(string from, string to, EdgeKind kind = Uses) =>
      Model.MemberEdges.Single(e => e.From == from && e.To == to && e.Kind == kind).Lines;

  // The file the page shows, split as the page splits it.
  static string[] FileLines(string module) =>
      Encoding.Latin1.GetString(ProjectExporter.FileContent(Project.Modules.Single(m => m.Name == module)))
          .Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

  [Fact]
  public void A_call_is_on_its_statements_line_and_each_line_is_listed_once()
  {
    Assert.Equal([8], Lines("AppInit.Startup", "Logger.LogInfo"));
    Assert.Equal([13], Lines("AppInit.LoadSettings", "Logger.LogInfo"));
    Assert.Equal([12], Lines("AppInit.LoadSettings", "AppInit.g_UseCsv"));
  }

  [Fact]
  public void Every_member_edge_has_lines_inside_its_member_that_name_what_it_refers_to()
  {
    var members = Model.Members.Where(n => !n.IsModuleNode).ToDictionary(n => n.Id);
    Assert.All(Model.MemberEdges, edge =>
    {
      Assert.NotEmpty(edge.Lines);
      var from = members[edge.From];
      var file = FileLines(from.Module.Name);
      var name = members.TryGetValue(edge.To, out var to) ? to.Member!.Name : edge.To; // a module node is the module's name
      Assert.All(edge.Lines, line =>
      {
        Assert.InRange(line, from.Member!.DeclarationLine, from.Member.EndLine);
        // A WithEvents handler names the event after the variable, as in m_svc_Progress.
        Assert.Contains(name, file[line - 1], StringComparison.OrdinalIgnoreCase);
      });
    });
  }

  [Fact]
  public void An_interface_implementation_and_an_event_handler_are_on_their_declarations()
  {
    var save = Model.MemberEdges.Single(e => e.From == "CsvOrderRepository.IOrderRepository_Save" && e.Kind == Implements);
    var declaration = Model.Members.Single(n => n.Id == "CsvOrderRepository.IOrderRepository_Save").Member!.DeclarationLine;
    Assert.Equal([declaration], save.Lines);

    var handler = Model.MemberEdges.Single(e => e.From == "ProgressReporter.m_svc_Progress" && e.Kind == Events);
    Assert.Equal([Model.Members.Single(n => n.Id == "ProgressReporter.m_svc_Progress").Member!.DeclarationLine], handler.Lines);
  }

  [Fact]
  public void A_reference_on_a_continued_line_is_on_the_physical_line_that_names_it()
  {
    var calls = new SyntheticModule("Calls", VbaModuleKind.Standard, string.Join("\r\n",
        "Attribute VB_Name = \"Calls\"",
        "Option Explicit",
        "Public Sub Run()",
        "    Dim total As Long",
        "    total = Helpers.One() + _",
        "        Helpers.Two()",
        "End Sub",
        "Public Function Describe(ByVal first As Long, _",
        "    ByVal second As Thing) As String",
        "End Function",
        ""));
    var helpers = new SyntheticModule("Helpers", VbaModuleKind.Standard, string.Join("\r\n",
        "Attribute VB_Name = \"Helpers\"",
        "Public Function One() As Long",
        "End Function",
        "Public Function Two() As Long",
        "End Function",
        ""));
    var thing = new SyntheticModule("Thing", VbaModuleKind.Class, string.Join("\r\n", "Attribute VB_Name = \"Thing\"", "Option Explicit", ""));
    var model = DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm([calls, helpers, thing]))));
    IReadOnlyList<int> LinesOf(string from, string to) => model.MemberEdges.Single(e => e.From == from && e.To == to).Lines;

    Assert.Equal([5], LinesOf("Calls.Run", "Helpers.One"));
    Assert.Equal([6], LinesOf("Calls.Run", "Helpers.Two"));
    Assert.Equal([9], LinesOf("Calls.Describe", "Thing"));
  }

  [Fact]
  public void Module_edges_have_no_lines_of_their_own()
  {
    Assert.All(Model.ModuleEdges, edge => Assert.Empty(edge.Lines));
  }
}
