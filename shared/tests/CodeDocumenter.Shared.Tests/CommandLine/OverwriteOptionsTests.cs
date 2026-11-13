using System.CommandLine;
using CodeDocumenter.Shared.CommandLine;
using CodeDocumenter.Shared.Files;

namespace CodeDocumenter.Shared.Tests.CommandLine;

public sealed class OverwriteOptionsTests
{
  private readonly OverwriteOptions options = new("the page", "an existing page");

  private ParseResult Parse(params string[] args)
  {
    var command = new RootCommand { options.OnExists, options.Force };
    command.Validators.Add(options.Validate);
    return command.Parse(args);
  }

  [Theory]
  [InlineData(new string[0], OnExistsBehaviour.Prompt)]
  [InlineData(new[] { "--force" }, OnExistsBehaviour.Overwrite)]
  [InlineData(new[] { "-y" }, OnExistsBehaviour.Overwrite)]
  [InlineData(new[] { "--on-exists", "Skip" }, OnExistsBehaviour.Skip)]
  [InlineData(new[] { "--on-exists", "FAIL" }, OnExistsBehaviour.Fail)]
  [InlineData(new[] { "--on-exists", "overwrite", "-y" }, OnExistsBehaviour.Overwrite)]
  public void The_default_is_to_ask_and_force_means_overwrite(string[] args, OnExistsBehaviour expected)
  {
    var result = Parse(args);

    Assert.Empty(result.Errors);
    Assert.Equal(expected, options.Bind(result));
  }

  [Theory]
  [InlineData("skip")]
  [InlineData("fail")]
  [InlineData("prompt")]
  public void Force_conflicts_with_any_other_behaviour(string behaviour)
  {
    var error = Assert.Single(Parse("--force", "--on-exists", behaviour).Errors);

    Assert.Equal(
      $"--force (-y) means --on-exists overwrite, which conflicts with --on-exists {behaviour}. Use only one of them.",
      error.Message);
  }

  [Theory]
  [InlineData("maybe")]
  [InlineData("1")]
  public void Unknown_values_and_numbers_are_rejected(string value)
  {
    var error = Assert.Single(Parse("--on-exists", value).Errors);

    Assert.Equal($"Invalid value '{value}' for --on-exists. Valid values are: prompt, overwrite, skip, fail.", error.Message);
  }

  [Fact]
  public void The_help_names_what_may_already_exist()
  {
    Assert.StartsWith("What to do when the page already exists:", options.OnExists.Description, StringComparison.Ordinal);
    Assert.StartsWith("Overwrite an existing page without asking.", options.Force.Description, StringComparison.Ordinal);
  }
}
