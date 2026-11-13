using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>
/// Each procedure's calls, in the order they appear in its code (step E2 of the feedback plan), which the Calls tab
/// follows: to procedures, properties, events and Declares, and late-bound calls, but not variables or constants.
/// </summary>
public class CallsTests
{
  static readonly DependencyModel Model =
      DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()))));

  static (string What, int Line)[] CallsOf(string from) =>
      Model.Calls.Where(c => c.From == from).Select(c => (c.To ?? "late-bound " + c.LateBound, c.Line)).ToArray();

  [Fact]
  public void A_procedures_calls_are_in_the_order_they_appear_in_its_code()
  {
    (string, int)[] expected =
    [
        ("OrderService.Init", 18),
        ("ProgressReporter.Attach", 19),
        ("IOrderRepository.LoadAll", 21),  // through the interface the variable is declared as
        ("Order.Status[Get]", 22),
        ("OrderService.Process", 24),
        ("Logger.LogInfo", 26),
        ("Order.Id[Get]", 26),
        ("OrderService.Summarise", 33),    // in a With block
        ("late-bound legacy.ExportAll", 37),
        ("Main.Factorial", 38),
        ("Logger.LogError", 41),
    ];

    Assert.Equal(expected, CallsOf("Main.ProcessOrders"));
  }

  [Fact]
  public void Each_call_is_in_the_innermost_block_around_it_and_a_blocks_own_line_is_in_the_block_around_that()
  {
    var process = Model.Members.Single(n => n.Id == "Main.ProcessOrders").Member!;
    string Where(string to, int line)
    {
      var block = Model.Calls.Single(c => c.From == "Main.ProcessOrders" && c.To == to && c.Line == line).Block;
      return block is { } id ? process.Blocks[id].Text : "(top)";
    }

    Assert.Equal("(top)", Where("OrderService.Init", 18));
    Assert.Equal("(top)", Where("IOrderRepository.LoadAll", 21));                  // For Each's own line
    Assert.Equal("For Each o In repo.LoadAll", Where("Order.Status[Get]", 22));    // Select Case's own line, in the loop
    Assert.Equal("Case \"NEW\"", Where("OrderService.Process", 24));
    Assert.Equal("Case \"HOLD\"", Where("Logger.LogInfo", 26));
    Assert.Equal("With svc", Where("OrderService.Summarise", 33));
    Assert.Equal("On error: ErrHandler", Where("Logger.LogError", 41));

    var factorial = Model.Members.Single(n => n.Id == "Main.Factorial").Member!;
    Assert.Equal("Else", factorial.Blocks[Model.Calls.Single(c => c.From == "Main.Factorial").Block!.Value].Text); // a single-line If's Else
  }

  [Fact]
  public void A_call_made_by_a_blocks_own_statement_is_that_blocks_and_stays_in_the_block_around_it()
  {
    string Condition(string from, string to)
    {
      var member = Model.Members.Single(n => n.Id == from).Member!;
      var call = Model.Calls.First(c => c.From == from && c.To == to);
      return call.Condition is { } id ? member.Blocks[id].Text : "(none)";
    }

    Assert.Equal("For Each o In repo.LoadAll", Condition("Main.ProcessOrders", "IOrderRepository.LoadAll"));
    Assert.Equal("Select Case o.Status", Condition("Main.ProcessOrders", "Order.Status[Get]"));
    Assert.Equal("(none)", Condition("Main.ProcessOrders", "OrderService.Process"));
    Assert.Equal("(none)", Condition("Main.ProcessOrders", "OrderService.Summarise")); // inside the With, not its object
    Assert.Equal("If o.IsValid Then", Condition("OrderService.Process", "Order.IsValid"));
    Assert.Equal("ElseIf o.Status = \"DRAFT\" Then", Condition("OrderService.Process", "Order.Status[Get]"));
    Assert.Equal("(none)", Condition("OrderService.Process", "Order.Status[Let]"));

    // Its block is still the one around the statement, as before.
    Assert.Null(Model.Calls.First(c => c.From == "Main.ProcessOrders" && c.To == "IOrderRepository.LoadAll").Block);
  }

  [Fact]
  public void The_calls_in_an_Or_condition_are_the_Ifs_even_when_nothing_inside_the_If_makes_a_call()
  {
    // The shape raised with step E5b: before, IsA and IsB showed loose above the Else, with no If row at all.
    const string helper = "Attribute VB_Name = \"Helper\"\r\nOption Explicit\r\n" +
        "Public Function IsA(ByVal n As Long) As Boolean\r\nEnd Function\r\n" +
        "Public Function IsB(ByVal n As Long) As Boolean\r\nEnd Function\r\n" +
        "Public Sub DoB()\r\nEnd Sub\r\n";
    const string caller = "Attribute VB_Name = \"Caller\"\r\nOption Explicit\r\n" +
        "Public Sub Run()\r\n    Dim x As Long\r\n" +
        "    If Helper.IsA(1) Or Helper.IsB(2) Then\r\n        x = 1\r\n    Else\r\n        Helper.DoB\r\n    End If\r\n" +
        "End Sub\r\n";
    var model = DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(
        [new SyntheticModule("Helper", VbaModuleKind.Standard, helper), new SyntheticModule("Caller", VbaModuleKind.Standard, caller)]))));
    var run = model.Members.Single(n => n.Id == "Caller.Run").Member!;
    string Text(int? block) => block is { } id ? run.Blocks[id].Text : "(none)";

    Assert.Equal(
        [("Helper.IsA", "If Helper.IsA(1) Or Helper.IsB(2) Then", "(none)"),
         ("Helper.IsB", "If Helper.IsA(1) Or Helper.IsB(2) Then", "(none)"),
         ("Helper.DoB", "(none)", "Else")],
        model.Calls.Where(c => c.From == "Caller.Run").Select(c => (c.To!, Text(c.Condition), Text(c.Block))));
  }

  [Fact]
  public void Recursion_is_a_call_to_itself_but_assigning_the_result_is_not()
  {
    Assert.Equal([("Main.Factorial", 45)], CallsOf("Main.Factorial"));
  }

  [Fact]
  public void Calls_through_an_interface_RaiseEvent_and_both_sides_of_a_property_are_listed()
  {
    var process = CallsOf("OrderService.Process").Select(c => c.What).ToArray();

    Assert.Equal(
        ["Order.IsValid", "IOrderRepository.Save", "OrderService.Progress", "Order.Describe", "Order.Status[Get]", "Order.Status[Let]",
         "Logger.LogError", "Order.Id[Get]"],
        process);
  }

  [Fact]
  public void Variables_constants_and_types_are_not_calls()
  {
    var targets = Model.Calls.Where(c => c.To != null).Select(c => c.To!).ToHashSet();
    var kinds = Model.Members.Where(n => n.Member != null && targets.Contains(n.Id)).Select(n => n.Member!.Kind).ToHashSet();

    Assert.DoesNotContain(MemberKind.Variable, kinds);
    Assert.DoesNotContain(MemberKind.Constant, kinds);
    Assert.DoesNotContain(MemberKind.Type, kinds);
    Assert.DoesNotContain("AppInit.g_UseCsv", targets); // read in LoadSettings, and in Main
    Assert.All(Model.Calls, c => Assert.True(c.To == null || targets.Contains(c.To)));
  }

  [Fact]
  public void Every_call_is_also_a_reference_on_the_same_line()
  {
    Assert.All(Model.Calls.Where(c => c.To != null), call =>
        Assert.Contains(Model.MemberEdges, e => e.From == call.From && e.To == call.To && e.Lines.Contains(call.Line)));
  }
}
