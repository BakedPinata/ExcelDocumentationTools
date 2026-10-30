using PowerQueryDocumenter.Analysis;
using PowerQueryDocumenter.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Analysis;

public sealed class CycleDetectorTests
{
  [Fact]
  public void An_acyclic_graph_has_no_cycles()
  {
    var graph = Fixtures.Graph(["A>B", "B>C", "A>C"]);

    Assert.Empty(graph.Cycles);
    Assert.DoesNotContain(graph.Nodes, n => n.IsInCycle);
  }

  [Fact]
  public void Two_queries_referencing_each_other_form_a_cycle()
  {
    var graph = Fixtures.Graph(["A>B", "B>A", "B>C"]);

    var cycle = Assert.Single(graph.Cycles);
    Assert.Equal(new[] { "A", "B" }, cycle.Members);
    Assert.Equal(new[] { "A", "B", "A" }, cycle.Path);
    Assert.Equal("A -> B -> A", cycle.Describe());
  }

  [Fact]
  public void The_cycle_path_starts_at_the_first_member_by_name_and_follows_edge_direction()
  {
    var graph = Fixtures.Graph(["Zeta>alpha", "alpha>Mid", "Mid>Zeta"]);

    var cycle = Assert.Single(graph.Cycles);
    Assert.Equal(new[] { "alpha", "Mid", "Zeta", "alpha" }, cycle.Path);
  }

  [Fact]
  public void Separate_cycles_are_reported_separately_in_name_order()
  {
    var graph = Fixtures.Graph(["X>Y", "Y>X", "B>C", "C>B", "C>X"]);

    Assert.Equal(2, graph.Cycles.Count);
    Assert.Equal(new[] { "B", "C" }, graph.Cycles[0].Members);
    Assert.Equal(new[] { "X", "Y" }, graph.Cycles[1].Members);
  }

  [Fact]
  public void Overlapping_cycles_are_one_group()
  {
    var graph = Fixtures.Graph(["A>B", "B>A", "B>C", "C>B"]);

    var cycle = Assert.Single(graph.Cycles);
    Assert.Equal(new[] { "A", "B", "C" }, cycle.Members);
  }

  [Fact]
  public void Only_queries_in_a_cycle_are_flagged()
  {
    var graph = Fixtures.Graph(["Source>A", "A>B", "B>A", "B>Report"]);

    Assert.Equal(
      new[] { "A", "B" },
      graph.Nodes.Where(n => n.IsInCycle).Select(n => n.Name).ToArray());
  }

  [Fact]
  public void Very_long_chains_and_cycles_do_not_overflow_the_stack()
  {
    const int length = 20_000;
    var names = Enumerable.Range(0, length).Select(i => $"Q{i:D5}").ToArray();
    var edges = names.Zip(names.Skip(1), (a, b) => new DependencyEdge(a, b))
      .Append(new DependencyEdge(names[^1], names[0]));

    var graph = DependencyGraph.Create(names, edges);

    var cycle = Assert.Single(graph.Cycles);
    Assert.Equal(length, cycle.Members.Count);
    Assert.Equal(length + 1, cycle.Path.Count);
  }

  [Fact]
  public void Successors_outside_the_node_list_and_self_loops_are_ignored()
  {
    var cycles = CycleDetector.FindCycles(["A", "B"], name => name == "A" ? ["A", "Unknown"] : []);

    Assert.Empty(cycles);
  }
}
