using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>How many other modules use each module and member, which the page uses to find widely used items (D4).</summary>
public class UsageTests
{
  static readonly DependencyModel Model =
      DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()))));

  static readonly IReadOnlyDictionary<string, int> UsedBy = Usage.UsedByOtherModules(Model);

  [Theory]
  [InlineData("Logger", 8)]           // every module but itself, IOrderRepository, Order and ThisWorkbook
  [InlineData("Order", 5)]
  [InlineData("IOrderRepository", 4)] // two implement it, two use it
  [InlineData("OrderService", 2)]     // Main uses it, and ProgressReporter both uses it and handles its events
  [InlineData("Sheet1", 2)]
  public void A_module_counts_each_other_module_that_uses_it_once_whatever_the_edges(string module, int expected)
  {
    Assert.Equal(expected, UsedBy[module]);
  }

  [Theory]
  [InlineData("Logger.LogInfo", 7)]
  [InlineData("Logger.LogError", 3)]
  public void A_member_counts_the_other_modules_whose_code_uses_it(string member, int expected)
  {
    Assert.Equal(expected, UsedBy[member]);
  }

  [Fact]
  public void Nothing_is_counted_for_what_only_its_own_module_uses_or_nothing_uses()
  {
    Assert.False(UsedBy.ContainsKey("AppInit.LoadSettings")); // a Private Sub only AppInit calls
    Assert.False(UsedBy.ContainsKey("Utils.UnusedHelper"));
    Assert.False(UsedBy.ContainsKey("ThisWorkbook"));
    Assert.Equal(1, UsedBy["Main"]);                          // Sheet1's button calls it
  }

  [Fact]
  public void No_count_is_more_than_the_other_modules_and_a_member_never_outnumbers_its_module()
  {
    var others = Model.Modules.Count - 1;
    Assert.All(UsedBy.Values, count => Assert.InRange(count, 1, others));
    var moduleOf = Model.Members.Where(n => !n.IsModuleNode).ToDictionary(n => n.Id, n => n.Module.Name);
    Assert.All(UsedBy.Where(p => moduleOf.ContainsKey(p.Key)), p => Assert.True(p.Value <= UsedBy[moduleOf[p.Key]], p.Key));
  }
}
