using CodeDocumenter.Shared;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Application;

/// <summary><c>pqdoc import</c>: what a choice comes to, from a folder of .m files or a workbook, and bundles.</summary>
public sealed class ImportCommandTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public ImportCommandTests()
  {
    this.temp.WriteFile("queries/Base.m", "let\r\n    Source = Excel.CurrentWorkbook(){[Name=\"Sales\"]}[Content]\r\nin\r\n    Source\r\n");
    this.temp.WriteFile("queries/fnClean.m", "(text as text) as text => Text.Trim(text)\r\n");
    this.temp.WriteFile("queries/Report.m", "let\r\n    Source = Base,\r\n    Clean = Table.TransformColumns(Source, {\"Name\", fnClean})\r\nin\r\n    Clean\r\n");
    this.temp.WriteFile("queries/Unrelated.m", "1\r\n");
  }

  private string Queries => this.temp.Combine("queries");

  private string Bundle => this.temp.Combine("Shared");

  public void Dispose() => this.temp.Dispose();

  private TestApp App() => new(this.temp.Path);

  [Fact]
  public async Task Named_queries_say_what_they_come_to_and_nothing_is_written_without_a_bundle()
  {
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync("import", Queries, "--items", "Report"));

    var info = app.Log.Messages(LogEventLevel.Information);
    Assert.Contains("Chosen: Report", info);
    Assert.Contains("Brought along: Base (used by Report), fnClean (used by Report)", info);
    Assert.Contains(info, m => m.StartsWith("Nothing was written", StringComparison.Ordinal));
    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.StartsWith("To check in the target: Sales", StringComparison.Ordinal));
    Assert.False(Directory.Exists(Bundle));
  }

  [Fact]
  public async Task A_bundle_holds_the_files_and_a_README_listing_each_query_after_those_it_uses()
  {
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync("import", Queries, "--items", "Report", "--bundle", Bundle));

    Assert.Equal([".gitattributes", "Base.m", "fnClean.m", "README.md", "Report.m"], Directory.GetFiles(Bundle).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
    var readme = File.ReadAllText(Path.Combine(Bundle, "README.md"));
    Assert.Contains("# Queries from queries", readme, StringComparison.Ordinal);
    Assert.True(readme.IndexOf("| `Base.m` |", StringComparison.Ordinal) < readme.IndexOf("| `Report.m` |", StringComparison.Ordinal));
    Assert.True(readme.IndexOf("| `fnClean.m` |", StringComparison.Ordinal) < readme.IndexOf("| `Report.m` |", StringComparison.Ordinal));
    Assert.Contains("**Sales:** a table or named range the workbook needs", readme, StringComparison.Ordinal);
    Assert.DoesNotContain("Unrelated", readme, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_workbook_works_as_a_folder_does_and_a_manifest_gives_the_choice()
  {
    var manifest = this.temp.WriteFile("choice.json", """{ "formatVersion": 1, "tool": "pqdoc", "workbook": "Queries.xlsx", "items": ["Chain_A"] }""");
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync("import", Fixtures.Workbook("Queries.xlsx"), "--manifest", manifest, "--bundle", Bundle));

    Assert.Contains("Chosen: Chain_A", app.Log.Messages(LogEventLevel.Information));
    Assert.True(File.Exists(Path.Combine(Bundle, "Chain_A.m")));
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("was made from", StringComparison.Ordinal));
  }

  [Fact]
  public async Task A_query_it_doesnt_have_stops_it_and_a_folder_of_other_files_is_left_alone()
  {
    using var app = App();
    Assert.Equal(ExitCodes.InputError, await app.RunAsync("import", Queries, "--items", "Nope", "--bundle", Bundle));
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("no query", StringComparison.Ordinal) && m.Contains("Nope", StringComparison.Ordinal));
    Assert.False(Directory.Exists(Bundle));

    var mine = this.temp.WriteFile("Shared/Mine.m", "1");
    Assert.Equal(ExitCodes.OutputRefused, await app.RunAsync("import", Queries, "--items", "Base", "--bundle", Bundle, "--force"));
    Assert.True(File.Exists(mine));
    Assert.False(File.Exists(Path.Combine(Bundle, "Base.m")));
  }

  [Fact]
  public async Task It_takes_a_manifest_or_names_but_not_neither()
  {
    using var app = App();

    Assert.Equal(ExitCodes.InvalidArguments, await app.RunAsync("import", Queries));
  }

  [Fact]
  public async Task Another_tools_manifest_is_refused()
  {
    var manifest = this.temp.WriteFile("choice.json", """{ "formatVersion": 1, "tool": "vbadoc", "items": [] }""");
    using var app = App();

    Assert.Equal(ExitCodes.InputError, await app.RunAsync("import", Queries, "--manifest", manifest));
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("vbadoc's choices", StringComparison.Ordinal));
  }
}
