using PowerQueryDocumenter.Cli;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Application;

/// <summary>What a workbook or folder offers to import, for the desktop app's list.</summary>
public sealed class ImportSourceTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  [Fact]
  public async Task A_workbooks_queries_are_listed_in_name_order_and_every_one_can_come()
  {
    var items = await ImportSource.ItemsAsync(Fixtures.Workbook("Queries.xlsx"), TestContext.Current.CancellationToken);

    Assert.Equal(14, items.Count);
    Assert.Equal(items.Select(i => i.Name).Order(StringComparer.OrdinalIgnoreCase), items.Select(i => i.Name));
    Assert.All(items, i => Assert.True(i.CanTake));
    Assert.Equal("function", Assert.Single(items, i => i.Name == "fnClean").Kind);
    Assert.Equal("query", Assert.Single(items, i => i.Name == "Sales Data").Kind);
  }

  [Fact]
  public async Task A_folders_queries_are_listed_too()
  {
    temp.WriteFile("queries/Base.m", "let\r\n    Source = 1\r\nin\r\n    Source\r\n");
    temp.WriteFile("queries/Twice.m", "(x) => x * 2");

    var items = await ImportSource.ItemsAsync(temp.Combine("queries"), TestContext.Current.CancellationToken);

    Assert.Equal(new[] { ("Base", "query"), ("Twice", "function") }, items.Select(i => (i.Name, i.Kind)));
  }
}
