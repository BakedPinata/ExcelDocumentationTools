using System.Net;
using System.Text;
using System.Text.Json;
using PowerQueryDocumenter.Core;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class HtmlExporterTests
{
  private static readonly ExportContext Context =
    new("sales-model", new DateTimeOffset(2026, 10, 2, 10, 3, 0, TimeSpan.FromHours(8)));

  private const string DataStart = "<script type=\"application/json\" id=\"pqdoc-data\">";

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
    Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
    Assert.DoesNotContain("@import", html);
    Assert.Contains("d3js.org v7.9.0", html);
  }

  [Fact]
  public void The_policy_blocks_all_network_access()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.Contains(
      "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src data:\">",
      html);
  }

  [Fact]
  public void The_favicon_is_part_of_the_page()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.Contains("<link rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml;base64,", html);
  }

  [Fact]
  public void The_licence_of_the_vendored_library_travels_with_it()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.Contains("/*! d3 7.9.0 (ISC licence)", html);
    Assert.Contains("Mike Bostock", html);
    Assert.True(html.IndexOf("/*! d3 7.9.0", StringComparison.Ordinal) < html.IndexOf("d3js.org v7.9.0", StringComparison.Ordinal));
  }

  [Fact]
  public void The_page_shows_the_build_stamp_and_links_to_the_repository()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    var stamp = WebUtility.HtmlEncode(ToolVersion.Current.ToString());
    Assert.Contains($"<meta name=\"generator\" content=\"{stamp}\">", html);
    Assert.Contains($"<span>{stamp}</span>", html);
    Assert.Contains($"href=\"{ToolVersion.RepositoryUrl}\"", html);
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
    Assert.Contains(">sales-model</h1>", html);
    Assert.Contains(">3 queries, 3 dependencies, 1 circular dependency. Generated 2026-10-02 10:03 (UTC+08:00).</p>", html);
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

    Assert.Contains(">R&amp;D &lt;draft&gt;</h1>", html);
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
    var names = EmbeddedData(html).GetProperty("queries").EnumerateArray().Select(q => q.GetProperty("id").GetString()).ToArray();
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
  public void Embedded_data_describes_queries_edges_and_cycles()
  {
    var graph = Fixtures.Graph(["Source>A", "A>B", "B>A", "B>Report"], "Lonely");

    var data = EmbeddedData(Render(graph));

    var queries = data.GetProperty("queries").EnumerateArray().ToDictionary(q => q.GetProperty("id").GetString()!, q => q);
    Assert.Equal("root", queries["Source"].GetProperty("role").GetString());
    Assert.Equal("leaf", queries["Report"].GetProperty("role").GetString());
    Assert.Equal("isolated", queries["Lonely"].GetProperty("role").GetString());
    Assert.Equal("intermediate", queries["A"].GetProperty("role").GetString());
    Assert.True(queries["A"].GetProperty("inCycle").GetBoolean());
    Assert.False(queries["Source"].GetProperty("inCycle").GetBoolean());
    Assert.Equal(2, queries["B"].GetProperty("downstream").GetInt32());
    Assert.Equal(1, queries["B"].GetProperty("upstream").GetInt32());

    var cycle = Assert.Single(data.GetProperty("cycles").EnumerateArray());
    Assert.Equal(new[] { "A", "B" }, cycle.EnumerateArray().Select(e => e.GetString()).ToArray());
  }

  // The page keeps a circular dependency's queries in one column, so it needs all of them. A cycle's
  // Path is only one loop through the group, and here the shortest loop from A leaves out C.
  [Fact]
  public void A_cycle_lists_every_member_of_the_group_not_just_one_loop()
  {
    var graph = Fixtures.Graph(["A>B", "B>A", "B>C", "C>A"]);

    var data = EmbeddedData(Render(graph));

    var cycle = Assert.Single(data.GetProperty("cycles").EnumerateArray());
    Assert.Equal(new[] { "A", "B", "C" }, cycle.EnumerateArray().Select(e => e.GetString()).ToArray());
  }

  [Fact]
  public void Edges_run_from_upstream_to_downstream()
  {
    var graph = Fixtures.Graph(["Source>Report"]);

    var data = EmbeddedData(Render(graph));

    var edge = Assert.Single(data.GetProperty("edges").EnumerateArray());
    Assert.Equal("Source", edge.GetProperty("from").GetString());
    Assert.Equal("Report", edge.GetProperty("to").GetString());
    Assert.False(edge.GetProperty("inCycle").GetBoolean());
  }

  // Query names are matched case-insensitively, but the page compares ids exactly.
  [Fact]
  public void Edges_use_the_same_spelling_as_the_queries()
  {
    var graph = DependencyGraph.Create(["Sales", "Report"], [new DependencyEdge("SALES", "report")]);

    var data = EmbeddedData(Render(graph));

    var edge = Assert.Single(data.GetProperty("edges").EnumerateArray());
    Assert.Equal("Sales", edge.GetProperty("from").GetString());
    Assert.Equal("Report", edge.GetProperty("to").GetString());
  }

  [Fact]
  public void The_page_follows_the_system_theme_and_motion_settings()
  {
    var html = Render(Fixtures.Graph(["A>B"]));

    Assert.Contains("prefers-color-scheme: light", html);
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
    Assert.Equal(11, data.GetProperty("queries").GetArrayLength());
    Assert.Equal(11, data.GetProperty("edges").GetArrayLength());
  }
}
