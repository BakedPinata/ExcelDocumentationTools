using CodeDocumenter.Shared.Workbooks;
using VbaDocumenter.Cli;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Cli;

/// <summary>What a workbook offers to import, for the desktop app's list.</summary>
public sealed class ImportSourceTests
{
  [Fact]
  public void Every_module_is_listed_in_name_order_with_what_it_is_and_why_it_cannot_come()
  {
    var items = ImportSource.Items(Fixtures.Workbook("SimpleMacro.xlsm"));

    Assert.Equal(items.Select(i => i.Name).Order(StringComparer.OrdinalIgnoreCase), items.Select(i => i.Name));
    var module = Assert.Single(items, i => i.CanTake);
    Assert.Equal("module", module.Kind);
    Assert.All(items.Where(i => !i.CanTake), i =>
    {
      Assert.Equal("document module", i.Kind);
      Assert.Equal("A document module belongs to its workbook", i.WhyNot);
    });
    Assert.Contains(items, i => i.Name == "ThisWorkbook");
  }

  [Fact]
  public void A_workbook_that_cannot_be_read_says_why()
  {
    Assert.Throws<ExtractionException>(() => ImportSource.Items(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xlsm")));
  }
}
