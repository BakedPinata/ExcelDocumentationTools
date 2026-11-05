using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Core;

namespace PowerQueryDocumenter.Tests.Analysis;

/// <summary>
/// A query's steps (FR-HTML-20, step E4 of the feedback plan): its outermost let's steps in order, each with its line and
/// the queries it uses, then the in expression; a let inside a step adds none.
/// </summary>
public sealed class QueryStepsTests
{
  // The B1 sample workbook's queries, analysed as the tool does.
  private static readonly DependencyGraph Workbook = AnalyseWorkbook();

  private static DependencyGraph AnalyseWorkbook()
  {
    var queries = WorkbookReader.Read(Fixtures.Workbook("Queries.xlsx"));
    return new DependencyAnalyser(Logger.None).Analyse(queries.Select(q => new QuerySource(q.Name, q.Expression)));
  }

  private static DependencyGraph Analyse(params (string Name, string Content)[] queries) =>
    new DependencyAnalyser(Logger.None).Analyse(queries.Select(q => new QuerySource(q.Name, q.Content)));

  private static (string?, int, string)[] Steps(DependencyGraph graph, string query) =>
    graph.StepsOf(query).Select(s => (s.Name, s.Line, string.Join(", ", s.Uses))).ToArray();

  [Fact]
  public void FR_HTML_20_each_step_has_its_line_and_the_queries_it_uses_then_the_in_expression()
  {
    Assert.Equal([("Source", 2, "Chain_A, pRegion"), (null, 3, "")], Steps(Workbook, "Chain_B"));
  }

  [Fact]
  public void FR_HTML_20_a_let_inside_a_step_adds_no_steps_and_quoted_names_are_unescaped()
  {
    // Manual Targets' first step holds "let _t = … in …"; its third uses the function fnClean.
    Assert.Equal(
      [("Source", 2, ""), ("Changed Type", 3, ""), ("cleaned", 4, "fnClean"), (null, 5, "")],
      Steps(Workbook, "Manual Targets"));
  }

  [Fact]
  public void FR_HTML_20_comments_dont_move_the_lines_and_a_step_over_several_lines_has_its_first()
  {
    Assert.Equal([("Source", 5, ""), ("Result", 6, ""), (null, 7, "")], Steps(Workbook, "Commented"));
    Assert.Equal([("Source", 2, "Chain_D"), (null, 7, "")], Steps(Workbook, "Chain_E"));
  }

  [Fact]
  public void FR_HTML_20_a_query_without_a_let_of_its_own_has_no_steps()
  {
    Assert.Empty(Workbook.StepsOf("fnClean"));
    var graph = Analyse(("Base", "1"), ("Wrapped", "Table.Buffer(let x = Base in x)"));
    Assert.Empty(graph.StepsOf("Wrapped")); // a let inside an argument isn't the query's own
    Assert.Equal(["Base>Wrapped"], graph.Edges.Select(e => $"{e.Upstream}>{e.Downstream}"));
  }

  [Fact]
  public void FR_HTML_20_a_function_querys_body_let_has_steps_and_a_step_using_a_query_twice_lists_it_once()
  {
    var graph = Analyse(
      ("Base", "1"),
      ("Scale", "(n as number) =>\n  let\n    a = Base * n + Base,\n    b = a + 1\n  in\n    b"));

    Assert.Equal([("a", 3, "Base"), ("b", 4, ""), (null, 5, "")], Steps(graph, "Scale"));
  }

  [Fact]
  public void FR_HTML_20_a_step_named_like_a_query_is_the_step_not_the_query()
  {
    var graph = Analyse(("Orders", "1"), ("Report", "let\n  Orders = 2,\n  Total = Orders + 1\nin\n  Total"));

    Assert.Equal([("Orders", 2, ""), ("Total", 3, ""), (null, 4, "")], Steps(graph, "Report"));
  }
}
