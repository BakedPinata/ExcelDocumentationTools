using System.Text;
using PowerQueryDocumenter.Core.Analysis;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Core;

namespace PowerQueryDocumenter.Tests.Extraction;

/// <summary>Reading queries straight from a workbook (FR-IN-2 to FR-IN-4), on the sample workbooks and on broken ones.</summary>
public sealed class WorkbookReaderTests
{
  private static readonly IReadOnlyList<WorkbookQuery> Sample = WorkbookReader.Read(Fixtures.Workbook("Queries.xlsx"));

  private static string Expression(string name) => Sample.Single(q => q.Name == name).Expression;

  private static string ErrorFor(byte[] file) => Assert.Throws<ExtractionException>(() => WorkbookReader.Read(file)).Message;

  [Fact]
  public void Reads_every_query_in_the_sample_in_workbook_order()
  {
    Assert.Equal(
      new[]
      {
        "Companies", "Sales Data", "fnClean", "pRegion", "Chain_A", "Chain_B", "Chain_C", "Chain_D", "Chain_E",
        "Sales/Returns", "Discount%", "Manual Targets", "Notes", "Commented",
      },
      Sample.Select(q => q.Name));
  }

  [Fact]
  public void Each_expression_is_as_Excel_stores_it_without_its_name_or_final_semicolon()
  {
    Assert.Equal("let\r\n    Source = #\"Sales Data\"\r\nin\r\n    Source", Expression("Chain_A"));
    Assert.StartsWith("// Commented query", Expression("Commented")); // a comment straight after "="
    Assert.EndsWith("// comment on the last line", Expression("Commented"));
    Assert.EndsWith("on the last lines\r\n*/", Expression("Sales Data"));
    Assert.EndsWith("// Single line comment on the last line", Expression("Chain_E"));
    Assert.StartsWith("let", Expression("Chain_E")); // its description isn't part of it

    // A ";" and a line starting "shared" inside a text value don't split the query.
    Assert.Contains("Store note; check stock; reorder soon\r\nshared between regions", Expression("Notes"));
  }

  [Fact]
  public void A_description_is_read_from_the_attributes_before_its_query()
  {
    Assert.Equal("Revenue totals per company for the region in pRegion.", Sample.Single(q => q.Name == "Chain_E").Description);
    Assert.All(Sample.Where(q => q.Name != "Chain_E"), q => Assert.Null(q.Description));
  }

  [Fact]
  public void A_binary_workbook_holds_the_same_queries()
  {
    Assert.Equal(Sample, WorkbookReader.Read(Fixtures.Workbook("Queries.xlsb")));
  }

  [Fact]
  public void The_sample_gives_the_dependencies_worked_out_by_hand()
  {
    var graph = new DependencyAnalyser(Logger.None).Analyse(Sample.Select(q => new QuerySource(q.Name, q.Expression)));
    var expected = Fixtures.Graph(Fixtures.WorkbookEdges, Fixtures.WorkbookIsolated);

    Assert.Equal(expected.Edges.Select(e => $"{e.Upstream}>{e.Downstream}").Order(), graph.Edges.Select(e => $"{e.Upstream}>{e.Downstream}").Order());
    Assert.Equal(expected.Nodes.Count, graph.Nodes.Count);
  }

  [Fact]
  public void Reads_a_workbook_while_another_program_has_it_open()
  {
    using var temp = new TempDirectory();
    var copy = temp.Combine("Queries.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), copy);
    using var excel = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    Assert.Equal(14, WorkbookReader.Read(copy).Count);
  }

  [Fact]
  public void The_custom_XML_part_can_be_UTF8_as_well_as_UTF16()
  {
    var mashup = Convert.ToBase64String(MashupWorkbook.Sections(0, MashupWorkbook.PackageParts("section Section1;\r\nshared A = 1;")));

    var queries = WorkbookReader.Read(MashupWorkbook.FromBase64(mashup, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

    Assert.Equal("A", Assert.Single(queries).Name);
  }

  [Fact]
  public void A_file_that_is_not_a_workbook_is_reported_as_such()
  {
    Assert.Contains("not an Excel workbook", ErrorFor("just some text"u8.ToArray()));
  }

  [Fact]
  public void A_compound_file_is_reported_as_encrypted_or_an_older_xls()
  {
    var compoundFile = new byte[512];
    new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(compoundFile, 0);

    var error = ErrorFor(compoundFile);

    Assert.Contains("encrypted", error);
    Assert.Contains(".xls", error);
  }

  [Fact]
  public void A_workbook_without_queries_says_so()
  {
    Assert.Contains("has no Power Query queries", ErrorFor(MashupWorkbook.Zip(new() { ["xl/workbook.xml"] = "<workbook/>"u8.ToArray() })));
    Assert.Contains("has no Power Query queries", ErrorFor(MashupWorkbook.FromXml("<Settings xmlns=\"urn:other\"/>")));
    Assert.Contains("has no Power Query queries", ErrorFor(MashupWorkbook.With("section Section1;")));
  }

  [Fact]
  public void Custom_XML_that_is_not_well_formed_is_someone_elses_and_is_passed_over()
  {
    var mashup = Convert.ToBase64String(MashupWorkbook.Sections(0, MashupWorkbook.PackageParts("section Section1;\r\nshared A = 1;")));
    var mashupXml = Encoding.Unicode.GetPreamble()
      .Concat(Encoding.Unicode.GetBytes($"<DataMashup xmlns=\"{WorkbookReader.DataMashupNamespace}\">{mashup}</DataMashup>"))
      .ToArray();
    var file = MashupWorkbook.Zip(new() { ["customXml/item1.xml"] = "<not closed"u8.ToArray(), ["customXml/item2.xml"] = mashupXml });

    Assert.Equal("A", Assert.Single(WorkbookReader.Read(file)).Name);
  }

  [Fact]
  public void Damage_is_reported_rather_than_read_past()
  {
    var package = MashupWorkbook.PackageParts("section Section1;\r\nshared A = 1;");

    Assert.Contains("isn't valid base64", ErrorFor(MashupWorkbook.FromBase64("not base64 at all!")));
    Assert.Contains("too short", ErrorFor(MashupWorkbook.FromMashup([0, 0])));
    Assert.Contains("version pqdoc doesn't know (1)", ErrorFor(MashupWorkbook.FromMashup(MashupWorkbook.Sections(1, package))));

    // A length longer than what's left, in the first and in the last section.
    var tooLong = MashupWorkbook.Sections(0, package);
    BitConverter.GetBytes(1_000_000u).CopyTo(tooLong, 4);
    Assert.Contains("Package Parts section says it's 1000000 bytes long", ErrorFor(MashupWorkbook.FromMashup(tooLong)));
    var lastTooLong = MashupWorkbook.Sections(0, package);
    BitConverter.GetBytes(10u).CopyTo(lastTooLong, lastTooLong.Length - 4);
    Assert.Contains("Permission Bindings section says it's 10 bytes long, but only 0 are left", ErrorFor(MashupWorkbook.FromMashup(lastTooLong)));

    // Cut off just before the last section's length (the last section is empty, so its length is the last 4 bytes).
    Assert.Contains("ends before the length of its Permission Bindings section", ErrorFor(MashupWorkbook.FromMashup(MashupWorkbook.Sections(0, package)[..^4])));

    Assert.Contains("package isn't a valid zip file", ErrorFor(MashupWorkbook.FromMashup(MashupWorkbook.Sections(0, "PK not a zip"u8.ToArray()))));
    Assert.Contains("has no Formulas/Section1.m", ErrorFor(MashupWorkbook.FromMashup(MashupWorkbook.Sections(0, MashupWorkbook.PackageParts(null)))));
    Assert.Contains("isn't well-formed XML", ErrorFor(MashupWorkbook.FromXml($"<DataMashup xmlns=\"{WorkbookReader.DataMashupNamespace}\">AAAA<unclosed></DataMashup>")));
  }

  [Fact]
  public void A_missing_file_is_reported_with_its_path()
  {
    using var temp = new TempDirectory();
    var missing = temp.Combine("missing.xlsx");

    Assert.Contains("missing.xlsx", Assert.Throws<ExtractionException>(() => WorkbookReader.Read(missing)).Message);
  }
}
