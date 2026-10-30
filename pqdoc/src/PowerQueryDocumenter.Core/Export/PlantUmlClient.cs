using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>A PlantUML server could not render a diagram. The message is suitable for showing to the user.</summary>
public sealed class PlantUmlRenderException : Exception
{
  /// <summary>Creates the exception.</summary>
  public PlantUmlRenderException(string message)
    : base(message)
  {
  }

  /// <summary>Creates the exception with an inner cause.</summary>
  public PlantUmlRenderException(string message, Exception innerException)
    : base(message, innerException)
  {
  }

  /// <summary>Creates the exception.</summary>
  public PlantUmlRenderException()
  {
  }
}

/// <summary>
/// Renders PlantUML source through a PlantUML server's standard HTTP API (FR-PUML-5, FR-PUML-6).
/// </summary>
/// <remarks>
/// <para>
/// Request choice: the diagram is encoded (<see cref="PlantUmlEncoder"/>) and requested with
/// <c>GET {server}/{format}/{encoded}</c>. GET is supported by every PlantUML server version
/// (including the lightweight <c>-picoweb</c> server) and is cacheable. Large graphs produce long
/// URLs, which proxies and servers commonly reject beyond a few thousand characters, so when the
/// URL would exceed <see cref="MaxGetUrlLength"/> characters the plain diagram text is sent as the
/// body of <c>POST {server}/{format}</c> instead, which the PlantUML server accepts with no length
/// limit. This keeps small diagrams on the most compatible path and still handles large graphs.
/// </para>
/// <para>
/// Every request has a timeout; network errors, timeouts, non-2xx responses, diagram errors
/// reported by the server, and empty or unexpected bodies all become a
/// <see cref="PlantUmlRenderException"/> with an actionable message.
/// </para>
/// </remarks>
public sealed class PlantUmlClient
{
  /// <summary>Longest GET URL used before switching to POST (FR-PUML-5).</summary>
  public const int MaxGetUrlLength = 2000;

  /// <summary>Response header the PlantUML server sets when the diagram has an error.</summary>
  public const string DiagramErrorHeader = "X-PlantUML-Diagram-Error";

  /// <summary>Response header with the line number of a diagram error.</summary>
  public const string DiagramErrorLineHeader = "X-PlantUML-Diagram-Error-Line";

  private readonly HttpClient httpClient;
  private readonly string serverBase;

  /// <param name="httpClient">The shared client (NFR-ARCH-4). Its own timeout is not relied on.</param>
  /// <param name="server">Absolute http(s) base URL of the server, for example https://plantuml.example.com/plantuml.</param>
  /// <param name="timeout">Timeout for each request.</param>
  public PlantUmlClient(HttpClient httpClient, Uri server, TimeSpan timeout)
  {
    ArgumentNullException.ThrowIfNull(httpClient);
    ArgumentNullException.ThrowIfNull(server);
    if (timeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be positive.");
    }

    this.httpClient = httpClient;
    Server = server;
    Timeout = timeout;
    serverBase = server.GetLeftPart(UriPartial.Path).TrimEnd('/');
  }

  /// <summary>The server base URL.</summary>
  public Uri Server { get; }

  /// <summary>The per-request timeout.</summary>
  public TimeSpan Timeout { get; }

  /// <summary>Creates the shared HTTP client used for all render requests.</summary>
  public static HttpClient CreateHttpClient()
  {
    // Timeouts are applied per request (see RenderAsync), so the client-wide timeout is disabled.
    var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    var version = typeof(PlantUmlClient).Assembly.GetName().Version?.ToString(3);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("pqdoc", version));
    return client;
  }

  /// <summary>Builds the request for a diagram: GET when the URL is short enough, otherwise POST.</summary>
  public HttpRequestMessage CreateRequest(string diagram, OutputFormat format)
  {
    ArgumentNullException.ThrowIfNull(diagram);
    if (!format.IsRendered())
    {
      throw new ArgumentOutOfRangeException(nameof(format), format, "Only svg, png and pdf are rendered by a PlantUML server.");
    }

    var endpoint = $"{serverBase}/{format.CliName()}";
    var getUrl = $"{endpoint}/{PlantUmlEncoder.Encode(diagram)}";

    if (getUrl.Length <= MaxGetUrlLength)
    {
      return new HttpRequestMessage(HttpMethod.Get, getUrl);
    }

    return new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new StringContent(diagram, Encoding.UTF8, "text/plain"),
    };
  }

  /// <summary>Renders <paramref name="diagram"/> and returns the rendered bytes.</summary>
  /// <exception cref="PlantUmlRenderException">Rendering failed.</exception>
  /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
  public async Task<byte[]> RenderAsync(string diagram, OutputFormat format, CancellationToken cancellationToken)
  {
    using var request = CreateRequest(diagram, format);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(Timeout);

    try
    {
      using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
        .ConfigureAwait(false);

      var diagramError = HeaderValue(response, DiagramErrorHeader);
      if (diagramError is not null)
      {
        var line = HeaderValue(response, DiagramErrorLineHeader);
        throw new PlantUmlRenderException(
          $"the PlantUML server reported a diagram error{(line is null ? string.Empty : $" on line {line}")}: {diagramError}");
      }

      if (!response.IsSuccessStatusCode)
      {
        throw new PlantUmlRenderException(
          $"the PlantUML server at {Server} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for a {request.Method} request");
      }

      var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
      if (body.Length == 0)
      {
        throw new PlantUmlRenderException($"the PlantUML server at {Server} returned an empty response");
      }

      if (!LooksLike(format, body))
      {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
        throw new PlantUmlRenderException(
          $"the PlantUML server at {Server} returned content that is not {format.CliName().ToUpperInvariant()} "
          + $"(Content-Type: {contentType}). Check that the URL points at a PlantUML server");
      }

      return body;
    }
    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
      throw new PlantUmlRenderException(
        $"the PlantUML server at {Server} did not respond within {Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds",
        ex);
    }
    catch (HttpRequestException ex)
    {
      throw new PlantUmlRenderException($"could not reach the PlantUML server at {Server}: {ex.Message}", ex);
    }
  }

  /// <summary>A light signature check, so that an HTML error page served with status 200 is not saved as a diagram.</summary>
  public static bool LooksLike(OutputFormat format, ReadOnlySpan<byte> body) => format switch
  {
    OutputFormat.Png => body.StartsWith(PngSignature),
    OutputFormat.Pdf => body.StartsWith("%PDF"u8),
    OutputFormat.Svg => Encoding.UTF8.GetString(body[..Math.Min(body.Length, 4096)]).Contains("<svg", StringComparison.OrdinalIgnoreCase),
    _ => true,
  };

  private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  private static string? HeaderValue(HttpResponseMessage response, string name) =>
    response.Headers.TryGetValues(name, out var values) ? string.Join(' ', values) : null;
}
