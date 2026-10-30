using System.Text;
using System.Text.RegularExpressions;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class PlantUmlDiagramBuilderTests
{
  private static string[] Lines(string diagram) => diagram.Split('\n');

  [Fact]
  public void The_diagram_is_wrapped_in_startuml_and_enduml()
  {
    var diagram = PlantUmlDiagramBuilder.Build(Fixtures.Graph(["A>B"]), "Title");

    Assert.StartsWith("@startuml\n", diagram);
    Assert.EndsWith("@enduml\n", diagram);
    Assert.DoesNotContain("\r", diagram);
  }

  [Fact]
  public void The_layout_runs_left_to_right_with_a_legend()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["A>B"]), "Title"));

    Assert.Contains("left to right direction", lines);
    Assert.Contains("legend right", lines);
    Assert.Contains("endlegend", lines);
    Assert.Contains("hide stereotype", lines);
  }

  [Fact]
  public void Aliases_are_assigned_in_name_order()
  {
    var graph = Fixtures.Graph(["beta>Alpha"], "gamma");

    var aliases = PlantUmlDiagramBuilder.CreateAliases(graph);

    Assert.Equal("q1", aliases["Alpha"]);
    Assert.Equal("q2", aliases["beta"]);
    Assert.Equal("q3", aliases["gamma"]);
  }

  [Fact]
  public void Every_query_is_a_node_including_isolated_ones()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["Source>Report"], "Lonely"), "Title"));

    Assert.Contains("rectangle \"Lonely\" as q1 <<isolated>>", lines);
    Assert.Contains("rectangle \"Report\" as q2 <<leaf>>", lines);
    Assert.Contains("rectangle \"Source\" as q3 <<root>>", lines);
  }

  [Fact]
  public void Intermediate_queries_have_no_stereotype()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["A>B", "B>C"]), "Title"));

    Assert.Contains("rectangle \"B\" as q2", lines);
  }

  [Fact]
  public void Edges_point_from_upstream_to_downstream()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["Source>Report"]), "Title"));

    Assert.Contains("q2 --> q1", lines);
  }

  [Fact]
  public void Queries_and_edges_in_a_cycle_are_flagged()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["A>B", "B>A", "B>C"]), "Title"));

    Assert.Contains("rectangle \"A\" as q1 <<cycle>>", lines);
    Assert.Contains("rectangle \"B\" as q2 <<cycle>>", lines);
    Assert.Contains($"q1 -[{PlantUmlDiagramBuilder.CycleArrowColour},bold]-> q2", lines);
    Assert.Contains($"q2 -[{PlantUmlDiagramBuilder.CycleArrowColour},bold]-> q1", lines);
    Assert.Contains("q2 --> q3", lines);
  }

  [Theory]
  [InlineData("Sales Report", "Sales Report")]
  [InlineData("fn_Clean-Text", "fn_Clean-Text")]
  [InlineData("Q1.Summary (2024)", "Q1.Summary (2024)")]
  [InlineData("Say \"Hi\"", "Say &#34;Hi&#34;")]
  [InlineData("back\\slash", "back&#92;slash")]
  [InlineData("a__b", "a_&#95;b")]
  [InlineData("x--y", "x-&#45;y")]
  [InlineData("//c", "/&#47;c")]
  [InlineData("**bold**", "&#42;&#42;bold&#42;&#42;")]
  [InlineData("#1 & <b>", "&#35;1 &#38; &#60;b&#62;")]
  [InlineData("[[link]] $var %date()", "&#91;&#91;link&#93;&#93; &#36;var &#37;date()")]
  [InlineData("tab\there", "tab here")]
  [InlineData("Ünïcödé 🚀", "Ünïcödé 🚀")]
  public void Labels_are_escaped_so_PlantUML_shows_them_literally(string name, string expected)
  {
    Assert.Equal(expected, PlantUmlDiagramBuilder.EscapeLabel(name));
  }

  [Fact]
  public void Awkward_names_cannot_break_out_of_the_label()
  {
    const string hostile = "Say \"Hi\" as q9 <<root>>\nq1 --> q2";
    var graph = DependencyGraph.Create([hostile, "Report"], [new DependencyEdge(hostile, "Report")]);

    var nodeLines = Lines(PlantUmlDiagramBuilder.Build(graph, "Title")).Where(l => l.StartsWith("rectangle", StringComparison.Ordinal)).ToArray();

    Assert.Equal(2, nodeLines.Length);
    Assert.All(nodeLines, line => Assert.Matches("^rectangle \"[^\"]*\" as q\\d+( <<[a-z]+>>)?$", line));
  }

  [Fact]
  public void The_title_is_escaped()
  {
    var lines = Lines(PlantUmlDiagramBuilder.Build(Fixtures.Graph(["A>B"]), "Model \"v2\""));

    Assert.Contains("title Model &#34;v2&#34;", lines);
  }

  [Fact]
  public void Output_is_deterministic()
  {
    var graph = Fixtures.Graph(["C>A", "B>A", "A>D"], "E");

    Assert.Equal(PlantUmlDiagramBuilder.Build(graph, "T"), PlantUmlDiagramBuilder.Build(graph, "T"));
  }

  [Fact]
  public async Task The_source_exporter_writes_UTF8_without_a_byte_order_mark()
  {
    var graph = Fixtures.Graph(["Région>Report"]);
    var context = new ExportContext("sales-model", DateTimeOffset.UnixEpoch);
    using var stream = new MemoryStream();

    await new PlantUmlSourceExporter().ExportAsync(graph, context, stream, TestContext.Current.CancellationToken);

    var bytes = stream.ToArray();
    Assert.Equal((byte)'@', bytes[0]);
    var text = Encoding.UTF8.GetString(bytes);
    Assert.Equal(PlantUmlDiagramBuilder.Build(graph, "Power Query dependencies: sales-model"), text);
    Assert.Contains("rectangle \"Région\"", text);
  }

  [Fact]
  public void The_source_exporter_describes_its_format()
  {
    var exporter = new PlantUmlSourceExporter();

    Assert.Equal(OutputFormat.Puml, exporter.Format);
    Assert.Equal(".puml", exporter.FileExtension);
  }

  [Fact]
  public async Task The_fixture_graph_produces_one_node_line_per_query()
  {
    var graph = await Fixtures.GraphAsync();

    var diagram = PlantUmlDiagramBuilder.Build(graph, "Fixtures");

    Assert.Equal(graph.Nodes.Count, Regex.Matches(diagram, "^rectangle ", RegexOptions.Multiline).Count);
    Assert.Equal(graph.Edges.Count, Regex.Matches(diagram, "^q\\d+ -", RegexOptions.Multiline).Count);
  }
}
