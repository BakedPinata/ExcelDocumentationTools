using System.Text;
using CodeDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Importing;

/// <summary>A query's <c>.m</c> file for a bundle.</summary>
/// <param name="FileName">Its name.</param>
/// <param name="Bytes">Its bytes.</param>
public sealed record QueryBundleFile(string FileName, byte[] Bytes);

/// <summary>
/// A plan as a bundle: each query's <c>.m</c> file, and a README saying how to bring them in by hand, in an order that
/// puts each query after those it uses. It says nothing about when it was written, so the same choice gives the same
/// bundle.
/// </summary>
public static class QueryBundle
{
  /// <summary>Each file's name and bytes.</summary>
  public static Dictionary<string, byte[]> Files(ImportPlan plan, IReadOnlyDictionary<string, QueryBundleFile> files)
  {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(files);
    return plan.Items.ToDictionary(id => files[id].FileName, id => files[id].Bytes, StringComparer.Ordinal);
  }

  /// <summary>The README, whose first line marks the folder as a bundle (see <see cref="BundleFolder"/>).</summary>
  /// <param name="source">The workbook's file name, or the folder's name.</param>
  /// <param name="plan">What's in the bundle.</param>
  /// <param name="graph">The queries, to put them in order.</param>
  /// <param name="files">Each query's file.</param>
  public static string Readme(string source, ImportPlan plan, DependencyGraph graph, IReadOnlyDictionary<string, QueryBundleFile> files)
  {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(files);
    var why = plan.Picked.ToDictionary(id => id, _ => "Chosen", DependencyGraph.NameComparer);
    foreach (var item in plan.Brought)
    {
      why[item.Id] = $"Used by {string.Join(", ", item.UsedBy)}";
    }

    var text = new StringBuilder();
    text.Append(BundleFolder.Marker("pqdoc")).Append('\n');
    text.Append($"\n# Queries from {source}\n\n");
    text.Append("Chosen with pqdoc, with everything they use. To bring them into a workbook by hand, for each file below, in this order:\n\n");
    text.Append("1. Choose **Data > Get Data > From Other Sources > Blank Query**.\n");
    text.Append("2. In the Power Query editor, choose **Home > Advanced Editor**, and replace what's there with the file's text.\n");
    text.Append("3. Name the query exactly as it's named below.\n");
    text.Append("4. Choose **Home > Close & Load To… > Only Create Connection**.\n");

    text.Append("\n| File | Query | Why it's here |\n|---|---|---|\n");
    foreach (var id in InOrder(plan, graph))
    {
      text.Append($"| `{files[id].FileName}` | {id} | {why[id]} |\n");
    }

    if (plan.Needs.Count > 0)
    {
      text.Append("\n## To check in the target\n\nThese didn't come along:\n\n");
      foreach (var need in plan.Needs)
      {
        text.Append($"- **{need.Need.What}:** {need.Need.Why}. Used by {string.Join(", ", need.UsedBy)}.\n");
      }
    }

    return text.ToString();
  }

  // Each query after those it uses; in a cycle, as far as it can be.
  private static List<string> InOrder(ImportPlan plan, DependencyGraph graph)
  {
    var items = new HashSet<string>(plan.Items, DependencyGraph.NameComparer);
    var seen = new HashSet<string>(DependencyGraph.NameComparer);
    var order = new List<string>();
    void Visit(string id)
    {
      if (!seen.Add(id))
      {
        return;
      }

      foreach (var used in graph.GetUpstream(id).Where(items.Contains))
      {
        Visit(used);
      }

      order.Add(id);
    }

    foreach (var id in plan.Items.Order(DependencyGraph.NameComparer))
    {
      Visit(id);
    }

    return order;
  }
}
