using System.Runtime.InteropServices;
using ExcelDocumenter.Desktop.Running;

namespace ExcelDocumenter.Desktop.Tests.Running;

public sealed class CommandLineTextTests
{
  [Theory]
  [InlineData(@"C:\Work\Sales.xlsm", @"C:\Work\Sales.xlsm")]
  [InlineData("--embed-source", "--embed-source")]
  [InlineData(@"C:\Work\Sales report.xlsm", "\"C:\\Work\\Sales report.xlsm\"")]
  [InlineData(@"C:\Docs\", @"C:\Docs\")]
  [InlineData(@"C:\Sales report\Docs\", "\"C:\\Sales report\\Docs\\\\\"")]   // the backslash would otherwise escape the closing quote
  [InlineData("html,csv", "\"html,csv\"")]         // PowerShell reads an unquoted comma as a list
  [InlineData("Sales & Co", "\"Sales & Co\"")]
  [InlineData("", "\"\"")]
  [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
  public void An_argument_is_quoted_only_when_it_needs_to_be(string argument, string written)
  {
    Assert.Equal(written, CommandLineText.Quote(argument));
  }

  [Fact]
  public void Windows_reads_back_exactly_the_arguments_written()
  {
    string[] arguments =
    [
      @"C:\Work\Sales report.xlsm", "--output", @"C:\My Docs\", "--output-name", "Sales", "--format", "html,csv", "",
      "a \"quoted\" word", @"back\\slashes\\", "C:\\",
    ];

    var line = CommandLineText.Format("vbadoc", arguments);

    Assert.Equal(["vbadoc", .. arguments], Split(line));
  }

  // How Windows splits a command line into arguments, as a .NET program reads them.
  private static string[] Split(string commandLine)
  {
    var pointer = CommandLineToArgvW(commandLine, out var count);
    Assert.NotEqual(IntPtr.Zero, pointer);
    try
    {
      return [.. Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!)];
    }
    finally
    {
      LocalFree(pointer);
    }
  }

  [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

  [DllImport("kernel32.dll")]
  private static extern IntPtr LocalFree(IntPtr memory);
}
