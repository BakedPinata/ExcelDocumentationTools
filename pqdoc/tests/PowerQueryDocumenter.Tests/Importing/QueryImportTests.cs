using ExcelDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Importing;
using Serilog.Core;

namespace PowerQueryDocumenter.Tests.Importing;

/// <summary>What a choice of queries comes to: functions and parameters come along, as they're queries too.</summary>
public class QueryImportTests
{
  private static readonly Dictionary<string, string> Sources = new()
  {
    ["pRegion"] = "\"North\" meta [IsParameterQuery = true]",
    ["fnClean"] = "(text as text) as text => Text.Trim(text)",
    ["Base"] = "let\r\n    Source = Excel.CurrentWorkbook(){[Name=\"Sales\"]}[Content]\r\nin\r\n    Source",
    ["Report"] = "let\r\n    Source = Base,\r\n    Clean = Table.TransformColumns(Source, {\"Name\", fnClean}),\r\n    Region = Table.SelectRows(Clean, each [Region] = pRegion)\r\nin\r\n    Region",
    ["Web"] = "let Source = Json.Document(Web.Contents(\"https://example.com/data\")) in Source",
    ["Files"] = "let Source = Csv.Document(File.Contents(\"C:\\\\data\\\\a.csv\")), Other = File.Contents(\"C:\\\\data\\\\b.csv\") in Source",
    ["AllTables"] = "Excel.CurrentWorkbook()",
    ["Literal"] = "\"Web.Contents is only text here\"",
  };

  private static readonly QueryImport Import = new(
    new DependencyAnalyser(Logger.None).Analyse(Sources.Select(s => new QuerySource(s.Key, s.Value))),
    Sources);

  [Fact]
  public void A_query_brings_what_it_uses_functions_and_parameters_included()
  {
    var plan = Import.Plan(["Report"]);

    Assert.Equal(["Report"], plan.Picked);
    Assert.Equal(["Base", "fnClean", "pRegion"], plan.Brought.Select(b => b.Id));
    Assert.All(plan.Brought, b => Assert.Equal(["Report"], b.UsedBy));
  }

  [Fact]
  public void The_table_a_query_reads_from_the_workbook_is_listed_not_held_against_it()
  {
    var need = Assert.Single(Import.Plan(["Report"]).Needs);

    Assert.Equal(new ImportNeed("Sales", "a table or named range the workbook needs"), need.Need);
    Assert.Equal(["Base"], need.UsedBy);
  }

  [Fact]
  public void A_data_source_is_listed_by_the_function_and_never_with_what_it_points_at()
  {
    var need = Assert.Single(QueryImport.NeedsOf(Sources["Web"]));

    Assert.Equal("Web.Contents", need.What);
    Assert.Contains("credentials and privacy level", need.Why);
    Assert.DoesNotContain("example.com", need.What + need.Why);
  }

  [Fact]
  public void Each_thing_is_listed_once_however_often_its_used()
  {
    Assert.Equal(["File.Contents"], QueryImport.NeedsOf(Sources["Files"]).Select(n => n.What));
  }

  [Fact]
  public void Excel_CurrentWorkbook_without_a_name_is_listed_as_it_stands()
  {
    Assert.Equal("Excel.CurrentWorkbook()", Assert.Single(QueryImport.NeedsOf(Sources["AllTables"])).What);
  }

  [Fact]
  public void Text_that_only_looks_like_a_source_isnt_one()
  {
    Assert.Empty(QueryImport.NeedsOf(Sources["Literal"]));
  }

  [Fact]
  public void A_name_is_found_whatever_its_case_and_a_missing_one_isnt()
  {
    Assert.Equal("Report", Import.Find("report"));
    Assert.Null(Import.Find("Nope"));
  }
}
