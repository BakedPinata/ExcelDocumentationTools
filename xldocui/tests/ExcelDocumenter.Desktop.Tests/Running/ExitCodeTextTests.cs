using System.Reflection;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared;

namespace ExcelDocumenter.Desktop.Tests.Running;

public sealed class ExitCodeTextTests
{
  public static TheoryData<int> EveryExitCode => [.. typeof(ExitCodes)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.IsLiteral)
    .Select(f => (int)f.GetRawConstantValue()!)];

  [Theory]
  [MemberData(nameof(EveryExitCode))]
  public void Every_exit_code_the_tools_use_is_said_in_words_with_its_number(int exitCode)
  {
    var text = ExitCodeText.Describe(exitCode);

    Assert.DoesNotContain("Finished.", text, StringComparison.Ordinal);
    Assert.EndsWith($"(exit code {exitCode})", text, StringComparison.Ordinal);
  }

  [Fact]
  public void An_exit_code_no_tool_uses_still_gives_its_number()
  {
    Assert.Equal("Finished. (exit code 42)", ExitCodeText.Describe(42));
  }
}
