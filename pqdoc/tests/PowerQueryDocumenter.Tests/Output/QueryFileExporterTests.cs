using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Output;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Output;

/// <summary>Writing a workbook's queries as .m files, kept in step with the workbook (FR-QF-1 to FR-QF-4).</summary>
public sealed class QueryFileExporterTests : IDisposable
{
  private readonly TempDirectory temp = new();

  private string Folder => temp.Combine("PowerQuery");

  public void Dispose() => temp.Dispose();

  private static WorkbookQuery Query(string name, string expression) => new(name, expression, null);

  private Task<QueryFileResult> Export(params WorkbookQuery[] queries) => QueryFileExporter.ExportAsync(queries, Folder);

  [Fact]
  public async Task Each_query_gets_a_file_holding_its_expression_with_CRLF()
  {
    var result = await Export(Query("Sales/Returns", "let\n    A = 1\nin\n    A"), Query("B", "2"));

    Assert.Equal(new[] { "B.m", "Sales%2FReturns.m" }, result.Written);
    Assert.Equal("let\r\n    A = 1\r\nin\r\n    A"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(Folder, "Sales%2FReturns.m"), TestContext.Current.CancellationToken));
    Assert.True(result.CreatedGitAttributes);
    Assert.Contains("*.m -text diff", await File.ReadAllTextAsync(Path.Combine(Folder, ".gitattributes"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Unchanged_files_are_not_rewritten_and_keep_their_timestamps()
  {
    await Export(Query("A", "1"), Query("B", "2"));
    var path = Path.Combine(Folder, "A.m");
    var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(path, stamp);

    var result = await Export(Query("A", "1"), Query("B", "3"));

    Assert.Equal(new[] { "A.m" }, result.Unchanged);
    Assert.Equal(new[] { "B.m" }, result.Written);
    Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    Assert.False(result.CreatedGitAttributes);
  }

  [Fact]
  public async Task Files_whose_query_has_gone_are_deleted_and_other_files_are_left_alone()
  {
    await Export(Query("A", "1"), Query("Old", "2"));
    temp.WriteFile(Path.Combine("PowerQuery", "notes.txt"), "mine");
    temp.WriteFile(Path.Combine("PowerQuery", "Sales-powerquery.html"), "a page");
    temp.WriteFile(Path.Combine("PowerQuery", "sub", "Deep.m"), "1");

    var result = await Export(Query("A", "1"));

    Assert.Equal(new[] { "Old.m" }, result.Deleted);
    Assert.Equal(new[] { ".gitattributes", "A.m", "Sales-powerquery.html", "notes.txt" }, temp.FileNames("PowerQuery"));
    Assert.True(File.Exists(Path.Combine(Folder, "sub", "Deep.m"))); // only the top level is pqdoc's
  }

  [Fact]
  public async Task A_query_renamed_only_by_case_gets_its_new_spelling()
  {
    await Export(Query("sales", "1"));

    var result = await Export(Query("Sales", "1"));

    Assert.Equal(new[] { "sales.m" }, result.Deleted);
    Assert.Equal(new[] { "Sales.m" }, result.Written);
    Assert.Contains("Sales.m", Directory.GetFiles(Folder).Select(Path.GetFileName));
  }

  [Fact]
  public async Task An_existing_gitattributes_is_never_overwritten()
  {
    temp.WriteFile(Path.Combine("PowerQuery", ".gitattributes"), "* text=auto\n");

    var result = await Export(Query("A", "1"));

    Assert.False(result.CreatedGitAttributes);
    Assert.Equal("* text=auto\n", await File.ReadAllTextAsync(Path.Combine(Folder, ".gitattributes"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Names_that_would_share_a_file_stop_before_anything_is_written()
  {
    await Export(Query("Kept", "1"));

    await Assert.ThrowsAsync<ExtractionException>(() => Export(Query("A", "1"), Query("a", "2")));

    Assert.Equal(new[] { ".gitattributes", "Kept.m" }, temp.FileNames("PowerQuery"));
  }
}
