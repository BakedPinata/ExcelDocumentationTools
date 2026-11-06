using System.Collections.Frozen;
using ExcelDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Model;

namespace PowerQueryDocumenter.Core.Importing;

/// <summary>
/// What a choice of queries comes to, and what a query relies on: the rules behind both the page's Export tab and the
/// import command, which is why the page gets them as data.
/// </summary>
/// <remarks>
/// Every query can come, functions and parameters included, since they're queries too. <c>Excel.CurrentWorkbook()</c>
/// isn't a reason to leave one out: it just means the new workbook, so what's listed is the table or named range it
/// reads, which that workbook will need. A query's data sources are listed as well, because credentials and privacy
/// levels belong to the person and the machine, not the workbook.
/// </remarks>
public sealed class QueryImport(DependencyGraph graph, IReadOnlyDictionary<string, string> sources)
{
  private const string CurrentWorkbook = "Excel.CurrentWorkbook";

  // The functions that reach outside the workbook. Those that only read what another gives them, such as Csv.Document
  // and Excel.Workbook, aren't here: File.Contents or Web.Contents beside them is the source.
  private static readonly FrozenSet<string> DataSources = new[]
  {
    "Access.Database", "ActiveDirectory.Domains", "AzureStorage.BlobContents", "AzureStorage.Blobs", "AzureStorage.DataLake",
    "AzureStorage.Tables", "DB2.Database", "Essbase.Cubes", "Exchange.Contents", "File.Contents", "Folder.Contents", "Folder.Files",
    "Hdfs.Contents", "Hdfs.Files", "Informix.Database", "MySQL.Database", "OData.Feed", "Odbc.DataSource", "Odbc.Query",
    "OleDb.DataSource", "OleDb.Query", "Oracle.Database", "PostgreSQL.Database", "Salesforce.Data", "Salesforce.Reports",
    "SapBusinessWarehouse.Cubes", "SapHana.Database", "SharePoint.Contents", "SharePoint.Files", "SharePoint.Tables",
    "Sql.Database", "Sql.Databases", "Sybase.Database", "Teradata.Database", "Web.Contents",
  }.ToFrozenSet(StringComparer.Ordinal);

  /// <summary>The query's name as the workbook spells it, or null if it hasn't one by that name.</summary>
  public string? Find(string name) => graph.TryGetNode(name, out var node) ? node.Name : null;

  /// <summary>What a choice of queries comes to.</summary>
  public ImportPlan Plan(IEnumerable<string> picks) =>
    ImportPlan.Build(picks, graph.GetUpstream, id => sources.TryGetValue(id, out var text) ? NeedsOf(text) : []);

  /// <summary>What a query's M relies on that doesn't come with it, each thing once.</summary>
  public static IReadOnlyList<ImportNeed> NeedsOf(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    var tokens = MTokeniser.Tokenise(text);
    var needs = new List<ImportNeed>();
    for (var i = 0; i < tokens.Count; i++)
    {
      if (tokens[i].Kind != MTokenKind.Identifier)
      {
        continue;
      }

      if (tokens[i].Text == CurrentWorkbook)
      {
        needs.Add(TableRead(tokens, i) is { } table
          ? new ImportNeed(table, "a table or named range the workbook needs")
          : new ImportNeed($"{CurrentWorkbook}()", "reads the workbook's tables and named ranges"));
      }
      else if (DataSources.Contains(tokens[i].Text))
      {
        needs.Add(new ImportNeed(tokens[i].Text, "a data source, whose credentials and privacy level don't come along"));
      }
    }

    return [.. needs.Distinct()];
  }

  // The name in Excel.CurrentWorkbook(){[Name="Sales"]}, or null if it isn't written that way.
  private static string? TableRead(IReadOnlyList<MToken> tokens, int at)
  {
    string[] before = ["(", ")", "{", "["];
    for (var i = 0; i < before.Length; i++)
    {
      if (at + 1 + i >= tokens.Count || !tokens[at + 1 + i].IsPunctuator(before[i]))
      {
        return null;
      }
    }

    var name = at + before.Length + 1;
    return name + 2 < tokens.Count && tokens[name] is { Kind: MTokenKind.Identifier, Text: "Name" }
      && tokens[name + 1].IsPunctuator("=") && tokens[name + 2].Kind == MTokenKind.TextLiteral
      ? tokens[name + 2].Text
      : null;
  }
}
