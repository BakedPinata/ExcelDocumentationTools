using System.Net;
using System.Text;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>A request captured by <see cref="StubHttpMessageHandler"/>.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The request URI.</param>
/// <param name="Body">The request body as text, or null.</param>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

/// <summary>
/// An HTTP handler that answers requests in-process, so tests never touch the network (NFR-TEST-2).
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
  : HttpMessageHandler
{
  private readonly List<RecordedRequest> requests = [];

  /// <summary>Requests received so far.</summary>
  public IReadOnlyList<RecordedRequest> Requests
  {
    get
    {
      lock (requests)
      {
        return [.. requests];
      }
    }
  }

  /// <summary>A handler that behaves like a working PlantUML server.</summary>
  public static StubHttpMessageHandler WorkingServer() => new((request, _) => Task.FromResult(FakePlantUml.Render(request)));

  /// <summary>A handler that renders normally except for one format, which fails with the given status.</summary>
  public static StubHttpMessageHandler FailingFor(string format, HttpStatusCode status) =>
    new((request, _) => Task.FromResult(
      FakePlantUml.FormatOf(request) == format
        ? new HttpResponseMessage(status)
        : FakePlantUml.Render(request)));

  /// <summary>A handler that fails the test if it is ever called.</summary>
  public static StubHttpMessageHandler Unused() =>
    new((request, _) => throw new InvalidOperationException($"No HTTP request was expected, but got {request.Method} {request.RequestUri}"));

  /// <inheritdoc />
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
    lock (requests)
    {
      requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
    }

    return await respond(request, cancellationToken);
  }
}

/// <summary>Canned PlantUML server responses.</summary>
public static class FakePlantUml
{
  /// <summary>A minimal SVG document.</summary>
  public static byte[] Svg { get; } = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

  /// <summary>Bytes that start with the PNG signature.</summary>
  public static byte[] Png { get; } = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

  /// <summary>Bytes that start with the PDF signature.</summary>
  public static byte[] Pdf { get; } = Encoding.ASCII.GetBytes("%PDF-1.4\n%fake\n");

  /// <summary>The format segment of a PlantUML request path ("svg", "png" or "pdf").</summary>
  public static string FormatOf(HttpRequestMessage request)
  {
    var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    return request.Method == HttpMethod.Post ? segments[^1] : segments[^2];
  }

  /// <summary>Returns a successful render for the requested format.</summary>
  public static HttpResponseMessage Render(HttpRequestMessage request)
  {
    var (bytes, mediaType) = FormatOf(request) switch
    {
      "svg" => (Svg, "image/svg+xml"),
      "png" => (Png, "image/png"),
      "pdf" => (Pdf, "application/pdf"),
      var other => throw new InvalidOperationException($"Unexpected format '{other}'"),
    };

    var content = new ByteArrayContent(bytes);
    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
  }
}
