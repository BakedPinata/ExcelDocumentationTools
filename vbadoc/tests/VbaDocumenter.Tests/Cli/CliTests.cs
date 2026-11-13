using System.CommandLine.Help;
using System.CommandLine.Invocation;
using CodeDocumenter.Shared.Files;
using VbaDocumenter.Cli;
using VbaDocumenter.Cli.Logging;

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
    Assert.Equal("MyBook-vba", options.OutputName);
    Assert.False(options.EmbedSource);
    Assert.Equal(LogVerbosity.Normal, options.Verbosity);
  }

  [Fact]
  public void The_page_is_named_after_the_workbook_unless_output_name_says_otherwise()
  {
    Assert.Equal("Sales_Q_1-vba", Bind(Path.GetFullPath("Sales#Q%1.xlsm")).OutputName); // # and % confuse a browser's address
    Assert.Equal("overview", Bind(Workbook, "--output-name", "overview").OutputName);
    Assert.Equal("overview", Bind(Workbook, "-n", "overview").OutputName);
  }

  [Theory]
  [InlineData("sub/page", "not a path")]
  [InlineData("page.html", "extension")]
  [InlineData("a#b", "can't be used")]
  [InlineData("50%", "can't be used")]
  [InlineData(" page", "space")]
  public void An_unsuitable_output_name_is_rejected(string name, string expected)
  {
    var error = Assert.Single(_cli.Parse([Workbook, "-n", name]).Errors);

    Assert.Contains(expected, error.Message);
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
  [InlineData(new string[0], OnExistsBehaviour.Prompt)]
  [InlineData(new[] { "--force" }, OnExistsBehaviour.Overwrite)]
  [InlineData(new[] { "-y" }, OnExistsBehaviour.Overwrite)]
  [InlineData(new[] { "--on-exists", "SKIP" }, OnExistsBehaviour.Skip)]
  [InlineData(new[] { "--on-exists", "fail" }, OnExistsBehaviour.Fail)]
  [InlineData(new[] { "--on-exists", "overwrite", "--force" }, OnExistsBehaviour.Overwrite)]
  public void A_page_that_would_change_is_asked_about_unless_the_options_say_otherwise(string[] options, OnExistsBehaviour expected)
  {
    Assert.Equal(expected, Bind([Workbook, .. options]).OnExists);
  }

  [Fact]
  public void Force_conflicts_with_any_other_on_exists()
  {
    var error = Assert.Single(_cli.Parse([Workbook, "-y", "--on-exists", "skip"]).Errors);

    Assert.Contains("--force (-y) means --on-exists overwrite, which conflicts with --on-exists skip", error.Message);
  }

  [Fact]
  public void An_unknown_on_exists_lists_the_valid_values()
  {
    var error = Assert.Single(_cli.Parse([Workbook, "--on-exists", "maybe"]).Errors);

    Assert.Equal("Invalid value 'maybe' for --on-exists. Valid values are: prompt, overwrite, skip, fail.", error.Message);
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
