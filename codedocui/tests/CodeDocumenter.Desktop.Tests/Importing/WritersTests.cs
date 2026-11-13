using System.Text.RegularExpressions;
using CodeDocumenter.Desktop.Importing;
using CodeDocumenter.Desktop.Running;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace CodeDocumenter.Desktop.Tests.Importing;

public sealed partial class WritersTests
{
  [GeneratedRegex(@"\.xl[a-z]{1,2}\b")]
  private static partial Regex Extension();

  private static string[] ExtensionsIn(string? description) =>
    [.. Extension().Matches(description ?? "").Select(m => m.Value).Distinct().Order()];

  [Fact]
  public void The_target_picker_offers_add_ins_only_when_vbadoc_is_writing()
  {
    Assert.Equal("Workbooks and add-ins (*.xlsx;*.xlsm;*.xlsb;*.xlam)|*.xlsx;*.xlsm;*.xlsb;*.xlam|All files (*.*)|*.*", Writers.TargetFilter(Tool.Vbadoc));
    Assert.Equal("Workbooks (*.xlsx;*.xlsm;*.xlsb)|*.xlsx;*.xlsm;*.xlsb|All files (*.*)|*.*", Writers.TargetFilter(Tool.Pqdoc));
  }

  [Fact]
  public void The_new_files_offered_are_the_kinds_each_tools_help_says_it_builds()
  {
    Assert.Equal(ExtensionsIn(new VbaCli.CliDefinition().ImportTarget.New.Description), Writers.NewFiles(Tool.Vbadoc).Order());
    Assert.Equal(ExtensionsIn(new PqCli.CliDefinition().ImportTarget.New.Description), Writers.NewFiles(Tool.Pqdoc).Order());
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Each_filter_is_one_a_file_dialog_takes(string name)
  {
    var tool = Enum.Parse<Tool>(name);

    // A dialog refuses a filter that isn't pairs of a name and its patterns, when it's set rather than when it's shown.
    using var open = new OpenFileDialog { Filter = Writers.TargetFilter(tool) };
    using var save = new SaveFileDialog { Filter = Writers.NewFileFilter(tool) };

    Assert.Equal(Writers.TargetFilter(tool), open.Filter);
    Assert.Equal(Writers.NewFileFilter(tool), save.Filter);
  }

  [Fact]
  public void The_new_file_picker_offers_vbadocs_kinds_one_at_a_time_as_Save_As_does()
  {
    Assert.Equal("Macro-enabled workbooks (*.xlsm)|*.xlsm|Excel add-ins (*.xlam)|*.xlam", Writers.NewFileFilter(Tool.Vbadoc));
    Assert.Equal("Workbooks (*.xlsx;*.xlsm)|*.xlsx;*.xlsm", Writers.NewFileFilter(Tool.Pqdoc));
  }
}
