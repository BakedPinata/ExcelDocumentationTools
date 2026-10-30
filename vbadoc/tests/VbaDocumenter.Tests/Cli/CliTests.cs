using System.CommandLine.Help;
using System.CommandLine.Invocation;
using VbaDocumenter.Cli;
using VbaDocumenter.Logging;

namespace VbaDocumenter.Tests.Cli;

public class CliTests
{
  static readonly string Workbook = Path.GetFullPath(Path.Combine("books", "MyBook.xlsm"));

  readonly CliDefinition _cli = new();

  CommandLineOptions Bind(params string[] args)
  {
    var parseResult = _cli.Parse(args);
    Assert.Empty(parseResult.Errors);
    return OptionsBinder.Bind(parseResult, _cli).Options!;
  }

  [Fact]
  public void Defaults_to_a_VBAProject_folder_next_to_the_workbook()
  {
    var options = Bind(Workbook);

    Assert.Equal(Path.Combine(Path.GetDirectoryName(Workbook)!, "VBAProject"), options.OutputFolder);
    Assert.False(options.EmbedSource);
    Assert.Equal(LogVerbosity.Normal, options.Verbosity);
  }

  [Fact]
  public void Accepts_options_in_any_order()
  {
    var options = Bind("--embed-source", Workbook, "--verbose", "--output", "elsewhere");

    Assert.Equal(Path.GetFullPath("elsewhere"), options.OutputFolder);
    Assert.True(options.EmbedSource);
    Assert.Equal(LogVerbosity.Verbose, options.Verbosity);
  }

  [Fact]
  public void The_shared_options_have_their_short_names()
  {
    var options = Bind(Workbook, "-o", "elsewhere", "-q");

    Assert.Equal(Path.GetFullPath("elsewhere"), options.OutputFolder);
    Assert.Equal(LogVerbosity.Quiet, options.Verbosity);
    Assert.Equal(LogVerbosity.Verbose, Bind(Workbook, "-v").Verbosity);
  }

  [Theory]
  [InlineData("--help")]
  [InlineData("-h")]
  [InlineData("-?")]
  public void Help_has_its_usual_names(string name)
  {
    Assert.IsType<HelpAction>(_cli.Parse([name]).Action);
  }

  public static TheoryData<string[]> InvalidArguments => new()
  {
    Array.Empty<string>(),
    new[] { "a.xlsm", "--output" },
    new[] { "a.xlsm", "--bogus" },
    new[] { "a.xlsm", "b.xlsm" },
  };

  [Theory]
  [MemberData(nameof(InvalidArguments))]
  public void Rejects_invalid_arguments(string[] args)
  {
    Assert.NotEmpty(_cli.Parse(args).Errors);
  }

  [Fact]
  public void Verbose_and_quiet_cannot_be_used_together()
  {
    var error = Assert.Single(_cli.Parse([Workbook, "--verbose", "--quiet"]).Errors);

    Assert.Contains("cannot be used together", error.Message);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void An_empty_workbook_path_is_an_error_rather_than_a_crash(string workbook)
  {
    var binding = OptionsBinder.Bind(_cli.Parse([workbook]), _cli);

    Assert.Null(binding.Options);
    Assert.Contains("workbook must not be empty.", binding.Errors);
  }
}
