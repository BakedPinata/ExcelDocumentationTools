using PowerQueryDocumenter.Analysis;
using PowerQueryDocumenter.Model;
using Serilog.Core;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>Access to the sample query files in Fixtures/Queries (NFR-TEST-3).</summary>
public static class Fixtures
{
  /// <summary>Folder holding the fixture .m files (copied to the test output folder).</summary>
  public static string QueriesFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Queries");

  /// <summary>The expected CSV for the fixture queries.</summary>
  public static string ExpectedCsvPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "expected-dependencies.csv");

  /// <summary>Copies the fixture queries into <paramref name="destination"/> (created if needed).</summary>
  public static void CopyQueriesTo(string destination)
  {
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(QueriesFolder, "*.m"))
    {
      File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }
  }

  /// <summary>Analyses the fixture queries.</summary>
  public static async Task<DependencyGraph> GraphAsync()
  {
    var result = await new DependencyAnalyser(Logger.None).AnalyseFolderAsync(QueriesFolder, TestContext.Current.CancellationToken);
    return result.Graph;
  }

  /// <summary>Builds a graph from "Upstream>Downstream" pairs (names must not contain ">") plus any extra isolated names.</summary>
  public static DependencyGraph Graph(string[] edges, params string[] isolated)
  {
    var pairs = edges.Select(e => e.Split('>')).Select(p => new DependencyEdge(p[0], p[1])).ToArray();
    var names = pairs.SelectMany(p => new[] { p.Upstream, p.Downstream }).Concat(isolated);
    return DependencyGraph.Create(names, pairs);
  }
}
