using System.Text;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Export;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Integration;

/// <summary>
/// Optional tests against a real PlantUML server. They are skipped unless a server URL is
/// configured, so the normal test run never needs the network (NFR-TEST-2).
/// </summary>
/// <remarks>
/// To run them, set <c>PLANTUML_SERVER_URL</c> in the environment, or put
/// <c>PLANTUML_SERVER_URL=...</c> in a <c>.env</c> file in the pqdoc folder (the file is
/// git-ignored). To run only the unit tests: <c>dotnet test --filter "Category!=Integration"</c>.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PlantUmlServerIntegrationTests
{
  private static async Task<Uri> ServerOrSkipAsync()
  {
    var configured = Environment.GetEnvironmentVariable(PlantUmlServerResolver.EnvironmentVariableName);

    // Walk up from the test output folder so a .env file in the pqdoc folder is found.
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); string.IsNullOrWhiteSpace(configured) && folder is not null; folder = folder.Parent)
    {
      var values = await DotEnvFile.LoadAsync(Path.Combine(folder.FullName, DotEnvFile.FileName), TestContext.Current.CancellationToken);
      values.TryGetValue(PlantUmlServerResolver.EnvironmentVariableName, out configured);
    }

    if (string.IsNullOrWhiteSpace(configured))
    {
      Assert.Skip("Set PLANTUML_SERVER_URL (environment variable or .env in the pqdoc folder) to run PlantUML server tests.");
    }

    if (!PlantUmlServerResolver.TryParseServerUrl(configured!, out var server))
    {
      Assert.Fail($"PLANTUML_SERVER_URL '{configured}' is not an absolute http(s) URL.");
    }

    return server!;
  }

  private static async Task<PlantUmlClient> ClientAsync(HttpClient httpClient) =>
    new(httpClient, await ServerOrSkipAsync(), TimeSpan.FromSeconds(60));

  [Theory]
  [InlineData(OutputFormat.Svg)]
  [InlineData(OutputFormat.Png)]
  [InlineData(OutputFormat.Pdf)]
  public async Task The_fixture_diagram_renders_without_errors(OutputFormat format)
  {
    using var httpClient = PlantUmlClient.CreateHttpClient();
    var client = await ClientAsync(httpClient);
    var diagram = PlantUmlDiagramBuilder.Build(await Fixtures.GraphAsync(), "Integration test");

    var rendered = await client.RenderAsync(diagram, format, TestContext.Current.CancellationToken);

    Assert.True(PlantUmlClient.LooksLike(format, rendered));
    if (format == OutputFormat.Svg)
    {
      var svg = Encoding.UTF8.GetString(rendered);
      Assert.DoesNotContain("Syntax Error", svg, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("Revenue &amp; Margin (2024)", svg, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task A_large_graph_is_sent_by_POST_and_renders()
  {
    using var httpClient = PlantUmlClient.CreateHttpClient();
    var client = await ClientAsync(httpClient);
    var edges = Enumerable.Range(0, 150).Select(i => $"Query number {i}>Query number {i + 1}").ToArray();
    var diagram = PlantUmlDiagramBuilder.Build(Fixtures.Graph(edges), "Large graph");
    using var request = client.CreateRequest(diagram, OutputFormat.Svg);
    Assert.Equal(HttpMethod.Post, request.Method);

    var rendered = await client.RenderAsync(diagram, OutputFormat.Svg, TestContext.Current.CancellationToken);

    Assert.Contains("Query number 150", Encoding.UTF8.GetString(rendered), StringComparison.Ordinal);
  }
}
