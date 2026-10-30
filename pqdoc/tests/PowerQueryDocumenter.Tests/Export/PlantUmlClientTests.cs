using System.Net;
using System.Text;
using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Export;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class PlantUmlClientTests
{
  private const string SmallDiagram = "@startuml\nA --> B\n@enduml\n";
  private static readonly Uri Server = new("https://plantuml.example.com/plantuml/");

  private static PlantUmlClient Client(StubHttpMessageHandler handler, TimeSpan? timeout = null) =>
    new(new HttpClient(handler), Server, timeout ?? TimeSpan.FromSeconds(30));

  private static string LargeDiagram() =>
    "@startuml\n" + string.Concat(Enumerable.Range(0, 400).Select(i => $"rectangle \"Query {Guid.NewGuid()}\" as q{i}\n")) + "@enduml\n";

  [Fact]
  public void The_user_agent_names_the_tool_and_its_version()
  {
    using var client = PlantUmlClient.CreateHttpClient();

    var product = Assert.Single(client.DefaultRequestHeaders.UserAgent).Product;

    Assert.Equal("pqdoc", product?.Name);
    Assert.Matches(@"^\d+\.\d+\.\d+$", product?.Version);
  }

  [Theory]
  [InlineData(OutputFormat.Svg, "svg")]
  [InlineData(OutputFormat.Png, "png")]
  [InlineData(OutputFormat.Pdf, "pdf")]
  public async Task Small_diagrams_use_GET_with_the_encoded_text_in_the_path(OutputFormat format, string segment)
  {
    var handler = StubHttpMessageHandler.WorkingServer();

    await Client(handler).RenderAsync(SmallDiagram, format, TestContext.Current.CancellationToken);

    var request = Assert.Single(handler.Requests);
    Assert.Equal(HttpMethod.Get, request.Method);
    var prefix = $"https://plantuml.example.com/plantuml/{segment}/";
    Assert.StartsWith(prefix, request.Uri.AbsoluteUri);
    Assert.Equal(SmallDiagram, PlantUmlEncoder.Decode(request.Uri.AbsoluteUri[prefix.Length..]));
  }

  [Fact]
  public async Task Large_diagrams_use_POST_with_the_plain_text_body()
  {
    var handler = StubHttpMessageHandler.WorkingServer();
    var diagram = LargeDiagram();

    await Client(handler).RenderAsync(diagram, OutputFormat.Png, TestContext.Current.CancellationToken);

    var request = Assert.Single(handler.Requests);
    Assert.Equal(HttpMethod.Post, request.Method);
    Assert.Equal("https://plantuml.example.com/plantuml/png", request.Uri.AbsoluteUri);
    Assert.Equal(diagram, request.Body);
  }

  [Fact]
  public void The_GET_URL_never_exceeds_the_limit()
  {
    using var request = Client(StubHttpMessageHandler.Unused()).CreateRequest(LargeDiagram(), OutputFormat.Svg);

    Assert.Equal(HttpMethod.Post, request.Method);
    Assert.InRange(request.RequestUri!.AbsoluteUri.Length, 1, PlantUmlClient.MaxGetUrlLength);
  }

  [Fact]
  public async Task Returns_the_rendered_bytes()
  {
    var bytes = await Client(StubHttpMessageHandler.WorkingServer())
      .RenderAsync(SmallDiagram, OutputFormat.Png, TestContext.Current.CancellationToken);

    Assert.Equal(FakePlantUml.Png, bytes);
  }

  [Fact]
  public async Task A_non_success_status_is_reported()
  {
    var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Svg, TestContext.Current.CancellationToken));

    Assert.Contains("503", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_diagram_error_reported_by_the_server_is_shown()
  {
    var handler = new StubHttpMessageHandler((_, _) =>
    {
      var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ByteArrayContent(FakePlantUml.Png) };
      response.Headers.Add(PlantUmlClient.DiagramErrorHeader, "Syntax Error?");
      response.Headers.Add(PlantUmlClient.DiagramErrorLineHeader, "7");
      return Task.FromResult(response);
    });

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Png, TestContext.Current.CancellationToken));

    Assert.Contains("line 7", exception.Message, StringComparison.Ordinal);
    Assert.Contains("Syntax Error?", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task An_empty_body_is_reported()
  {
    var handler = new StubHttpMessageHandler((_, _) =>
      Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }));

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Svg, TestContext.Current.CancellationToken));

    Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Content_that_is_not_the_requested_format_is_reported()
  {
    var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("<html><body>Proxy login</body></html>", Encoding.UTF8, "text/html"),
    }));

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Png, TestContext.Current.CancellationToken));

    Assert.Contains("not PNG", exception.Message, StringComparison.Ordinal);
    Assert.Contains("text/html", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_slow_server_times_out()
  {
    var handler = new StubHttpMessageHandler(async (_, token) =>
    {
      await Task.Delay(Timeout.InfiniteTimeSpan, token);
      return new HttpResponseMessage(HttpStatusCode.OK);
    });

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler, TimeSpan.FromMilliseconds(100)).RenderAsync(SmallDiagram, OutputFormat.Svg, TestContext.Current.CancellationToken));

    Assert.Contains("did not respond within", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_network_failure_is_reported()
  {
    var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No such host is known."));

    var exception = await Assert.ThrowsAsync<PlantUmlRenderException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Svg, TestContext.Current.CancellationToken));

    Assert.Contains("could not reach", exception.Message, StringComparison.Ordinal);
    Assert.Contains("No such host", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Cancellation_by_the_caller_is_not_reported_as_a_timeout()
  {
    using var cancellation = new CancellationTokenSource();
    var handler = new StubHttpMessageHandler(async (_, token) =>
    {
      await cancellation.CancelAsync();
      await Task.Delay(Timeout.InfiniteTimeSpan, token);
      return new HttpResponseMessage(HttpStatusCode.OK);
    });

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Client(handler).RenderAsync(SmallDiagram, OutputFormat.Svg, cancellation.Token));
  }

  [Fact]
  public void Only_rendered_formats_are_accepted()
  {
    var client = Client(StubHttpMessageHandler.Unused());

    Assert.Throws<ArgumentOutOfRangeException>(() => client.CreateRequest(SmallDiagram, OutputFormat.Csv));
    Assert.Throws<ArgumentOutOfRangeException>(() => new PlantUmlRenderExporter(OutputFormat.Html, client));
  }

  [Fact]
  public void The_timeout_must_be_positive()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new PlantUmlClient(new HttpClient(StubHttpMessageHandler.Unused()), Server, TimeSpan.Zero));
  }

  [Theory]
  [InlineData(OutputFormat.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
  [InlineData(OutputFormat.Png, new byte[] { 0x3C, 0x68 }, false)]
  [InlineData(OutputFormat.Pdf, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, true)]
  [InlineData(OutputFormat.Svg, new byte[] { 0x3C, 0x73, 0x76, 0x67 }, true)]
  [InlineData(OutputFormat.Svg, new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, false)]
  public void Signatures_are_checked(OutputFormat format, byte[] body, bool expected)
  {
    Assert.Equal(expected, PlantUmlClient.LooksLike(format, body));
  }

  [Fact]
  public async Task The_render_exporter_writes_the_server_response()
  {
    var handler = StubHttpMessageHandler.WorkingServer();
    var exporter = new PlantUmlRenderExporter(OutputFormat.Svg, Client(handler));
    using var stream = new MemoryStream();

    await exporter.ExportAsync(Fixtures.Graph(["A>B"]), new ExportContext("model", DateTimeOffset.UnixEpoch), stream, TestContext.Current.CancellationToken);

    Assert.Equal(FakePlantUml.Svg, stream.ToArray());
    var request = Assert.Single(handler.Requests);
    var encoded = request.Uri.AbsoluteUri[(request.Uri.AbsoluteUri.LastIndexOf('/') + 1)..];
    Assert.Contains("title Power Query dependencies: model", PlantUmlEncoder.Decode(encoded), StringComparison.Ordinal);
    Assert.Equal(".svg", exporter.FileExtension);
  }
}
