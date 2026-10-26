using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Tests.Parsing;

public class LogicalLineReaderTests
{
    [Fact]
    public void Joins_continued_lines_and_keeps_their_line_range()
    {
        var lines = LogicalLineReader.Read("Public Function F(a, _\r\n                  b)\r\nEnd Function\r\n");

        Assert.Equal(
        [
            new LogicalLine("Public Function F(a, b)", null, 1, 2),
            new LogicalLine("End Function", null, 3, 3),
        ], lines);
    }

    [Fact]
    public void An_underscore_at_the_end_of_a_name_is_not_a_continuation()
    {
        var lines = LogicalLineReader.Read("x = my_\r\ny = 1");

        Assert.Equal(["x = my_", "y = 1"], lines.Select(l => l.Code));
    }

    [Fact]
    public void Splits_off_apostrophe_comments_but_not_apostrophes_in_strings()
    {
        var line = LogicalLineReader.Read("x = \"it's\" ' a note").Single();

        Assert.Equal("x = \"it's\"", line.Code);
        Assert.Equal(" a note", line.Comment);
    }

    [Fact]
    public void Rem_starts_a_comment_at_the_start_of_a_statement_only()
    {
        var lines = LogicalLineReader.Read("Rem hello\r\nx = 1: Rem note\r\nRemove x\r\nRem");

        Assert.True(lines[0].IsCommentOnly);
        Assert.Equal(["x = 1"], LogicalLineReader.SplitStatements(lines[1].Code));
        Assert.Equal("note", lines[1].Comment);
        Assert.Equal("Remove x", lines[2].Code);
        Assert.True(lines[3].IsCommentOnly);
    }

    [Fact]
    public void A_continued_comment_is_one_comment()
    {
        // As in the VBA editor, " _" continues a comment onto the next line.
        var lines = LogicalLineReader.Read("' first _\r\n  second\r\nx = 1");

        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].IsCommentOnly);
        Assert.Equal((1, 2), (lines[0].FirstLine, lines[0].LastLine));
    }

    [Fact]
    public void Collapses_whitespace_outside_strings_only()
    {
        Assert.Equal("x = \"a   b\" & c", LogicalLineReader.Read("   x   =  \"a   b\"  &   c   ").Single().Code);
    }

    [Fact]
    public void Counts_blank_lines_but_not_the_newline_at_the_end_of_the_file()
    {
        var lines = LogicalLineReader.Read("a\r\n\r\nb\r\n");

        Assert.Equal([1, 2, 3], lines.Select(l => l.FirstLine));
        Assert.True(lines[1].IsBlank);
    }

    [Fact]
    public void Reads_LF_only_text_the_same_way()
    {
        Assert.Equal(
            LogicalLineReader.Read("a = 1 _\r\n  + 2\r\n' c\r\n"),
            LogicalLineReader.Read("a = 1 _\n  + 2\n' c\n"));
    }

    [Theory]
    [InlineData("a = 1: b = 2", new[] { "a = 1", "b = 2" })]
    [InlineData("Foo x:=1, y:=2", new[] { "Foo x:=1, y:=2" })]
    [InlineData("s = \"a:b\": t = \"say \"\"x:y\"\"\"", new[] { "s = \"a:b\"", "t = \"say \"\"x:y\"\"\"" })]
    [InlineData("t = #12:30:00 PM#: u = 1", new[] { "t = #12:30:00 PM#", "u = 1" })]
    [InlineData("Print #1, \"x\": y = 2", new[] { "Print #1, \"x\"", "y = 2" })]
    [InlineData("d# = 1: e = 2", new[] { "d# = 1", "e = 2" })]
    public void Splits_statements_at_colons_outside_literals(string code, string[] expected)
    {
        Assert.Equal(expected, LogicalLineReader.SplitStatements(code));
    }
}
