using System.Text;
using PowerQueryDocumenter.Analysis;
using PowerQueryDocumenter.Model;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Core;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Analysis;

public sealed class DependencyAnalyserTests : IDisposable
{
  private readonly TempDirectory temp = new();
  private readonly TestLog log = new();

  public void Dispose() => temp.Dispose();

  private DependencyAnalyser CreateAnalyser(Func<string, CancellationToken, Task<string>>? readFile = null) =>
    new(log.CreateLogger(), readFile);

  private static string[] EdgeList(DependencyGraph graph) =>
    graph.Edges.Select(e => $"{e.Upstream}>{e.Downstream}").ToArray();

  private DependencyGraph Analyse(params (string Name, string Content)[] queries) =>
    CreateAnalyser().Analyse(queries.Select(q => new QuerySource(q.Name, q.Content)));

  [Fact]
  public async Task Fixture_queries_produce_the_expected_dependencies()
  {
    var graph = await Fixtures.GraphAsync();

    Assert.Equal(
      new[] {
        "Customers>Sales Report",
        "Cycle A>Cycle B",
        "Cycle B>Cycle A",
        "fnCleanText>Orders",
        "Orders>Q1.Summary",
        "Orders>Sales Report",
        "Q1.Summary>Sales Report",
        "Sales Region Map>Orders",
        "Sales Report>Revenue & Margin (2024)",
        "Source>Customers",
        "Source>Orders",
      },
      EdgeList(graph));
    Assert.Equal(11, graph.Nodes.Count);
  }

  [Fact]
  public void A_query_named_Source_is_detected_when_referenced()
  {
    // Acceptance criterion 6 and FR-ANA-2: a query can be named like a built-in.
    var graph = Analyse(("Source", "1"), ("Orders", "let Data = Source in Data"));

    Assert.Equal(new[] { "Source>Orders" }, EdgeList(graph));
  }

  [Theory]
  [InlineData("Table")]
  [InlineData("Value")]
  [InlineData("Text")]
  [InlineData("List")]
  [InlineData("Count")]
  public void Queries_named_like_built_ins_are_detected(string name)
  {
    var graph = Analyse((name, "1"), ("Report", $"let A = {name} in A"));

    Assert.Equal(new[] { $"{name}>Report" }, EdgeList(graph));
  }

  [Fact]
  public void Dotted_library_functions_do_not_match_a_query_with_the_prefix_name()
  {
    var graph = Analyse(("Table", "1"), ("Value", "2"), ("Report", "Table.SelectRows(Value.Metadata(x), each true)"));

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void Names_only_in_comments_or_text_create_no_dependency()
  {
    // Acceptance criterion 7.
    var graph = Analyse(
      ("Orders", "1"),
      ("Report", "let A = \"Orders\", // Orders\n B = 1 /* Orders */ in A"));

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void Quoted_references_are_detected()
  {
    var graph = Analyse(("Sales Data", "1"), ("Report", "Table.Buffer(#\"Sales Data\")"));

    Assert.Equal(new[] { "Sales Data>Report" }, EdgeList(graph));
  }

  [Fact]
  public void Matching_is_case_insensitive_and_uses_the_file_spelling()
  {
    var graph = Analyse(("Sales", "1"), ("Report", "SALES"));

    Assert.Equal(new[] { "Sales>Report" }, EdgeList(graph));
  }

  [Fact]
  public void Lower_case_type_names_do_not_match_capitalised_queries()
  {
    var graph = Analyse(
      ("Text", "1"),
      ("Table", "2"),
      ("Report", "let T = type table [Name = text], F = (x as text) as table => x in F"));

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void A_type_name_still_matches_a_query_spelled_exactly_the_same()
  {
    var graph = Analyse(("text", "1"), ("Report", "text"));

    Assert.Equal(new[] { "text>Report" }, EdgeList(graph));
  }

  [Fact]
  public void Self_references_are_ignored()
  {
    var graph = Analyse(("Orders", "let Orders = 1, Again = #\"Orders\" in Orders"));

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void A_local_step_with_a_query_name_shadows_the_query()
  {
    var graph = Analyse(("Customers", "1"), ("Report", "let Customers = 2, Total = Customers + 1 in Total"));

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void Unknown_identifiers_are_ignored()
  {
    var graph = Analyse(("Report", "let A = Excel.CurrentWorkbook(), B = SomethingElse in B"));

    Assert.Empty(graph.Edges);
    Assert.Single(graph.Nodes);
  }

  [Fact]
  public async Task Folder_with_no_query_files_reports_zero()
  {
    temp.WriteFile("notes.txt", "Orders");
    temp.WriteFile("sub/Nested.m", "1");

    var result = await CreateAnalyser().AnalyseFolderAsync(temp.Path, TestContext.Current.CancellationToken);

    Assert.Equal(0, result.QueryFileCount);
    Assert.Empty(result.Graph.Nodes);
  }

  [Fact]
  public async Task Only_top_level_m_files_are_analysed()
  {
    temp.WriteFile("Orders.m", "1");
    temp.WriteFile("Report.M", "Orders");
    temp.WriteFile("Orders.mx", "1");
    temp.WriteFile("sub/Hidden.m", "Orders");

    var result = await CreateAnalyser().AnalyseFolderAsync(temp.Path, TestContext.Current.CancellationToken);

    Assert.Equal(new[] { "Orders", "Report" }, result.Graph.Nodes.Select(n => n.Name).ToArray());
    Assert.Equal(new[] { "Orders>Report" }, EdgeList(result.Graph));
  }

  [Fact]
  public async Task Files_with_a_byte_order_mark_are_read_correctly()
  {
    await File.WriteAllTextAsync(temp.Combine("Orders.m"), "1", new UTF8Encoding(true), TestContext.Current.CancellationToken);
    await File.WriteAllTextAsync(temp.Combine("Report.m"), "let A = Orders in A", Encoding.Unicode, TestContext.Current.CancellationToken);
    await File.WriteAllTextAsync(temp.Combine("Summary.m"), "#\"Report\"", new UTF8Encoding(true), TestContext.Current.CancellationToken);

    var result = await CreateAnalyser().AnalyseFolderAsync(temp.Path, TestContext.Current.CancellationToken);

    Assert.Equal(new[] { "Orders>Report", "Report>Summary" }, EdgeList(result.Graph));
  }

  [Fact]
  public async Task An_unreadable_file_is_logged_and_skipped_but_its_query_is_kept()
  {
    temp.WriteFile("Orders.m", "1");
    temp.WriteFile("Broken.m", "Orders");
    temp.WriteFile("Report.m", "Broken");

    Task<string> Read(string path, CancellationToken token) =>
      Path.GetFileName(path) == "Broken.m"
        ? throw new IOException("The file is locked")
        : DependencyAnalyser.ReadQueryFileAsync(path, token);

    var result = await CreateAnalyser(Read).AnalyseFolderAsync(temp.Path, TestContext.Current.CancellationToken);

    Assert.Equal(3, result.QueryFileCount);
    Assert.Equal(new[] { temp.Combine("Broken.m") }, result.UnreadableFiles);
    Assert.Equal(new[] { "Broken>Report" }, EdgeList(result.Graph));
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("Broken.m", StringComparison.Ordinal) && m.Contains("locked", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Query_names_differing_only_by_case_are_reported_as_duplicates()
  {
    temp.WriteFile("Sales.m", "1");
    temp.WriteFile("sales.m", "2");
    if (temp.FileNames().Count < 2)
    {
      Assert.Skip("This file system is case-insensitive, so two files cannot differ only by case.");
    }

    var result = await CreateAnalyser().AnalyseFolderAsync(temp.Path, TestContext.Current.CancellationToken);

    Assert.Single(result.Graph.Nodes);
    Assert.Single(result.DuplicateFiles);
    Assert.Contains(log.Messages(LogEventLevel.Warning), m => m.Contains("not case-sensitive", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Missing_folder_throws_DirectoryNotFoundException()
  {
    await Assert.ThrowsAsync<DirectoryNotFoundException>(
      () => CreateAnalyser().AnalyseFolderAsync(temp.Combine("missing"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Cancellation_stops_the_analysis()
  {
    temp.WriteFile("Orders.m", "1");
    using var cancellation = new CancellationTokenSource();
    await cancellation.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => new DependencyAnalyser(Logger.None).AnalyseFolderAsync(temp.Path, cancellation.Token));
  }
}
