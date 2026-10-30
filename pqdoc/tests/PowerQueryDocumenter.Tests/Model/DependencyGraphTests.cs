using PowerQueryDocumenter.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Model;

public sealed class DependencyGraphTests
{
  [Theory]
  [InlineData(0, 0, QueryRole.Isolated)]
  [InlineData(0, 2, QueryRole.Root)]
  [InlineData(3, 0, QueryRole.Leaf)]
  [InlineData(1, 1, QueryRole.Intermediate)]
  public void Roles_follow_upstream_and_downstream_counts(int upstream, int downstream, QueryRole expected)
  {
    Assert.Equal(expected, DependencyGraph.Classify(upstream, downstream));
  }

  [Fact]
  public void Nodes_are_classified_from_the_edges()
  {
    var graph = Fixtures.Graph(["Source>Clean", "Clean>Report"], "Parameter");

    Assert.Equal(QueryRole.Root, graph.GetNode("Source").Role);
    Assert.Equal(QueryRole.Intermediate, graph.GetNode("Clean").Role);
    Assert.Equal(QueryRole.Leaf, graph.GetNode("Report").Role);
    Assert.Equal(QueryRole.Isolated, graph.GetNode("Parameter").Role);
    Assert.Equal(1, graph.CountByRole(QueryRole.Root));
  }

  [Fact]
  public void Nodes_and_edges_are_sorted_case_insensitively()
  {
    var graph = Fixtures.Graph(["b>a", "B2>A1", "c>a"]);

    Assert.Equal(new[] { "a", "A1", "b", "B2", "c" }, graph.Nodes.Select(n => n.Name).ToArray());
    Assert.Equal(new[] { "b>a", "B2>A1", "c>a" }, graph.Edges.Select(e => $"{e.Upstream}>{e.Downstream}").ToArray());
  }

  [Fact]
  public void Duplicate_and_self_edges_are_dropped()
  {
    var graph = DependencyGraph.Create(
      ["A", "B"],
      [new("A", "B"), new("A", "B"), new("a", "b"), new("A", "A")]);

    var edge = Assert.Single(graph.Edges);
    Assert.Equal(new DependencyEdge("A", "B"), edge);
    Assert.Equal(QueryRole.Root, graph.GetNode("A").Role);
  }

  [Fact]
  public void Names_differing_only_by_case_are_one_query_and_the_first_spelling_wins()
  {
    var graph = DependencyGraph.Create(["Sales", "SALES", "Report"], [new("sales", "report")]);

    Assert.Equal(new[] { "Report", "Sales" }, graph.Nodes.Select(n => n.Name).ToArray());
    Assert.Equal(new DependencyEdge("Sales", "Report"), Assert.Single(graph.Edges));
  }

  [Fact]
  public void Lookups_are_case_insensitive()
  {
    var graph = Fixtures.Graph(["Orders>Report"]);

    Assert.True(graph.Contains("ORDERS"));
    Assert.Equal(new[] { "Orders" }, graph.GetUpstream("report"));
    Assert.Equal(new[] { "Report" }, graph.GetDownstream("orders"));
    Assert.True(graph.TryGetNode("REPORT", out var node));
    Assert.Equal("Report", node.Name);
  }

  [Fact]
  public void Counts_are_recorded_on_each_node()
  {
    var graph = Fixtures.Graph(["A>C", "B>C", "C>D"]);

    var node = graph.GetNode("C");
    Assert.Equal(2, node.UpstreamCount);
    Assert.Equal(1, node.DownstreamCount);
  }

  [Fact]
  public void An_edge_to_an_unknown_query_is_rejected()
  {
    var exception = Assert.Throws<ArgumentException>(() => DependencyGraph.Create(["A"], [new("A", "Missing")]));

    Assert.Contains("Missing", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Blank_query_names_are_rejected()
  {
    Assert.Throws<ArgumentException>(() => DependencyGraph.Create(["A", " "], []));
  }

  [Fact]
  public void Looking_up_an_unknown_query_throws()
  {
    var graph = Fixtures.Graph(["A>B"]);

    Assert.Throws<KeyNotFoundException>(() => graph.GetNode("C"));
    Assert.Throws<KeyNotFoundException>(() => graph.GetUpstream("C"));
    Assert.False(graph.TryGetNode("C", out _));
  }

  [Fact]
  public void The_graph_cannot_be_modified_through_its_collections()
  {
    var graph = Fixtures.Graph(["A>B"]);

    Assert.Throws<NotSupportedException>(() => ((IList<QueryNode>)graph.Nodes).Add(new QueryNode("C", QueryRole.Isolated, false, 0, 0)));
    Assert.Throws<NotSupportedException>(() => ((IList<DependencyEdge>)graph.Edges).Clear());
    Assert.Throws<NotSupportedException>(() => ((IList<string>)graph.GetDownstream("A")).Clear());
  }

  [Fact]
  public void The_empty_graph_has_nothing()
  {
    Assert.Empty(DependencyGraph.Empty.Nodes);
    Assert.Empty(DependencyGraph.Empty.Edges);
    Assert.Empty(DependencyGraph.Empty.Cycles);
  }
}
