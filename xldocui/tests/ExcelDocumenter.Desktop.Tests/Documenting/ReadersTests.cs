using System.Text.RegularExpressions;
using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Tests.Documenting;

public sealed partial class ReadersTests
{
  [GeneratedRegex(@"\.[a-z]{3,4}\b")]
  private static partial Regex Extension();

  private static string[] ExtensionsIn(string? description) =>
    [.. Extension().Matches(description ?? "").Select(m => m.Value).Where(e => e.StartsWith(".xl", StringComparison.Ordinal)).Distinct().Order()];

  [Fact]
  public void The_workbooks_offered_to_vbadoc_are_the_ones_its_help_names()
  {
    Assert.Equal(ExtensionsIn(new VbaCli.CliDefinition().Workbook.Description), Readers.VbadocExtensions.Order());
  }

  [Fact]
  public void The_workbooks_offered_to_pqdoc_are_the_ones_its_help_names()
  {
    Assert.Equal(ExtensionsIn(new PqCli.CliDefinition().Input.Description), Readers.PqdocExtensions.Order());
  }

  [Fact]
  public void Every_tool_is_offered_until_something_is_chosen()
  {
    Assert.True(Readers.Reads(Tool.Vbadoc, ""));
    Assert.True(Readers.Reads(Tool.Pqdoc, ""));
  }

  [Fact]
  public void A_name_with_no_extension_is_taken_as_a_folder_unless_it_is_a_file()
  {
    using var folder = new TempFolder();
    var file = folder.Combine("Exported");
    File.WriteAllText(file, "not a folder");

    Assert.True(Readers.Reads(Tool.Pqdoc, folder.Combine("queries")));
    Assert.False(Readers.Reads(Tool.Pqdoc, file));
    Assert.False(Readers.Reads(Tool.Vbadoc, folder.Path));
  }

  [Fact]
  public void Why_a_tool_is_not_offered_is_said_plainly()
  {
    using var folder = new TempFolder();

    Assert.Equal("vbadoc doesn't read .xlsx files", Readers.WhyNot(Tool.Vbadoc, @"C:\Work\Sales.XLSX"));
    Assert.Equal("vbadoc reads a workbook, not a folder", Readers.WhyNot(Tool.Vbadoc, folder.Path));
    Assert.Equal("pqdoc doesn't read .xls files", Readers.WhyNot(Tool.Pqdoc, @"C:\Work\Sales.xls"));
  }
}
