using CodeDocumenter.Desktop.Documenting;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace CodeDocumenter.Desktop.Tests.Documenting;

public sealed class OptionHelpTests
{
  [Fact]
  public void The_default_is_read_from_the_tools_own_help()
  {
    Assert.Equal("a \"VBAProject\" folder next to the workbook", OptionHelp.Default(new VbaCli.CliDefinition().Output.Description));
    Assert.Equal("a PowerQuery folder beside a workbook, or the input folder", OptionHelp.Default(new PqCli.CliDefinition().Output.Description));
  }

  [Fact]
  public void Help_without_a_default_gives_none()
  {
    Assert.Null(OptionHelp.Default("Show warnings and errors only."));
    Assert.Null(OptionHelp.Default(null));
  }

  [Fact]
  public void The_rest_of_the_help_is_kept_for_the_tooltip()
  {
    Assert.Equal("Where to write the source files and page.", OptionHelp.WithoutDefault(new VbaCli.CliDefinition().Output.Description));
  }
}
