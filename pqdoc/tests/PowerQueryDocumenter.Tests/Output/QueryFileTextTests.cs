using PowerQueryDocumenter.Core.Output;

namespace PowerQueryDocumenter.Tests.Output;

/// <summary>Line endings in .m files: CRLF, except inside text literals (FR-QF-3).</summary>
public sealed class QueryFileTextTests
{
  private static string Crlf(string m) => QueryFileText.WithCrlfOutsideText(m);

  [Fact]
  public void Code_gets_CRLF_whatever_it_had()
  {
    Assert.Equal("let\r\n    A = 1\r\nin\r\n    A", Crlf("let\n    A = 1\rin\r\n    A"));
  }

  [Fact]
  public void Line_breaks_inside_text_stay_as_they_were()
  {
    Assert.Equal("let\r\n    T = \"one\ntwo\rthree\r\nfour\"\r\nin T", Crlf("let\n    T = \"one\ntwo\rthree\r\nfour\"\nin T"));
  }

  [Fact]
  public void Escaped_quotes_do_not_end_the_text()
  {
    Assert.Equal("\"say \"\"hi\"\"\nthere\"\r\n", Crlf("\"say \"\"hi\"\"\nthere\"\n"));
  }

  [Fact]
  public void A_quote_inside_a_comment_does_not_start_text()
  {
    Assert.Equal("1 // it's \"quoted\r\n/* and \"\r\nhere */\r\n2", Crlf("1 // it's \"quoted\n/* and \"\nhere */\n2"));
  }

  [Fact]
  public void Quoted_names_follow_the_same_rules_and_unterminated_text_runs_to_the_end()
  {
    Assert.Equal("#\"A\"\"B\"\r\n\"open\nend", Crlf("#\"A\"\"B\"\n\"open\nend"));
  }

  [Fact]
  public void Files_are_UTF8_without_a_byte_order_mark()
  {
    Assert.Equal("é\r\n"u8.ToArray(), QueryFileText.Bytes("é\n"));
  }
}
