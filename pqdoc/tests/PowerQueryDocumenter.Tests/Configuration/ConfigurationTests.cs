using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Logging;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Configuration;

public sealed class DotEnvFileTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  [Fact]
  public void Parses_keys_values_comments_and_blank_lines()
  {
    var values = DotEnvFile.Parse("# PlantUML\n\nPLANTUML_SERVER_URL=http://localhost:8080\r\nOTHER = spaced value \n");

    Assert.Equal("http://localhost:8080", values["PLANTUML_SERVER_URL"]);
    Assert.Equal("spaced value", values["OTHER"]);
    Assert.Equal(2, values.Count);
  }

  [Theory]
  [InlineData("KEY=\"quoted # not a comment\"", "quoted # not a comment")]
  [InlineData("KEY='single'", "single")]
  [InlineData("export KEY=value", "value")]
  [InlineData("KEY=value # trailing comment", "value")]
  [InlineData("KEY=a=b", "a=b")]
  [InlineData("KEY=", "")]
  public void Handles_common_value_forms(string line, string expected)
  {
    Assert.Equal(expected, DotEnvFile.Parse(line)["KEY"]);
  }

  [Fact]
  public void Later_values_override_earlier_ones()
  {
    Assert.Equal("second", DotEnvFile.Parse("KEY=first\nKEY=second")["KEY"]);
  }

  [Fact]
  public void Lines_without_a_key_are_ignored()
  {
    Assert.Empty(DotEnvFile.Parse("=value\njust text\n"));
  }

  [Fact]
  public async Task A_missing_file_has_no_values()
  {
    Assert.Empty(await DotEnvFile.LoadAsync(temp.Combine(".env"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Loads_a_file_from_disk()
  {
    var path = temp.WriteFile(".env", "PLANTUML_SERVER_URL=https://plantuml.example.com");

    var values = await DotEnvFile.LoadAsync(path, TestContext.Current.CancellationToken);

    Assert.Equal("https://plantuml.example.com", values["PLANTUML_SERVER_URL"]);
  }
}

public sealed class PlantUmlServerResolverTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private FakeEnvironment Environment(string? variable = null) =>
    new(temp.Path, variable is null ? null : new() { [PlantUmlServerResolver.EnvironmentVariableName] = variable });

  private static Task<PlantUmlServerResolution> Resolve(string? commandLineValue, FakeEnvironment environment) =>
    PlantUmlServerResolver.ResolveAsync(commandLineValue, environment, TestContext.Current.CancellationToken);

  [Fact]
  public async Task The_command_line_wins_over_the_environment_and_dotenv()
  {
    temp.WriteFile(".env", "PLANTUML_SERVER_URL=https://dotenv.example.com");

    var resolution = await Resolve("https://cli.example.com", Environment("https://env.example.com"));

    Assert.Equal(new Uri("https://cli.example.com"), resolution.Server);
    Assert.Equal(PlantUmlServerSource.CommandLine, resolution.Source);
  }

  [Fact]
  public async Task The_environment_wins_over_dotenv()
  {
    temp.WriteFile(".env", "PLANTUML_SERVER_URL=https://dotenv.example.com");

    var resolution = await Resolve(null, Environment("https://env.example.com"));

    Assert.Equal(new Uri("https://env.example.com"), resolution.Server);
    Assert.Equal(PlantUmlServerSource.EnvironmentVariable, resolution.Source);
  }

  [Fact]
  public async Task Dotenv_in_the_current_directory_is_used_last()
  {
    temp.WriteFile(".env", "PLANTUML_SERVER_URL=http://localhost:8080/plantuml");

    var resolution = await Resolve(" ", Environment());

    Assert.Equal(new Uri("http://localhost:8080/plantuml"), resolution.Server);
    Assert.Equal(PlantUmlServerSource.DotEnvFile, resolution.Source);
    Assert.Null(resolution.Error);
  }

  [Fact]
  public async Task Nothing_configured_resolves_to_no_server_without_an_error()
  {
    var resolution = await Resolve(null, Environment());

    Assert.Null(resolution.Server);
    Assert.Null(resolution.Error);
    Assert.Equal(PlantUmlServerSource.None, resolution.Source);
  }

  [Fact]
  public async Task An_invalid_higher_priority_value_is_an_error_not_a_fallback()
  {
    var resolution = await Resolve("not a url", Environment("https://env.example.com"));

    Assert.Null(resolution.Server);
    Assert.NotNull(resolution.Error);
    Assert.Contains("--plantuml-server", resolution.Error, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("https://plantuml.example.com", true)]
  [InlineData("http://localhost:8080/plantuml/", true)]
  [InlineData("HTTPS://PLANTUML.EXAMPLE.COM/a/b", true)]
  [InlineData("ftp://plantuml.example.com", false)]
  [InlineData("plantuml.example.com", false)]
  [InlineData("localhost:8080", false)]
  [InlineData("/plantuml", false)]
  [InlineData("https://plantuml.example.com/?x=1", false)]
  [InlineData("https://plantuml.example.com/#top", false)]
  [InlineData("file:///c:/plantuml", false)]
  public void Only_absolute_http_and_https_URLs_are_accepted(string value, bool valid)
  {
    Assert.Equal(valid, PlantUmlServerResolver.TryParseServerUrl(value, out _));
  }

  [Fact]
  public void The_missing_server_message_names_all_three_routes()
  {
    var message = PlantUmlServerResolver.MissingServerMessage;

    Assert.Contains("--plantuml-server", message, StringComparison.Ordinal);
    Assert.Contains("PLANTUML_SERVER_URL environment variable", message, StringComparison.Ordinal);
    Assert.Contains(".env file", message, StringComparison.Ordinal);
  }
}

public sealed class LoggingSetupTests
{
  [Theory]
  [InlineData(LogVerbosity.Normal, LogEventLevel.Information)]
  [InlineData(LogVerbosity.Verbose, LogEventLevel.Debug)]
  [InlineData(LogVerbosity.Quiet, LogEventLevel.Warning)]
  public void Verbosity_maps_to_a_minimum_level(LogVerbosity verbosity, LogEventLevel expected)
  {
    Assert.Equal(expected, LoggingSetup.MinimumLevel(verbosity));
  }

  [Theory]
  [InlineData(null, false, true)]
  [InlineData("", false, true)]
  [InlineData("1", false, false)]
  [InlineData(null, true, false)]
  public void Colour_respects_NO_COLOR_and_redirection(string? noColour, bool redirected, bool expected)
  {
    var environment = new FakeEnvironment(".", noColour is null ? null : new() { ["NO_COLOR"] = noColour });

    Assert.Equal(expected, LoggingSetup.UseColour(environment, redirected));
  }

  [Fact]
  public void Templates_show_the_level_and_message_and_only_verbose_shows_time()
  {
    Assert.Contains("{Level:u3}", LoggingSetup.OutputTemplate, StringComparison.Ordinal);
    Assert.DoesNotContain("{Timestamp", LoggingSetup.OutputTemplate, StringComparison.Ordinal);
    Assert.Contains("{Timestamp", LoggingSetup.VerboseOutputTemplate, StringComparison.Ordinal);
  }
}
