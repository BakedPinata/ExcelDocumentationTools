using PowerQueryDocumenter.Core.Analysis;

namespace PowerQueryDocumenter.Tests.Analysis;

/// <summary>
/// What a query says about itself (FR-HTML-24): its description in the workbook, otherwise the comments at the top of
/// its code.
/// </summary>
public sealed class QueryDescriptionTests
{
  [Fact]
  public void FR_HTML_24_the_workbooks_description_comes_before_the_comments()
  {
    Assert.Equal("From the workbook.", QueryDescription.Of("  From the workbook. ", "// From a comment\nlet a = 1 in a"));
    Assert.Equal("From a comment", QueryDescription.Of(null, "// From a comment\nlet a = 1 in a"));
    Assert.Equal("From a comment", QueryDescription.Of("   ", "// From a comment\nlet a = 1 in a"));
    Assert.Null(QueryDescription.Of(null, null));
  }

  [Fact]
  public void FR_HTML_24_line_comments_at_the_top_are_the_description_up_to_the_code()
  {
    var code = "// Orders from the warehouse,\r\n// one row for each line.\r\nlet\r\n    // not this one\r\n    Source = 1\r\nin\r\n    Source";

    Assert.Equal("Orders from the warehouse,\none row for each line.", QueryDescription.FromComments(code));
  }

  [Fact]
  public void FR_HTML_24_a_block_comment_counts_with_its_stars_and_decoration_dropped()
  {
    var code = "/*\n * Cleans the region names.\n *\n * ----------\n * Used by every report.\n */\n// And a line after it.\n(x) => x";

    Assert.Equal("Cleans the region names.\n\nUsed by every report.\n\nAnd a line after it.", QueryDescription.FromComments(code));
  }

  [Theory]
  [InlineData("let a = 1 in a // at the end")]
  [InlineData("let\n    // inside\n    a = 1\nin a")]
  [InlineData("")]
  [InlineData("//\n// ----\nlet a = 1 in a")]
  [InlineData("\"// not a comment\"")]
  public void FR_HTML_24_a_query_that_starts_with_code_or_says_nothing_has_none(string code)
  {
    Assert.Null(QueryDescription.FromComments(code));
  }

  [Fact]
  public void FR_HTML_24_an_unclosed_block_comment_and_a_very_long_one_are_coped_with()
  {
    Assert.Equal("never closed", QueryDescription.FromComments("/* never closed"));

    var description = QueryDescription.FromComments(string.Concat(Enumerable.Repeat("// A line of a long history of changes.\n", 60)) + "1")!;
    Assert.InRange(description.Length, QueryDescription.MaxLength - 1, QueryDescription.MaxLength);
    Assert.EndsWith("…", description, StringComparison.Ordinal);
  }
}
