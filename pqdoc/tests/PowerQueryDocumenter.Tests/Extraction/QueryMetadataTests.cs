using System.Text;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Extraction;

/// <summary>Query groups and result types from a workbook's metadata (FR-HTML-11).</summary>
public sealed class QueryMetadataTests
{
  private static readonly Guid Staging = Guid.Parse("c5506b5e-2b6a-43a6-840d-f8043dd958e9");
  private static readonly Guid Reference = Guid.Parse("9227dae9-f6c3-4d78-a93c-e3938794b77c");

  /// <summary>A metadata section: version, the XML after its length, then an empty content part.</summary>
  private static byte[] Section(string xml)
  {
    var bytes = Encoding.UTF8.GetBytes(xml);
    return [.. BitConverter.GetBytes(0u), .. BitConverter.GetBytes((uint)bytes.Length), .. bytes, .. BitConverter.GetBytes(0u)];
  }

  /// <summary>A QueryGroups list laid out as Excel writes it, with optional bytes left over.</summary>
  private static string GroupList(byte[]? extra, params (Guid Id, string Name, int Order)[] groups)
  {
    using var memory = new MemoryStream();
    using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
    {
      writer.Write(groups.Length);
      foreach (var (id, name, order) in groups)
      {
        writer.Write(0);
        writer.Write(id.ToByteArray());
        writer.Write(name);
        writer.Write(string.Empty);
        writer.Write((byte)0);
        writer.Write(order);
      }

      writer.Write(extra ?? []);
    }

    return Convert.ToBase64String(memory.ToArray());
  }

  private static string Xml(string groupList, params (string Path, string Entries)[] items) =>
    "<?xml version=\"1.0\" encoding=\"utf-8\"?><LocalPackageMetadataFile><Items>"
    + $"<Item><ItemLocation><ItemType>AllFormulas</ItemType><ItemPath /></ItemLocation><StableEntries><Entry Type=\"QueryGroups\" Value=\"s{groupList}\" /></StableEntries></Item>"
    + string.Concat(items.Select(i => $"<Item><ItemLocation><ItemType>Formula</ItemType><ItemPath>{i.Path}</ItemPath></ItemLocation><StableEntries>{i.Entries}</StableEntries></Item>"))
    + "</Items></LocalPackageMetadataFile>";

  private static string Entry(string type, string value) => $"<Entry Type=\"{type}\" Value=\"{value}\" />";

  [Theory]
  [InlineData("Queries.xlsx")]
  [InlineData("Queries.xlsb")]
  public void The_sample_workbooks_groups_and_function_are_read(string workbook)
  {
    var queries = WorkbookReader.Read(Fixtures.Workbook(workbook)).ToDictionary(q => q.Name);

    Assert.Equal(new QueryGroup("Staging", 0), queries["Chain_A"].Group);
    Assert.Equal(new QueryGroup("Staging", 0), queries["Chain_D"].Group);
    Assert.Equal(new QueryGroup("Reference", 1), queries["Discount%"].Group);
    Assert.Equal(new QueryGroup("Reference", 1), queries["Manual Targets"].Group);
    Assert.Null(queries["Chain_E"].Group);
    Assert.Null(queries["Sales/Returns"].Group);
    Assert.Equal("Function", queries["fnClean"].ResultType);
    Assert.Equal("Table", queries["Sales Data"].ResultType);
    Assert.Equal("Text", queries["pRegion"].ResultType);
  }

  [Fact]
  public void Names_are_decoded_and_steps_are_not_queries()
  {
    var xml = Xml(
      GroupList(null, (Reference, "Reference", 1)),
      ("Section1/Sales%2FReturns", Entry("ResultType", "sTable") + Entry("QueryGroupID", $"s{Reference}")),
      ("Section1/Sales%2FReturns/Source", Entry("ResultType", "sText")));

    var metadata = QueryMetadata.Read(Section(xml));

    var entry = Assert.Single(metadata);
    Assert.Equal("Sales/Returns", entry.Key);
    Assert.Equal(new QueryMetadataEntry(new QueryGroup("Reference", 1), "Table"), entry.Value);
  }

  [Fact]
  public void A_group_list_in_an_unknown_layout_means_no_groups_but_keeps_the_result_types()
  {
    var xml = Xml(
      GroupList([0xFF], (Staging, "Staging", 0)),
      ("Section1/Chain_A", Entry("ResultType", "sTable") + Entry("QueryGroupID", $"s{Staging}")));

    var entry = Assert.Single(QueryMetadata.Read(Section(xml)));

    Assert.Equal(new QueryMetadataEntry(null, "Table"), entry.Value);
  }

  [Fact]
  public void A_query_in_a_group_the_list_does_not_have_is_in_no_group()
  {
    var xml = Xml(
      GroupList(null, (Staging, "Staging", 0)),
      ("Section1/Chain_A", Entry("QueryGroupID", $"s{Reference}")));

    Assert.Null(Assert.Single(QueryMetadata.Read(Section(xml))).Value.Group);
  }

  [Theory]
  [InlineData(new byte[0])]
  [InlineData(new byte[] { 0, 0, 0, 0, 255, 255, 255, 127 })]
  [InlineData(new byte[] { 0, 0, 0, 0, 3, 0, 0, 0, 0x3C, 0x61, 0x3E })]
  public void Metadata_that_cannot_be_read_means_none_rather_than_an_error(byte[] section)
  {
    Assert.Empty(QueryMetadata.Read(section));
  }

  [Fact]
  public void Damaged_metadata_does_not_stop_the_queries_being_read()
  {
    var workbook = MashupWorkbook.FromMashup(MashupWorkbook.Sections(
      0,
      MashupWorkbook.PackageParts("section Section1;\r\nshared A = 1;"),
      [],
      [0, 0, 0, 0, 3, 0, 0, 0, 0x3C, 0x61, 0x3E],
      []));

    var query = Assert.Single(WorkbookReader.Read(workbook));

    Assert.Equal(new WorkbookQuery("A", "1", null), query);
  }
}
