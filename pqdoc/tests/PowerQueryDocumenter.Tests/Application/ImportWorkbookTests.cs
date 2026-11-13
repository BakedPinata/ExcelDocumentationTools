using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CodeDocumenter.Shared;
using CodeDocumenter.Shared.Importing;
using CodeDocumenter.Shared.Workbooks;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Application;

/// <summary><c>pqdoc import</c> writing into a workbook: a new one, a copy of a target, or the target itself.</summary>
public sealed class ImportWorkbookTests : IDisposable
{
  private const string BaseText = "let\r\n    Source = 1\r\nin\r\n    Source";
  private const string ReportText = "let\r\n    Source = Base\r\nin\r\n    fnClean(Source)";
  private const string CleanText = "(value) => value";

  private readonly TempDirectory temp = new();
  private FakeUserInteraction interaction = new(isInteractive: false);

  public ImportWorkbookTests()
  {
    this.temp.WriteFile("queries/Base.m", BaseText + "\r\n");
    this.temp.WriteFile("queries/fnClean.m", CleanText + "\r\n");
    this.temp.WriteFile("queries/Report.m", ReportText + "\r\n");
    this.temp.WriteFile("queries/Unrelated.m", "2\r\n");
  }

  private string Source => this.temp.Combine("queries");

  private string Target => this.temp.Combine("Target.xlsx");

  private string Copy => this.temp.Combine("Target-imported.xlsx");

  private string New => this.temp.Combine("Toolkit.xlsx");

  public void Dispose() => this.temp.Dispose();

  private async Task<(int Exit, TestLog Log)> RunFrom(string source, params string[] args)
  {
    using var app = new TestApp(this.temp.Path, interaction: this.interaction);
    var exit = await app.RunAsync(["import", source, .. args]);
    return (exit, app.Log);
  }

  private Task<(int Exit, TestLog Log)> Run(params string[] args) => RunFrom(Source, args);

  private void Answer(params ClashAnswer[] answers)
  {
    this.interaction = new FakeUserInteraction(isInteractive: true);
    foreach (var answer in answers)
    {
      this.interaction.ClashAnswers.Enqueue(answer);
    }
  }

  private void WriteTarget(params (string Name, string Text)[] queries)
  {
    var section = new StringBuilder("section Section1;");
    foreach (var (name, text) in queries)
    {
      section.Append($"\r\n\r\nshared {name} = {text};");
    }

    File.WriteAllBytes(Target, WorkbookWith(section.ToString()));
  }

  // A workbook Excel could have written, as far as the tool looks: a blank one holding a DataMashup with these queries.
  private static byte[] WorkbookWith(string section, string permissions = "")
  {
    var package = BlankWorkbook.Build(macroEnabled: false);
    var mashup = MashupWorkbook.Sections(0, MashupWorkbook.PackageParts(section), Encoding.UTF8.GetBytes(permissions), [], [1, 2, 3]);
    package.Set("customXml/item1.xml", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(
      $"<?xml version=\"1.0\" encoding=\"utf-16\"?><DataMashup xmlns=\"{WorkbookReader.DataMashupNamespace}\">{Convert.ToBase64String(mashup)}</DataMashup>")]);
    return package.Write();
  }

  private static IReadOnlyList<WorkbookQuery> Read(string path) => WorkbookReader.Read(path);

  private static IEnumerable<string> Names(string path) => Read(path).Select(q => q.Name).Order(StringComparer.OrdinalIgnoreCase);

  private static string TextOf(string path, string query) => Read(path).Single(q => q.Name == query).Expression;

  // The metadata items and bindings of the workbook's mashup.
  private static (XDocument Metadata, byte[][] Sections) Mashup(string path)
  {
    var data = Convert.FromBase64String(OpcPackage.Read(File.ReadAllBytes(path)).Xml("customXml/item1.xml").Root!.Value);
    var sections = new byte[4][];
    var at = 4;
    for (var i = 0; i < sections.Length; i++)
    {
      var size = BitConverter.ToInt32(data, at);
      sections[i] = data[(at + 4)..(at + 4 + size)];
      at += 4 + size;
    }

    Assert.Equal(data.Length, at);
    var length = BitConverter.ToInt32(sections[2], 4);
    return (XDocument.Load(new MemoryStream(sections[2], 8, length)), sections);
  }

  private static XElement ItemOf(string path, string itemPath) =>
    Mashup(path).Metadata.Descendants("Item").Single(i => (string?)i.Element("ItemLocation")?.Element("ItemPath") == itemPath);

  private static string? Entry(XElement item, string type) =>
    (string?)item.Element("StableEntries")!.Elements("Entry").SingleOrDefault(e => (string?)e.Attribute("Type") == type)?.Attribute("Value");

  private static IReadOnlyList<XElement> Connections(string path)
  {
    var package = OpcPackage.Read(File.ReadAllBytes(path));
    return package.Has("xl/connections.xml") ? [.. package.Xml("xl/connections.xml").Root!.Elements(OpcPackage.Main + "connection")] : [];
  }

  [Fact]
  public async Task A_new_workbook_holds_what_was_chosen_and_what_it_uses_and_no_more()
  {
    var (exit, log) = await Run("--items", "Report", "--new", New);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Equal(["Base", "fnClean", "Report"], Names(New));
    Assert.Equal(ReportText, TextOf(New, "Report"));
    Assert.Contains($"Wrote {New}", log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public async Task A_new_query_is_connection_only_with_a_connection_and_the_right_result_type()
  {
    await Run("--items", "Report", "--new", New);

    var item = ItemOf(New, "Section1/Report");
    Assert.Equal("sConnectionOnly", Entry(item, "FillObjectType"));
    Assert.Equal("l0", Entry(item, "FillEnabled"));
    Assert.Equal("sTable", Entry(item, "ResultType"));
    Assert.Equal("sFunction", Entry(ItemOf(New, "Section1/fnClean"), "ResultType"));
    Assert.Equal(["Query - Report", "Query - Base", "Query - fnClean"], Connections(New).Select(c => (string?)c.Attribute("name")).OrderBy(n => n != "Query - Report"));
  }

  [Fact]
  public async Task A_new_workbooks_parts_are_tied_together_as_a_package_needs()
  {
    await Run("--items", "Base", "--new", New);

    var package = OpcPackage.Read(File.ReadAllBytes(New));
    Assert.Contains(package.RelationshipsOf("xl/workbook.xml"), r => r.Target == "connections.xml");
    Assert.Contains(package.RelationshipsOf("xl/workbook.xml"), r => r.Target == "../customXml/item1.xml");
    Assert.Contains(package.RelationshipsOf("customXml/item1.xml"), r => r.Target == "itemProps1.xml");
    var overrides = package.Xml(OpcPackage.ContentTypesPart).Root!.Elements(OpcPackage.ContentTypes + "Override").Select(o => (string?)o.Attribute("PartName"));
    Assert.Contains("/xl/connections.xml", overrides);
    Assert.Contains("/customXml/itemProps1.xml", overrides);
    Assert.True(package.Get("customXml/item1.xml").AsSpan().StartsWith(Encoding.Unicode.GetPreamble()), "Excel writes it as UTF-16");
  }

  [Fact]
  public async Task An_xlsm_can_be_built_and_is_macro_enabled()
  {
    var file = this.temp.Combine("Toolkit.xlsm");

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--new", file)).Exit);

    var overrides = OpcPackage.Read(File.ReadAllBytes(file)).Xml(OpcPackage.ContentTypesPart).Root!.Elements(OpcPackage.ContentTypes + "Override");
    Assert.Contains(overrides, o => (string?)o.Attribute("ContentType") == BlankWorkbook.MacroEnabledContentType);
  }

  [Theory]
  [InlineData("Toolkit.xlsb")]
  [InlineData("Toolkit")]
  public async Task A_new_workbook_has_to_be_a_kind_that_can_be_built(string name)
  {
    var (exit, log) = await Run("--items", "Base", "--new", this.temp.Combine(name));

    Assert.Equal(ExitCodes.InvalidArguments, exit);
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains(".xlsx or .xlsm", StringComparison.Ordinal));
    Assert.False(File.Exists(this.temp.Combine(name)));
  }

  [Fact]
  public async Task The_source_is_never_written_over()
  {
    var source = this.temp.Combine("Source.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), source);
    var before = File.ReadAllBytes(source);

    var (exit, _) = await RunFrom(source, "--items", "Chain_A", "--into", source, "--in-place", "--force");

    Assert.Equal(ExitCodes.InvalidArguments, exit);
    Assert.Equal(before, File.ReadAllBytes(source));
  }

  [Fact]
  public async Task A_copy_goes_beside_the_target_which_is_left_byte_for_byte_as_it_was()
  {
    WriteTarget(("Mine", "3"));
    var before = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Report", "--into", Target)).Exit);

    Assert.Equal(["Base", "fnClean", "Mine", "Report"], Names(Copy));
    Assert.Equal("3", TextOf(Copy, "Mine"));
    Assert.Equal(before, File.ReadAllBytes(Target));
  }

  [Fact]
  public async Task A_real_workbook_keeps_its_queries_and_every_part_it_didnt_need_to_change()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    var before = OpcPackage.Read(File.ReadAllBytes(Target));
    var queries = Read(Target);

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target)).Exit);

    var after = OpcPackage.Read(File.ReadAllBytes(Copy));
    string[] changed = ["customXml/item1.xml", "xl/connections.xml"];
    foreach (var name in before.Names.Except(changed))
    {
      Assert.Equal(before.Get(name), after.Get(name));
    }

    var read = Read(Copy);
    foreach (var query in queries)
    {
      Assert.Equal(query, read.Single(q => q.Name == query.Name));
    }

    Assert.Equal(queries.Count + 1, read.Count);
  }

  [Fact]
  public async Task Permissions_bindings_and_the_metadatas_content_come_through()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    var (metadata, before) = Mashup(Target);
    var xmlLength = BitConverter.ToInt32(before[2], 4);

    await Run("--items", "Base", "--into", Target);

    var (_, after) = Mashup(Copy);
    Assert.Equal(before[1], after[1]);
    Assert.Equal(before[3], after[3]);
    Assert.Equal(before[2][(8 + xmlLength)..], after[2][(8 + BitConverter.ToInt32(after[2], 4))..]);
    Assert.NotNull(metadata.Root);
    Assert.NotNull(Entry(ItemOf(Copy, ""), "QueryGroups"));
  }

  [Fact]
  public async Task An_xlsb_stays_an_xlsb_and_its_binary_connections_are_left_alone()
  {
    var target = this.temp.Combine("Target.xlsb");
    File.Copy(Fixtures.Workbook("Queries.xlsb"), target);
    var before = OpcPackage.Read(File.ReadAllBytes(target));

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", target)).Exit);

    var copy = this.temp.Combine("Target-imported.xlsb");
    Assert.Contains("Base", Names(copy));
    var after = OpcPackage.Read(File.ReadAllBytes(copy));
    Assert.Equal(before.Get("xl/connections.bin"), after.Get("xl/connections.bin"));
  }

  [Fact]
  public async Task A_workbook_without_queries_gets_them()
  {
    File.WriteAllBytes(Target, BlankWorkbook.Build(macroEnabled: false).Write());

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target)).Exit);

    Assert.Equal(["Base"], Names(Copy));
  }

  [Fact]
  public async Task A_workbook_can_be_the_source_and_its_descriptions_and_result_types_come_along()
  {
    var (exit, _) = await RunFrom(Fixtures.Workbook("Queries.xlsx"), "--items", "Chain_E,pRegion", "--new", New);

    Assert.Equal(ExitCodes.Success, exit);
    var original = WorkbookReader.Read(Fixtures.Workbook("Queries.xlsx")).Single(q => q.Name == "Chain_E");
    var written = Read(New).Single(q => q.Name == "Chain_E");
    Assert.Equal(original.Expression, written.Expression);
    Assert.Equal(original.Description, written.Description);
    Assert.Equal("sText", Entry(ItemOf(New, "Section1/pRegion"), "ResultType"));
  }

  [Fact]
  public async Task Names_that_need_quoting_are_quoted_and_read_back()
  {
    var (exit, _) = await RunFrom(Fixtures.Workbook("Queries.xlsx"), "--items", "Sales/Returns,Discount%,Sales Data", "--new", New);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Contains("Sales/Returns", Names(New));
    Assert.Contains("Discount%", Names(New));
    Assert.NotNull(ItemOf(New, "Section1/Sales%2FReturns"));
    var location = (string?)Connections(New).Single(c => (string?)c.Attribute("name") == "Query - Sales Data").Element(OpcPackage.Main + "dbPr")!.Attribute("connection");
    Assert.Contains("Location=\"Sales Data\";", location, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_query_named_as_a_keyword_is_quoted()
  {
    this.temp.WriteFile("queries/let.m", "1\r\n");

    Assert.Equal(ExitCodes.Success, (await Run("--items", "let", "--new", New)).Exit);

    Assert.Equal(["let"], Names(New));
  }

  [Fact]
  public async Task A_line_comment_at_the_end_doesnt_swallow_the_semicolon()
  {
    this.temp.WriteFile("queries/Commented.m", "1 // the end\r\n");

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Commented,Base", "--new", New)).Exit);

    Assert.Equal("1 // the end", TextOf(New, "Commented"));
    Assert.Equal(BaseText, TextOf(New, "Base"));
  }

  [Fact]
  public async Task A_file_written_with_a_byte_order_mark_or_bare_line_feeds_comes_through_as_a_query()
  {
    File.WriteAllBytes(this.temp.Combine("queries/Exported.m"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("let\nA = \"x\ny\"\nin\nA\n")]);

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Exported", "--new", New)).Exit);

    Assert.Equal("let\r\nA = \"x\ny\"\r\nin\r\nA", TextOf(New, "Exported"));
  }

  [Fact]
  public async Task A_query_that_would_break_the_section_stops_it_with_nothing_written()
  {
    this.temp.WriteFile("queries/Broken.m", "1; shared Other = 2\r\n");

    var (exit, _) = await Run("--items", "Broken", "--new", New);

    Assert.Equal(ExitCodes.InputError, exit);
    Assert.False(File.Exists(New));
  }

  [Fact]
  public async Task A_connection_name_already_taken_gets_a_number()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    var package = OpcPackage.Read(File.ReadAllBytes(Target));
    var connections = package.Xml("xl/connections.xml");
    connections.Root!.Elements().First().SetAttributeValue("name", "Query - Base");
    package.SetXml("xl/connections.xml", connections);
    File.WriteAllBytes(Target, package.Write());

    await Run("--items", "Base", "--into", Target);

    Assert.Contains(Connections(Copy), c => (string?)c.Attribute("name") == "Query - Base (1)");
    Assert.Equal(Connections(Copy).Count, Connections(Copy).Select(c => (string?)c.Attribute("id")).Distinct().Count());
  }

  [Fact]
  public async Task An_encrypted_target_and_an_old_one_are_refused_saying_why()
  {
    File.WriteAllBytes(Target, [0x01, 0x02, 0x03, 0x04]);

    var (exit, log) = await Run("--items", "Base", "--into", Target);

    Assert.Equal(ExitCodes.InputError, exit);
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("not an Excel workbook", StringComparison.Ordinal));
    Assert.False(File.Exists(Copy));
  }

  [Fact]
  public async Task An_add_in_isnt_a_target_as_only_vbadoc_writes_into_them()
  {
    var target = this.temp.Combine("Tools.xlam");
    File.WriteAllBytes(target, BlankWorkbook.Build(macroEnabled: true).Write());

    var (exit, log) = await Run("--items", "Base", "--into", target);

    Assert.Equal(ExitCodes.InputError, exit);
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("queries can only be written into .xlsx, .xlsm, .xlsb files", StringComparison.Ordinal));
    Assert.False(File.Exists(this.temp.Combine("Tools-imported.xlam")));
  }

  [Fact]
  public async Task An_identical_query_is_left_alone_without_asking_and_counted()
  {
    WriteTarget(("Base", BaseText.Replace("\r\n", "\n", StringComparison.Ordinal)));

    var (exit, log) = await Run("--items", "Report", "--into", Target);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Contains("Already there, identical: Base", log.Messages(LogEventLevel.Information));
    Assert.Contains("Adding, as connection only: Report, fnClean", log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public async Task When_everything_is_there_already_nothing_is_written()
  {
    WriteTarget(("Base", BaseText));

    var (exit, log) = await Run("--items", "Base", "--into", Target);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.False(File.Exists(Copy));
    Assert.Contains("The target needs nothing from this, so nothing was written", log.Messages(LogEventLevel.Information));
  }

  [Fact]
  public async Task A_clash_with_nobody_to_ask_stops_with_nothing_written_and_force_doesnt_answer_it()
  {
    WriteTarget(("Base", "99"));

    var (exit, log) = await Run("--items", "Report", "--into", Target, "--force");

    Assert.Equal(ExitCodes.OutputRefused, exit);
    Assert.False(File.Exists(Copy));
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("--on-clash", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Skip_keeps_the_targets_version_and_says_what_that_means()
  {
    WriteTarget(("Base", "99"));
    Answer(ClashAnswer.Skip);

    var (exit, log) = await Run("--items", "Report", "--into", Target);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Equal("99", TextOf(Copy, "Base"));
    Assert.Contains("Report", Names(Copy));
    Assert.Contains("Skipped, keeping the target's: Base", log.Messages(LogEventLevel.Warning));
    Assert.Equal("Keeps the target's version, which may differ, for Report to use.", Assert.Single(this.interaction.ClashesAsked).IfSkipped);
  }

  [Fact]
  public async Task Overwrite_replaces_the_text_and_says_what_uses_it_there()
  {
    WriteTarget(("Base", "99"), ("Uses", "Base + 1"));
    Answer(ClashAnswer.Overwrite);

    var (exit, log) = await Run("--items", "Base", "--into", Target);

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Equal(BaseText, TextOf(Copy, "Base"));
    Assert.Equal("Base + 1", TextOf(Copy, "Uses"));
    Assert.Contains("Overwriting, each still loading where the target's did: Base", log.Messages(LogEventLevel.Information));
    Assert.Equal("Replaces the target's version, used there by Uses.", Assert.Single(this.interaction.ClashesAsked).IfOverwritten);
  }

  [Fact]
  public async Task Overwrite_keeps_where_the_query_loads_and_its_connection_and_drops_its_old_steps()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    var connections = Connections(Target).Count;
    var id = Entry(ItemOf(Target, "Section1/Sales%20Data"), "QueryID");
    this.temp.WriteFile("queries/Sales Data.m", "(x) => x\r\n");

    var (exit, _) = await Run("--items", "Sales Data", "--into", Target, "--on-clash", "overwrite");

    Assert.Equal(ExitCodes.Success, exit);
    Assert.Equal("(x) => x", TextOf(Copy, "Sales Data"));
    var item = ItemOf(Copy, "Section1/Sales%20Data");
    Assert.Equal(id, Entry(item, "QueryID"));
    Assert.Equal("sTable", Entry(item, "FillObjectType"));
    Assert.Equal("sSales_Data", Entry(item, "FillTarget"));
    Assert.Equal("sFunction", Entry(item, "ResultType"));
    Assert.DoesNotContain(Mashup(Copy).Metadata.Descendants("ItemPath"), p => p.Value.StartsWith("Section1/Sales%20Data/", StringComparison.Ordinal));
    Assert.Contains(Mashup(Copy).Metadata.Descendants("ItemPath"), p => p.Value == "Section1/Companies/Source");
    Assert.Equal(connections, Connections(Copy).Count);
  }

  [Fact]
  public async Task Overwrite_replaces_a_description_and_leaves_the_rest_of_the_section_alone()
  {
    File.WriteAllBytes(Target, WorkbookWith("section Section1;\r\n\r\n[ Description = \"Old; words\" ]\r\nshared Base = 99;\r\n\r\n// kept\r\nshared Other = \"a;b\";\r\n"));

    await Run("--items", "Base", "--into", Target, "--on-clash", "overwrite");

    var read = Read(Copy);
    Assert.Null(read.Single(q => q.Name == "Base").Description);
    Assert.Equal(BaseText, read.Single(q => q.Name == "Base").Expression);
    Assert.Equal("\"a;b\"", read.Single(q => q.Name == "Other").Expression);
  }

  [Fact]
  public async Task Skip_All_and_Overwrite_All_answer_for_the_rest()
  {
    WriteTarget(("Base", "98"), ("fnClean", "99"));
    Answer(ClashAnswer.OverwriteAll);

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Report", "--into", Target)).Exit);
    Assert.Single(this.interaction.ClashesAsked);
    Assert.Equal(BaseText, TextOf(Copy, "Base"));
    Assert.Equal(CleanText, TextOf(Copy, "fnClean"));

    File.Delete(Copy);
    Answer(ClashAnswer.SkipAll);
    Assert.Equal(ExitCodes.Success, (await Run("--items", "Report", "--into", Target)).Exit);
    Assert.Single(this.interaction.ClashesAsked);
    Assert.Equal("98", TextOf(Copy, "Base"));
  }

  [Fact]
  public async Task Quit_stops_with_nothing_written_though_earlier_answers_were_given()
  {
    WriteTarget(("Base", "98"), ("fnClean", "99"));
    Answer(ClashAnswer.Overwrite, ClashAnswer.Quit);

    Assert.Equal(ExitCodes.OutputRefused, (await Run("--items", "Report", "--into", Target)).Exit);

    Assert.False(File.Exists(Copy));
  }

  [Theory]
  [InlineData("overwrite")]
  [InlineData("skip")]
  public async Task On_clash_answers_for_an_unattended_run(string behaviour)
  {
    WriteTarget(("Base", "99"));

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Report", "--into", Target, "--on-clash", behaviour)).Exit);

    Assert.Equal(behaviour == "overwrite" ? BaseText : "99", TextOf(Copy, "Base"));
  }

  [Fact]
  public async Task A_function_and_a_query_of_the_same_name_are_said_to_differ_in_kind()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    this.temp.WriteFile("queries/fnClean.m", "1\r\n");
    Answer(ClashAnswer.Skip);

    await Run("--items", "fnClean", "--into", Target);

    Assert.Equal("The target's fnClean is a function, and the one in queries is a query.", Assert.Single(this.interaction.ClashesAsked).Problem);
  }

  [Fact]
  public async Task A_name_spelled_differently_is_a_clash_even_when_the_text_is_the_same()
  {
    WriteTarget(("BASE", BaseText));
    Answer(ClashAnswer.Skip);

    await Run("--items", "Base", "--into", Target);

    Assert.Contains("case-sensitive", Assert.Single(this.interaction.ClashesAsked).Problem, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Overwriting_a_name_spelled_differently_keeps_the_targets_spelling()
  {
    WriteTarget(("BASE", "99"), ("Uses", "BASE + 1"));

    await Run("--items", "Base", "--into", Target, "--on-clash", "overwrite");

    Assert.Equal(["BASE", "Uses"], Names(Copy));
    Assert.Equal(BaseText, TextOf(Copy, "BASE"));
  }

  [Fact]
  public async Task An_earlier_copy_is_covered_by_on_exists_and_force()
  {
    WriteTarget(("Mine", "3"));
    File.WriteAllBytes(Copy, [1, 2, 3]);

    Assert.Equal(ExitCodes.OutputRefused, (await Run("--items", "Base", "--into", Target)).Exit);
    Assert.Equal([1, 2, 3], File.ReadAllBytes(Copy));

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target, "--on-exists", "skip")).Exit);
    Assert.Equal([1, 2, 3], File.ReadAllBytes(Copy));

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target, "--force")).Exit);
    Assert.Contains("Base", Names(Copy));
  }

  [Fact]
  public async Task In_place_asks_first_and_a_no_changes_nothing()
  {
    WriteTarget(("Mine", "3"));
    var before = File.ReadAllBytes(Target);
    this.interaction = new FakeUserInteraction(isInteractive: true);
    this.interaction.Confirmations.Enqueue(false);

    var (exit, _) = await Run("--items", "Report", "--into", Target, "--in-place");

    Assert.Equal(ExitCodes.OutputRefused, exit);
    Assert.Equal(before, File.ReadAllBytes(Target));
    Assert.Contains("3 queries added", Assert.Single(this.interaction.Questions), StringComparison.Ordinal);
    Assert.False(File.Exists(Copy));
  }

  [Fact]
  public async Task In_place_a_yes_changes_the_target_even_an_xlsx()
  {
    WriteTarget(("Mine", "3"));
    this.interaction = new FakeUserInteraction(isInteractive: true);
    this.interaction.Confirmations.Enqueue(true);

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target, "--in-place")).Exit);

    Assert.Equal(["Base", "Mine"], Names(Target));
    Assert.False(File.Exists(Copy));
  }

  [Fact]
  public async Task In_place_force_answers_yes_and_with_nobody_to_ask_it_stops_without_it()
  {
    WriteTarget(("Mine", "3"));
    var before = File.ReadAllBytes(Target);

    Assert.Equal(ExitCodes.OutputRefused, (await Run("--items", "Base", "--into", Target, "--in-place")).Exit);
    Assert.Equal(before, File.ReadAllBytes(Target));

    Assert.Equal(ExitCodes.Success, (await Run("--items", "Base", "--into", Target, "--in-place", "--force")).Exit);
    Assert.Equal(["Base", "Mine"], Names(Target));
  }

  [Fact]
  public async Task In_place_a_target_held_open_stops_it_before_anything_is_asked()
  {
    WriteTarget(("Mine", "3"));
    using var open = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read);
    this.interaction = new FakeUserInteraction(isInteractive: true);

    var (exit, _) = await Run("--items", "Base", "--into", Target, "--in-place");

    Assert.Equal(ExitCodes.ExportFailed, exit);
    Assert.Empty(this.interaction.Questions);
  }

  [Fact]
  public async Task A_target_set_to_ignore_privacy_levels_is_warned_about()
  {
    File.WriteAllBytes(Target, WorkbookWith("section Section1;\r\n\r\nshared Mine = 3;", "<PermissionList><FirewallEnabled>false</FirewallEnabled></PermissionList>"));

    var (_, log) = await Run("--items", "Base", "--into", Target);

    Assert.Contains(log.Messages(LogEventLevel.Warning), m => m.Contains("ignore privacy levels", StringComparison.Ordinal));
  }

  [Fact]
  public async Task A_target_with_ordinary_permissions_isnt_warned_about()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);

    var (_, log) = await Run("--items", "Base", "--into", Target);

    Assert.DoesNotContain(log.Messages(LogEventLevel.Warning), m => m.Contains("privacy", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Only_one_place_to_write_can_be_given()
  {
    Assert.Equal(ExitCodes.InvalidArguments, (await Run("--items", "Base", "--new", New, "--into", Target)).Exit);
    Assert.Equal(ExitCodes.InvalidArguments, (await Run("--items", "Base", "--new", New, "--bundle", this.temp.Combine("B"))).Exit);
  }

  [Fact]
  public async Task The_zip_inside_the_mashup_keeps_its_other_entries()
  {
    File.Copy(Fixtures.Workbook("Queries.xlsx"), Target);
    static byte[] Entry(byte[] packageParts, string name)
    {
      using var zip = new ZipArchive(new MemoryStream(packageParts), ZipArchiveMode.Read);
      using var stream = zip.GetEntry(name)!.Open();
      var buffer = new MemoryStream();
      stream.CopyTo(buffer);
      return buffer.ToArray();
    }

    var before = Mashup(Target).Sections[0];

    await Run("--items", "Base", "--into", Target);

    var after = Mashup(Copy).Sections[0];
    Assert.Equal(Entry(before, "Config/Package.xml"), Entry(after, "Config/Package.xml"));
    Assert.Equal(Entry(before, "[Content_Types].xml"), Entry(after, "[Content_Types].xml"));
  }
}
