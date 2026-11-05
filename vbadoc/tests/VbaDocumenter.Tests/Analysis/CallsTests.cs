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
