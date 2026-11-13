using System.Text.Json;
using CodeDocumenter.Shared.Pages;

namespace CodeDocumenter.Shared.Tests.Pages;

/// <summary>
/// A saved views file in an output folder (step L4 of the generalisation plan): read for building into the page,
/// with a warning in place of a failure when it can't be used.
/// </summary>
public sealed class ViewsFileTests : IDisposable
{
  private const string Views = """
    {
      "formatVersion": 1,
      "tool": "vbadoc",
      "workbook": "Sales.xlsm",
      "views": [
        { "name": "Orders", "description": "How an order is <processed>", "filter": ["Order", "OrderService"], "tab": "classes" },
        { "name": "Logging", "filter": ["Logger"] }
      ]
    }
    """;

  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private ViewsFileResult Read(string? content, string tool = "vbadoc", string source = "Sales.xlsm")
  {
    if (content is not null)
    {
      temp.WriteFile(ViewsFile.FileName(tool), content);
    }

    return ViewsFile.Read(temp.Path, tool, source);
  }

  [Fact]
  public void The_file_is_named_for_its_tool()
  {
    Assert.Equal("vbadoc-views.json", ViewsFile.FileName("vbadoc"));
    Assert.Equal("pqdoc-views.json", ViewsFile.FileName("pqdoc"));
  }

  [Fact]
  public void No_file_is_the_usual_case_and_says_nothing()
  {
    Assert.Equal(ViewsFileResult.None, Read(null));
    Assert.Equal(ViewsFileResult.None, ViewsFile.Read(temp.Combine("not-made-yet"), "vbadoc", "Sales.xlsm"));
  }

  [Fact]
  public void A_views_file_gives_its_views_as_the_page_reads_them()
  {
    var result = Read(Views);

    Assert.Null(result.Warning);
    Assert.Equal(2, result.Count);
    using var built = JsonDocument.Parse(result.Json!);
    Assert.Equal(["formatVersion", "tool", "views"], built.RootElement.EnumerateObject().Select(p => p.Name));
    Assert.Equal(1, built.RootElement.GetProperty("formatVersion").GetInt32());
    Assert.Equal("vbadoc", built.RootElement.GetProperty("tool").GetString());
    var views = built.RootElement.GetProperty("views");
    Assert.Equal(["Orders", "Logging"], views.EnumerateArray().Select(v => v.GetProperty("name").GetString()));
    Assert.Equal("How an order is <processed>", views[0].GetProperty("description").GetString());
    Assert.Equal(["Order", "OrderService"], views[0].GetProperty("filter").EnumerateArray().Select(i => i.GetString()));
  }

  [Fact]
  public void The_same_file_always_gives_the_same_text_with_line_feeds()
  {
    var first = Read(Views).Json!;
    var again = Read(Views.ReplaceLineEndings("\r\n")).Json!;

    Assert.Equal(first, again);
    Assert.DoesNotContain('\r', first);
  }

  [Theory]
  [InlineData("{ not json", "it isn't valid JSON")]
  [InlineData("[]", "it isn't a views file")]
  [InlineData("{ \"formatVersion\": 1, \"tool\": \"vbadoc\" }", "it isn't a views file")]
  [InlineData("{ \"tool\": \"vbadoc\", \"views\": [] }", "it isn't a views file")]
  [InlineData("{ \"formatVersion\": 1, \"tool\": \"pqdoc\", \"views\": [] }", "it holds pqdoc's views, not vbadoc's")]
  [InlineData("{ \"formatVersion\": 1, \"views\": [] }", "it holds another tool's views, not vbadoc's")]
  [InlineData("{ \"formatVersion\": 2, \"tool\": \"vbadoc\", \"views\": [] }", "it was written by a newer vbadoc than this one")]
  public void A_file_that_cant_be_used_is_a_warning_and_nothing_is_built_in(string content, string why)
  {
    var result = Read(content);

    Assert.Null(result.Json);
    Assert.Equal(0, result.Count);
    Assert.StartsWith("vbadoc-views.json wasn't built into the page: " + why, result.Warning, StringComparison.Ordinal);
  }

  [Fact]
  public void Views_without_a_name_are_left_out_and_counted()
  {
    var result = Read("""{ "formatVersion": 1, "tool": "vbadoc", "views": [ { "name": "Kept" }, { "filter": [] }, { "name": "  " }, 7 ] }""");

    Assert.Equal(1, result.Count);
    Assert.Equal("vbadoc-views.json: 3 without a name were left out.", result.Warning);
    Assert.Contains("\"Kept\"", result.Json, StringComparison.Ordinal);
  }

  [Fact]
  public void An_empty_views_file_builds_nothing_in_and_says_nothing()
  {
    Assert.Equal(ViewsFileResult.None, Read("""{ "formatVersion": 1, "tool": "vbadoc", "views": [] }"""));
  }

  [Fact]
  public void Views_of_another_source_are_built_in_with_a_warning()
  {
    var result = Read(Views, source: "Purchases.xlsm");

    Assert.Equal(2, result.Count);
    Assert.NotNull(result.Json);
    Assert.Equal("vbadoc-views.json: it holds views of Sales.xlsm, not Purchases.xlsm, so they may mention items this page doesn't have.", result.Warning);
  }

  [Fact]
  public void Reading_leaves_the_file_as_it_was()
  {
    var path = temp.WriteFile("vbadoc-views.json", Views);
    var written = File.GetLastWriteTimeUtc(path);

    ViewsFile.Read(temp.Path, "vbadoc", "Sales.xlsm");

    Assert.Equal(Views, File.ReadAllText(path));
    Assert.Equal(written, File.GetLastWriteTimeUtc(path));
  }
}
