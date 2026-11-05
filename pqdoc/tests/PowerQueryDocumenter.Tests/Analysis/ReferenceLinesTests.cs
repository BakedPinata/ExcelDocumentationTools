using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Core;

namespace PowerQueryDocumenter.Tests.Analysis;

/// <summary>
/// Where each reference is (FR-HTML-19, step E1 of the feedback plan): the lines of the downstream query's source that
/// name the upstream query, so the details panel can link to them.
/// </summary>
public sealed class ReferenceLinesTests
{
  private static DependencyGraph Analyse(params (string Name, string Content)[] queries) =>
    new DependencyAnalyser(Logger.None).Analyse(queries.Select(q => new QuerySource(q.Name, q.Content)));

  private static IReadOnlyList<int> Lines(DependencyGraph graph, string upstream, string downstream) =>
    graph.LinesOf(new DependencyEdge(upstream, downstream));

  [Fact]
  public async Task The_fixture_queries_lines_are_where_each_query_is_named()
  {
    var graph = await Fixtures.GraphAsync();

    Assert.Equal([2], Lines(graph, "Orders", "Sales Report"));
    Assert.Equal([2], Lines(graph, "Customers", "Sales Report"));
    Assert.Equal([3], Lines(graph, "Q1.Summary", "Sales Report"));
  }

  [Fact]
  public async Task Every_fixture_edge_has_lines_and_each_names_the_upstream_query()
  {
    var graph = await Fixtures.GraphAsync();
    var files = Directory.GetFiles(Fixtures.QueriesFolder, "*.m").ToDictionary(
      f => Path.GetFileNameWithoutExtension(f), f => File.ReadAllText(f).Split(["\r\n", "\n", "\r"], StringSplitOptions.None),
      DependencyGraph.NameComparer);

    Assert.All(graph.Edges, edge =>
    {
      var lines = graph.LinesOf(edge);
      Assert.NotEmpty(lines);
      Assert.All(lines, line => Assert.Contains(edge.Upstream, files[edge.Downstream][line - 1], StringComparison.OrdinalIgnoreCase));
    });
  }

  [Fact]
  public void A_query_named_on_several_lines_lists_each_line_once_in_order()
  {
    var graph = Analyse(("Base", "1"), ("Report", "let\n  A = Base + Base,\n  B = 2,\n  C = Base\nin\n  A + C"));

    Assert.Equal([2, 4], Lines(graph, "Base", "Report"));
  }

  [Fact]
  public void Every_kind_of_line_ending_counts_as_one_line()
  {
    var graph = Analyse(("Base", "1"), ("Report", "let\r\n  A = 1,\r  B = Base,\n  C = Base\r\nin\r\n  #\"Base\""));

    Assert.Equal([3, 4, 6], Lines(graph, "Base", "Report"));
  }

  [Fact]
  public void A_name_in_a_comment_or_a_local_step_has_no_line()
  {
    var graph = Analyse(("Base", "1"), ("Report", "let\n  // Base is read below\n  Base = 2,\n  A = Base\nin\n  A"));

    Assert.Empty(graph.Edges);
    Assert.Empty(graph.ReferenceLines);
  }

  [Fact]
  public void A_graph_built_without_sources_has_no_lines()
  {
    var graph = Fixtures.Graph(["A>B"]);

    Assert.Empty(graph.LinesOf(new DependencyEdge("A", "B")));
  }
}
