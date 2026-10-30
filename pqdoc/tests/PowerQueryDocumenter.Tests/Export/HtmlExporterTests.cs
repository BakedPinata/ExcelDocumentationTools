using System.Text;
using System.Text.Json;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class HtmlExporterTests
{
  private static readonly ExportContext Context =
    new("sales-model", new DateTimeOffset(2026, 10, 2, 10, 3, 0, TimeSpan.FromHours(8)));

  private const string DataStart = "<script type=\"application/json\" id=\"graph-data\">";

  private static string Render(DependencyGraph graph, ExportContext? context = null) =>
    HtmlExporter.Render(graph, context ?? Context);

  private static JsonElement EmbeddedData(string html)
  {
    var start = html.IndexOf(DataStart, StringComparison.Ordinal) + DataStart.Length;
    var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
    return JsonDocument.Parse(html[start..end]).RootElement.Clone();
  }

  [Fact]
  public void The_page_is_self_contained()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.DoesNotContain("<script src", html);
    Assert.DoesNotContain("<link", html);
    Assert.DoesNotContain("@import", html);
    Assert.Contains("d3js.org v7.9.0", html);
    Assert.Contains("ISC licence", html);
  }

  [Fact]
  public void Every_placeholder_is_replaced()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.DoesNotMatch("\\{\\{[A-Z0-9_]+\\}\\}", html);
  }

  [Fact]
  public void The_title_and_heading_show_the_name_counts_and_generation_time()
  {
    var html = Render(Fixtures.Graph(["A>B", "B>A", "B>C"]));

    Assert.Contains("<title>sales-model: 3 queries, 3 dependencies, generated 2026-10-02 10:03 (UTC+08:00)</title>", html);
    Assert.Contains("<h1>sales-model</h1>", html);
    Assert.Contains("3 queries, 3 dependencies, 1 circular dependency. Generated 2026-10-02 10:03 (UTC+08:00).", html);
  }

  [Fact]
  public void Singular_counts_read_naturally()
  {
    var html = Render(DependencyGraph.Create(["Only"], []));

    Assert.Contains("1 query, 0 dependencies.", html);
  }

  [Fact]
  public void The_output_name_is_HTML_encoded()
  {
    var html = Render(Fixtures.Graph(["A>B"]), Context with { OutputName = "R&D <draft>" });

    Assert.Contains("<h1>R&amp;D &lt;draft&gt;</h1>", html);
    Assert.DoesNotContain("<draft>", html);
  }

  [Fact]
  public void Query_names_cannot_break_out_of_the_data_script()
  {
    const string hostile = "</script><script>alert('pwned')</script>";
    var graph = DependencyGraph.Create([hostile, "Report & <Co>"], [new DependencyEdge(hostile, "Report & <Co>")]);

    var html = Render(graph);

    Assert.DoesNotContain("alert('pwned')", html);
    Assert.DoesNotContain("<Co>", html);
    var names = EmbeddedData(html).GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()).ToArray();
    Assert.Equal(new[] { hostile, "Report & <Co>" }, names);
  }

  [Theory]
  [InlineData("{\"a\":\"</script>\"}", "{\"a\":\"\\u003C/script\\u003E\"}")]
  [InlineData("{\"a\":\"x & y\"}", "{\"a\":\"x \\u0026 y\"}")]
  [InlineData("{\"a\":\"line\u2028sep\"}", "{\"a\":\"line\\u2028sep\"}")]
  [InlineData("{\"a\":1}", "{\"a\":1}")]
  public void Script_escaping_neutralises_markup_characters(string json, string expected)
  {
    Assert.Equal(expected, HtmlExporter.EscapeForScriptElement(json));
  }

  [Fact]
  public void Embedded_data_describes_nodes_links_and_cycles()
  {
    var graph = Fixtures.Graph(["Source>A", "A>B", "B>A", "B>Report"], "Lonely");

    var data = EmbeddedData(Render(graph));

    var nodes = data.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("name").GetString()!, n => n);
    Assert.Equal("root", nodes["Source"].GetProperty("role").GetString());
    Assert.Equal("leaf", nodes["Report"].GetProperty("role").GetString());
    Assert.Equal("isolated", nodes["Lonely"].GetProperty("role").GetString());
    Assert.Equal("intermediate", nodes["A"].GetProperty("role").GetString());
    Assert.True(nodes["A"].GetProperty("inCycle").GetBoolean());
    Assert.False(nodes["Source"].GetProperty("inCycle").GetBoolean());
    Assert.Equal(2, nodes["B"].GetProperty("downstream").GetInt32());
    Assert.Equal(1, nodes["B"].GetProperty("upstream").GetInt32());

    var cycle = Assert.Single(data.GetProperty("cycles").EnumerateArray());
    Assert.Equal(new[] { "A", "B", "A" }, cycle.EnumerateArray().Select(e => e.GetString()).ToArray());
  }

  [Fact]
  public void Links_run_from_upstream_to_downstream()
  {
    var graph = Fixtures.Graph(["Source>Report"]);

    var data = EmbeddedData(Render(graph));

    var ids = data.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetInt32(), n => n.GetProperty("name").GetString());
    var link = Assert.Single(data.GetProperty("links").EnumerateArray());
    Assert.Equal("Source", ids[link.GetProperty("source").GetInt32()]);
    Assert.Equal("Report", ids[link.GetProperty("target").GetInt32()]);
    Assert.False(link.GetProperty("inCycle").GetBoolean());
  }

  [Fact]
  public void The_page_adapts_to_light_and_dark_schemes()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.Contains("prefers-color-scheme: dark", html);
    Assert.Contains("prefers-reduced-motion", html);
  }

  [Fact]
  public async Task The_exporter_writes_UTF8_without_a_byte_order_mark()
  {
    using var stream = new MemoryStream();

    await new HtmlExporter().ExportAsync(Fixtures.Graph(["Région>B"]), Context, stream, TestContext.Current.CancellationToken);

    var bytes = stream.ToArray();
    Assert.Equal((byte)'<', bytes[0]);
    Assert.Contains("\\u00E9", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(OutputFormat.Html, new HtmlExporter().Format);
  }

  [Fact]
  public async Task The_fixture_graph_renders()
  {
    var html = Render(await Fixtures.GraphAsync());

    var data = EmbeddedData(html);
    Assert.Equal(11, data.GetProperty("nodes").GetArrayLength());
    Assert.Equal(11, data.GetProperty("links").GetArrayLength());
  }
}
