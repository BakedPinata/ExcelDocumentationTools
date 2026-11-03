using PowerQueryDocumenter.Core.Output;

namespace PowerQueryDocumenter.Tests.Output;

/// <summary>Query names to .m file names and back (FR-QF-2).</summary>
public sealed class QueryFileNamesTests
{
  [Theory]
  [InlineData("Sales Data", "Sales Data.m")]
  [InlineData("Sales/Returns", "Sales%2FReturns.m")]
  [InlineData("Discount%", "Discount%25.m")]
  [InlineData("a<b>c:d\"e\\f|g?h*i", "a%3Cb%3Ec%3Ad%22e%5Cf%7Cg%3Fh%2Ai.m")]
  [InlineData("CON", "%43ON.m")]
  [InlineData("con", "%63on.m")]
  [InlineData("LPT9", "%4CPT9.m")]
  [InlineData("Console", "Console.m")]
  [InlineData("Größe", "Größe.m")]
  public void Characters_Windows_forbids_and_device_names_are_escaped(string queryName, string fileName)
  {
    Assert.Equal(fileName, QueryFileNames.FileName(queryName));
    Assert.Equal(queryName, QueryFileNames.QueryName(fileName[..^2]));
  }

  [Fact]
  public void Control_characters_are_escaped()
  {
    Assert.Equal("a%01b.m", QueryFileNames.FileName("a\u0001b"));
  }

  [Theory]
  [InlineData("Sales%20Data")]  // a space, which is never escaped
  [InlineData("%41BC")]         // "ABC" isn't a device name
  [InlineData("50%")]           // no hex digits after it
  [InlineData("a%2fb")]         // lower-case hex isn't how pqdoc escapes
  public void Other_percent_signs_in_a_hand_made_file_name_are_kept(string fileName)
  {
    Assert.Equal(fileName, QueryFileNames.QueryName(fileName));
  }

  [Theory]
  [InlineData("%41BC")]
  [InlineData("Sales%2FReturns")]
  [InlineData("%")]
  [InlineData("NUL")]
  [InlineData("a%%2F")]
  public void Every_name_survives_the_round_trip(string queryName)
  {
    Assert.Equal(queryName, QueryFileNames.QueryName(QueryFileNames.FileName(queryName)[..^2]));
  }
}
