using PowerQueryDocumenter.Core.Analysis;

namespace PowerQueryDocumenter.Tests.Analysis;

/// <summary>Telling a function from its code, for a folder of .m files (FR-HTML-11).</summary>
public sealed class FunctionDetectorTests
{
  [Theory]
  [InlineData("(x) => x + 1")]
  [InlineData("(input as nullable text) as nullable text =>\r\n    let\r\n        Trimmed = Text.Trim(input)\r\n    in\r\n        Trimmed")]
  [InlineData("(t as text) as text => Text.Upper(t)")]
  [InlineData("// Cleans text\r\n(t) => Text.Trim(t)")]
  [InlineData("each _ * 2")]
  [InlineData("let\r\n    Source = (x as number) => x * 2\r\nin\r\n    Source")]
  [InlineData("let\r\n    Helper = 1,\r\n    Fn = (x) => let y = x in y\r\nin\r\n    Fn")]
  [InlineData("() => DateTime.LocalNow()")]
  public void Functions_are_recognised(string expression)
  {
    Assert.True(FunctionDetector.IsFunction(expression));
  }

  [Theory]
  [InlineData("")]
  [InlineData("1 + 2")]
  [InlineData("(1 + 2) * 3")]
  [InlineData("let\r\n    Source = Excel.CurrentWorkbook()\r\nin\r\n    Source")]
  [InlineData("let\r\n    Fn = (x) => x,\r\n    Result = Fn(1)\r\nin\r\n    Result")]
  [InlineData("let\r\n    Fn = (x) => x\r\nin\r\n    Fn(1)")]
  [InlineData("[A = (x) => x]")]
  [InlineData("let\r\n    Inner = let Fn = (x) => x in Fn\r\nin\r\n    Missing")]
  [InlineData("(x) as table")]
  public void Anything_else_is_not(string expression)
  {
    Assert.False(FunctionDetector.IsFunction(expression));
  }
}
