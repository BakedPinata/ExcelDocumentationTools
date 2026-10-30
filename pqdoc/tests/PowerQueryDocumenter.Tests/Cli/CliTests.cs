using System.CommandLine.Help;
using System.CommandLine.Invocation;
using PowerQueryDocumenter.Cli;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Cli;

public sealed class CliDefinitionTests
{
  private readonly CliDefinition cli = new();

  private string[] Errors(params string[] args) => cli.Parse(args).Errors.Select(e => e.Message).ToArray();

  [Fact]
  public void A_folder_alone_is_valid_and_uses_the_defaults()
  {
    var result = cli.Parse(["C:\\queries"]);

    Assert.Empty(result.Errors);
    Assert.Equal("C:\\queries", result.GetValue(cli.InputFolder));
    Assert.Equal("powerquery-dependencies", result.GetValue(cli.OutputName));
    Assert.Empty(result.GetValue(cli.Formats) ?? []);
    Assert.False(result.GetValue(cli.NoCsv));
    Assert.Null(result.GetValue(cli.OnExists));
  }

  [Fact]
  public void The_input_folder_is_required()
  {
    Assert.NotEmpty(Errors());
  }

  [Theory]
  [InlineData("-f", "puml,html")]
  [InlineData("--format", "PUML,Html")]
  [InlineData("-f", " puml , html ")]
  public void Formats_accept_comma_separated_values_in_any_case(string option, string value)
  {
    var result = cli.Parse(["in", option, value]);

    Assert.Empty(result.Errors);
    Assert.Equal(new[] { OutputFormat.Puml, OutputFormat.Html }, result.GetValue(cli.Formats));
  }

  [Fact]
  public void Formats_can_be_repeated_and_duplicates_are_removed()
  {
    var result = cli.Parse(["in", "-f", "puml", "-f", "html,puml", "--format", "svg", "--plantuml-server", "https://p.example.com"]);

    Assert.Empty(result.Errors);
    Assert.Equal(new[] { OutputFormat.Puml, OutputFormat.Html, OutputFormat.Svg }, result.GetValue(cli.Formats));
  }

  [Theory]
  [InlineData("jpeg")]
  [InlineData("puml,gif")]
  public void Unknown_formats_are_rejected_with_the_valid_list(string value)
  {
    var error = Assert.Single(Errors("in", "-f", value));

    Assert.Contains("--format", error, StringComparison.Ordinal);
    Assert.Contains("puml, html, pdf, png, svg", error, StringComparison.Ordinal);
  }

  [Fact]
  public void Asking_for_csv_explains_that_it_is_on_by_default()
  {
    var error = Assert.Single(Errors("in", "-f", "csv"));

    Assert.Contains("--no-csv", error, StringComparison.Ordinal);
  }

  [Fact]
  public void No_csv_without_a_format_is_rejected()
  {
    var error = Assert.Single(Errors("in", "--no-csv"));

    Assert.Contains("--no-csv", error, StringComparison.Ordinal);
    Assert.Contains("nothing would be produced", error, StringComparison.Ordinal);
  }

  [Fact]
  public void No_csv_with_a_format_is_valid()
  {
    Assert.Empty(Errors("in", "--no-csv", "-f", "html"));
  }

  [Theory]
  [InlineData("sub/name")]
  [InlineData("sub\\name")]
  [InlineData("report.csv")]
  [InlineData("model.v2")]
  [InlineData("bad:name")]
  [InlineData("bad*name")]
  [InlineData("trailing.")]
  [InlineData(" padded")]
  public void Invalid_output_names_are_rejected(string name)
  {
    var error = Assert.Single(Errors("in", "-n", name));

    Assert.Contains("--output-name", error, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("sales-model")]
  [InlineData("Sales Model 2024")]
  [InlineData("model_v2")]
  public void Plain_output_names_are_accepted(string name)
  {
    Assert.Null(CliDefinition.ValidateOutputName(name));
    Assert.Empty(Errors("in", "--output-name", name));
  }

  [Theory]
  [InlineData("prompt", OnExistsBehaviour.Prompt)]
  [InlineData("OVERWRITE", OnExistsBehaviour.Overwrite)]
  [InlineData("skip", OnExistsBehaviour.Skip)]
  [InlineData("Fail", OnExistsBehaviour.Fail)]
  public void On_exists_values_are_case_insensitive(string value, OnExistsBehaviour expected)
  {
    var result = cli.Parse(["in", "--on-exists", value]);

    Assert.Empty(result.Errors);
    Assert.Equal(expected, result.GetValue(cli.OnExists));
  }

  [Theory]
  [InlineData("sometimes")]
  [InlineData("1")]
  public void Invalid_on_exists_values_are_rejected(string value)
  {
    var error = Assert.Single(Errors("in", "--on-exists", value));

    Assert.Contains("--on-exists", error, StringComparison.Ordinal);
    Assert.Contains("prompt, overwrite, skip, fail", error, StringComparison.Ordinal);
  }

  [Fact]
  public void Force_conflicting_with_on_exists_is_rejected()
  {
    var error = Assert.Single(Errors("in", "-y", "--on-exists", "skip"));

    Assert.Contains("--force", error, StringComparison.Ordinal);
    Assert.Contains("--on-exists skip", error, StringComparison.Ordinal);
  }

  [Fact]
  public void Force_agreeing_with_on_exists_is_accepted()
  {
    Assert.Empty(Errors("in", "--force", "--on-exists", "overwrite"));
  }

  [Fact]
  public void Verbose_and_quiet_together_are_rejected()
  {
    var error = Assert.Single(Errors("in", "-v", "-q"));

    Assert.Contains("--verbose", error, StringComparison.Ordinal);
    Assert.Contains("--quiet", error, StringComparison.Ordinal);
  }

  [Fact]
  public void Several_problems_are_all_reported()
  {
    Assert.Equal(3, Errors("in", "--no-csv", "-v", "-q", "-n", "a/b").Length);
  }

  [Fact]
  public void Unknown_arguments_are_rejected()
  {
    Assert.NotEmpty(Errors("in", "--colour"));
  }

  [Fact]
  public void Every_option_has_a_long_name_and_the_usual_ones_have_aliases()
  {
    Assert.Contains("-n", cli.OutputName.Aliases);
    Assert.Contains("-o", cli.Output.Aliases);
    Assert.Contains("-f", cli.Formats.Aliases);
    Assert.Contains("-y", cli.Force.Aliases);
    Assert.Contains("-v", cli.Verbose.Aliases);
    Assert.Contains("-q", cli.Quiet.Aliases);
    Assert.All(cli.RootCommand.Options, option => Assert.StartsWith("--", option.Name));
  }

  [Fact]
  public void Help_and_version_are_generated()
  {
    Assert.IsType<HelpAction>(cli.Parse(["--help"]).Action);
    Assert.NotNull(cli.Parse(["--version"]).Action);
    Assert.IsNotType<ParseErrorAction>(cli.Parse(["--version"]).Action);
  }
}

public sealed class OptionsBinderTests : IDisposable
{
  private readonly TempDirectory temp = new();
  private readonly CliDefinition cli = new();

  public void Dispose() => temp.Dispose();

  private async Task<BindingResult> BindWith(Dictionary<string, string>? variables, params string[] args)
  {
    var parsed = cli.Parse(args);
    Assert.Empty(parsed.Errors);
    return await OptionsBinder.BindAsync(parsed, cli, new FakeEnvironment(temp.Path, variables), TestContext.Current.CancellationToken);
  }

  private Task<BindingResult> Bind(params string[] args) => BindWith(null, args);

  [Fact]
  public async Task Defaults_write_a_csv_next_to_the_inputs()
  {
    var options = (await Bind("queries")).Options!;

    Assert.Equal(temp.Combine("queries"), options.InputFolder);
    Assert.Equal(options.InputFolder, options.OutputFolder);
    Assert.Equal("powerquery-dependencies", options.OutputName);
    Assert.Equal(new[] { OutputFormat.Csv }, options.Formats);
    Assert.Equal(OnExistsBehaviour.Prompt, options.OnExists);
    Assert.Equal(LogVerbosity.Normal, options.Verbosity);
    Assert.Null(options.PlantUmlServer);
    Assert.Equal(TimeSpan.FromSeconds(30), options.RenderTimeout);
    Assert.Equal(temp.Combine("queries", "powerquery-dependencies.csv"), options.GetOutputPath(OutputFormat.Csv));
  }

  [Fact]
  public async Task Relative_paths_are_resolved_against_the_current_directory()
  {
    var options = (await Bind("in", "-o", "out/docs", "-n", "model")).Options!;

    Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, "out", "docs")), options.OutputFolder);
    Assert.Equal(Path.Combine(options.OutputFolder, "model.puml"), options.GetOutputPath(OutputFormat.Puml));
  }

  [Fact]
  public async Task Formats_are_additive_and_ordered()
  {
    var options = (await Bind("in", "-f", "html,puml")).Options!;

    Assert.Equal(new[] { OutputFormat.Csv, OutputFormat.Puml, OutputFormat.Html }, options.Formats);
  }

  [Fact]
  public async Task No_csv_removes_the_csv()
  {
    var options = (await Bind("in", "--no-csv", "-f", "html")).Options!;

    Assert.Equal(new[] { OutputFormat.Html }, options.Formats);
  }

  [Theory]
  [InlineData("-y", OnExistsBehaviour.Overwrite)]
  [InlineData("--force", OnExistsBehaviour.Overwrite)]
  public async Task Force_means_overwrite(string flag, OnExistsBehaviour expected)
  {
    Assert.Equal(expected, (await Bind("in", flag)).Options!.OnExists);
  }

  [Theory]
  [InlineData("-v", LogVerbosity.Verbose)]
  [InlineData("-q", LogVerbosity.Quiet)]
  public async Task Verbosity_flags_are_bound(string flag, LogVerbosity expected)
  {
    Assert.Equal(expected, (await Bind("in", flag)).Options!.Verbosity);
  }

  [Fact]
  public async Task Rendered_formats_without_a_server_are_a_configuration_error()
  {
    var result = await Bind("in", "-f", "png");

    Assert.Null(result.Options);
    Assert.Equal(PlantUmlServerResolver.MissingServerMessage, Assert.Single(result.Errors));
  }

  [Fact]
  public async Task Rendered_formats_use_the_server_from_the_environment()
  {
    var options = (await BindWith(new() { ["PLANTUML_SERVER_URL"] = "https://env.example.com/plantuml" }, "in", "-f", "svg")).Options!;

    Assert.Equal(new Uri("https://env.example.com/plantuml"), options.PlantUmlServer);
  }

  [Fact]
  public async Task Rendered_formats_use_the_server_from_a_dotenv_file()
  {
    temp.WriteFile(".env", "PLANTUML_SERVER_URL=http://localhost:8080");

    var options = (await Bind("in", "-f", "pdf")).Options!;

    Assert.Equal(new Uri("http://localhost:8080"), options.PlantUmlServer);
  }

  [Fact]
  public async Task An_explicit_invalid_server_is_rejected_even_without_rendered_formats()
  {
    var result = await Bind("in", "--plantuml-server", "plantuml.example.com");

    Assert.Null(result.Options);
    Assert.Contains("--plantuml-server", Assert.Single(result.Errors), StringComparison.Ordinal);
  }

  [Fact]
  public async Task The_environment_is_not_consulted_when_no_server_is_needed()
  {
    var result = await BindWith(new() { ["PLANTUML_SERVER_URL"] = "not a url" }, "in", "-f", "html");

    Assert.Empty(result.Errors);
    Assert.Null(result.Options!.PlantUmlServer);
  }
}
