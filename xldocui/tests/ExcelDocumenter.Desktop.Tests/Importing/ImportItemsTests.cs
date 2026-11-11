using ExcelDocumenter.Desktop.Importing;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.Importing;
using ExcelDocumenter.Shared.Workbooks;

namespace ExcelDocumenter.Desktop.Tests.Importing;

/// <summary>The Import tab's list, read by the tool that would take from the source.</summary>
public sealed class ImportItemsTests : IDisposable
{
  private readonly TempFolder folder = new();

  public void Dispose() => folder.Dispose();

  [Fact]
  public async Task Vbadoc_lists_a_workbooks_modules_with_why_some_cannot_come()
  {
    var items = await ImportItems.ReadAsync(Tool.Vbadoc, folder.Fixture("SimpleMacro.xlsm"), TestContext.Current.CancellationToken);

    Assert.Contains(items, item => item.CanTake);
    Assert.Contains(items, item => item.Name == "ThisWorkbook" && !item.CanTake);
  }

  [Fact]
  public async Task Pqdoc_lists_a_workbooks_queries()
  {
    var items = await ImportItems.ReadAsync(Tool.Pqdoc, folder.Fixture("Queries.xlsx"), TestContext.Current.CancellationToken);

    Assert.Equal(14, items.Count);
  }

  [Fact]
  public async Task A_source_that_cannot_be_read_says_why_in_place_of_the_list()
  {
    var notes = folder.Combine("Notes.xlsm");
    File.WriteAllText(notes, "not a workbook");

    var thrown = await Assert.ThrowsAsync<ExtractionException>(() => ImportItems.ReadAsync(Tool.Vbadoc, notes, TestContext.Current.CancellationToken));

    Assert.Equal(thrown.Message, ImportItems.WhyUnreadable(thrown));
    Assert.Null(ImportItems.WhyUnreadable(new InvalidOperationException("a fault of the app's")));
  }

  [Fact]
  public void The_line_under_the_list_says_what_there_is_and_whats_ticked()
  {
    ImportableItem[] items = [new("Utilities", "module", null), new("Logger", "class", null), new("Sheet1", "document module", "A document module belongs to its workbook")];

    Assert.Equal("2 to choose from, and 1 that can't come. 1 ticked. What they use comes with them: Preview says what.", ImportItems.Summary(items, 1));
    Assert.Equal("2 to choose from. 0 ticked. What they use comes with them: Preview says what.", ImportItems.Summary(items[..2], 0));
  }
}
