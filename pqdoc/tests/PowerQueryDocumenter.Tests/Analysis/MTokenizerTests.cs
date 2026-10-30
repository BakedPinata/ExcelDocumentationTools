using PowerQueryDocumenter.Analysis;

namespace PowerQueryDocumenter.Tests.Analysis;

public sealed class MTokenizerTests
{
  private static IReadOnlyList<MToken> Tokens(string m) => MTokenizer.Tokenize(m);

  private static string[] Texts(string m, MTokenKind kind) =>
    Tokens(m).Where(t => t.Kind == kind).Select(t => t.Text).ToArray();

  [Fact]
  public void Line_comments_are_ignored()
  {
    var identifiers = Texts("a // Sales is mentioned here\r\nb", MTokenKind.Identifier);

    Assert.Equal(new[] { "a", "b" }, identifiers);
  }

  [Fact]
  public void Block_comments_are_ignored_across_lines()
  {
    var identifiers = Texts("a /* Sales\n Orders\n*/ b", MTokenKind.Identifier);

    Assert.Equal(new[] { "a", "b" }, identifiers);
  }

  [Fact]
  public void Unterminated_block_comment_runs_to_the_end_without_throwing()
  {
    var identifiers = Texts("a /* Sales Orders", MTokenKind.Identifier);

    Assert.Equal(new[] { "a" }, identifiers);
  }

  [Fact]
  public void Text_literals_are_single_tokens_with_escaped_quotes_unescaped()
  {
    var tokens = Tokens("x = \"Say \"\"Sales\"\" please\"");

    var literal = Assert.Single(tokens, t => t.Kind == MTokenKind.TextLiteral);
    Assert.Equal("Say \"Sales\" please", literal.Text);
    Assert.DoesNotContain(tokens, t => t.IsIdentifier && t.Text == "Sales");
  }

  [Fact]
  public void Comment_markers_inside_text_literals_are_not_comments()
  {
    var tokens = Tokens("Web.Contents(\"https://example.com/*path\") & Orders");

    Assert.Contains(tokens, t => t.Kind == MTokenKind.TextLiteral && t.Text == "https://example.com/*path");
    Assert.Contains(tokens, t => t.Kind == MTokenKind.Identifier && t.Text == "Orders");
  }

  [Fact]
  public void Quotes_inside_comments_do_not_start_text_literals()
  {
    var identifiers = Texts("a // it's a \"quote\nOrders", MTokenKind.Identifier);

    Assert.Equal(new[] { "a", "Orders" }, identifiers);
  }

  [Fact]
  public void Unterminated_text_literal_runs_to_the_end_without_throwing()
  {
    var tokens = Tokens("a = \"never closed Orders");

    Assert.Equal(MTokenKind.TextLiteral, tokens[^1].Kind);
    Assert.DoesNotContain(tokens, t => t.Text == "Orders" && t.IsIdentifier);
  }

  [Theory]
  [InlineData("#\"Sales Data\"", "Sales Data")]
  [InlineData("#\"Say \"\"Hi\"\"\"", "Say \"Hi\"")]
  [InlineData("#\"Revenue & Margin (2024)\"", "Revenue & Margin (2024)")]
  [InlineData("#\"\"", "")]
  public void Quoted_identifiers_are_unescaped(string m, string expected)
  {
    var token = Assert.Single(Tokens(m));

    Assert.Equal(MTokenKind.QuotedIdentifier, token.Kind);
    Assert.Equal(expected, token.Text);
  }

  [Theory]
  [InlineData("Table.SelectRows")]
  [InlineData("Int64.Type")]
  [InlineData("Q1.Summary")]
  [InlineData("fn_Clean2")]
  public void Dotted_identifiers_are_single_tokens(string m)
  {
    var token = Assert.Single(Tokens(m));

    Assert.Equal(MTokenKind.Identifier, token.Kind);
    Assert.Equal(m, token.Text);
  }

  [Fact]
  public void Range_operator_is_not_part_of_an_identifier_or_number()
  {
    var texts = Tokens("{1..10, a..b}").Select(t => t.Text).ToArray();

    Assert.Equal(new[] { "{", "1", "..", "10", ",", "a", "..", "b", "}" }, texts);
  }

  [Fact]
  public void Keywords_are_case_sensitive()
  {
    var tokens = Tokens("let Let each Each");

    Assert.Equal(
      new[] { MTokenKind.Keyword, MTokenKind.Identifier, MTokenKind.Keyword, MTokenKind.Identifier },
      tokens.Select(t => t.Kind).ToArray());
  }

  [Theory]
  [InlineData("#table")]
  [InlineData("#date")]
  [InlineData("#shared")]
  public void Hash_keywords_are_recognised(string m)
  {
    var token = Assert.Single(Tokens(m));

    Assert.Equal(MTokenKind.HashKeyword, token.Kind);
  }

  [Fact]
  public void Multi_character_operators_are_single_tokens()
  {
    var punctuators = Texts("(x) => x <> 1 and y <= 2 ?? z", MTokenKind.Punctuator);

    Assert.Contains("=>", punctuators);
    Assert.Contains("<>", punctuators);
    Assert.Contains("<=", punctuators);
    Assert.Contains("??", punctuators);
  }

  [Theory]
  [InlineData("123")]
  [InlineData("1.5")]
  [InlineData("1e10")]
  [InlineData("2.5E-3")]
  [InlineData("0xFF")]
  [InlineData(".5")]
  public void Numbers_are_single_tokens(string m)
  {
    var token = Assert.Single(Tokens(m));

    Assert.Equal(MTokenKind.NumberLiteral, token.Kind);
    Assert.Equal(m, token.Text);
  }

  [Fact]
  public void Positions_point_at_the_start_of_each_token()
  {
    var tokens = Tokens("let  Sales");

    Assert.Equal(0, tokens[0].Position);
    Assert.Equal(5, tokens[1].Position);
  }

  [Fact]
  public void Empty_input_gives_no_tokens()
  {
    Assert.Empty(Tokens(string.Empty));
  }
}
