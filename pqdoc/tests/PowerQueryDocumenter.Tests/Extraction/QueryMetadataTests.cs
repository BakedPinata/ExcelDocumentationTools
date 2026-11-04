using System.Text;
using PowerQueryDocumenter.Core.Extraction;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Extraction;

/// <summary>Query groups and result types from a workbook's metadata (FR-HTML-11).</summary>
public sealed class QueryMetadataTests
{
  private static readonly Guid Staging = Guid.Parse("c5506b5e-2b6a-43a6-840d-f8043dd958e9");
  private static readonly Guid Reference = Guid.Parse("9227dae9-f6c3-4d78-a93c-e3938794b77c");
  private static readonly Guid Root = Guid.Parse("1f9b7c57-e710-48f9-89fe-db0ef9c11a4e");
  private static readonly Guid Middle = Guid.Parse("667870fa-c3bf-4b38-a55e-55c28b333149");
  private static readonly Guid Deep = Guid.Parse("41b82030-d40d-4b7a-a39f-009857f14f56");

  /// <summary>One group in a list: its parent, when it has one, and its description, when it isn't empty.</summary>
  private sealed record Stored(Guid Id, string Name, int Order, Guid? Parent = null, string Description = "");

  /// <summary>A metadata section: version, the XML after its length, then an empty content part.</summary>
  private static byte[] Section(string xml)
  {
    var bytes = Encoding.UTF8.GetBytes(xml);
    return [.. BitConverter.GetBytes(0u), .. BitConverter.GetBytes((uint)bytes.Length), .. bytes, .. BitConverter.GetBytes(0u)];
  }

  /// <summary>A QueryGroups list laid out as Excel writes it, with optional bytes left over.</summary>
  private static string GroupList(byte[]? extra, params (Guid Id, string Name, int Order)[] groups) =>
    GroupList(extra, groups.Select(g => new Stored(g.Id, g.Name, g.Order)).ToArray());

  /// <summary>A QueryGroups list laid out as Excel writes it, with nested groups, and optional bytes left over.</summary>
  private static string GroupList(byte[]? extra, params Stored[] groups)
  {
    using var memory = new MemoryStream();
    using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
    {
      writer.Write(groups.Length);
      foreach (var group in groups)
      {
        writer.Write(0);
        writer.Write(group.Id.ToByteArray());
        writer.Write(group.Name);
        writer.Write(group.Description);
        writer.Write(group.Parent is null ? (byte)0 : (byte)1);
        if (group.Parent is { } parent)
        {
          writer.Write(parent.ToByteArray());
        }

        writer.Write(group.Order);
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
    var log = new TestLog();
    var queries = WorkbookReader.Read(Fixtures.Workbook(workbook), log.CreateLogger()).ToDictionary(q => q.Name);

    var root = new QueryGroup("Root", 3);
    var middle = new QueryGroup("Has Description", 0, "This is a nested group with a description", root);
    Assert.Equal(new QueryGroup("Staging", 0), queries["Chain_A"].Group);
    Assert.Equal(new QueryGroup("Staging", 0), queries["Chain_D"].Group);
    Assert.Equal(new QueryGroup("Reference", 1), queries["Discount%"].Group);
    Assert.Equal(new QueryGroup("Reference", 1), queries["Manual Targets"].Group);
    Assert.Equal(new QueryGroup("Deep", 0, null, middle), queries["Sales Data"].Group);
    Assert.Equal(middle, queries["Sales/Returns"].Group);
    Assert.Equal(["Root", "Has Description", "Deep"], queries["Sales Data"].Group!.Path().Select(g => g.Name));
    Assert.Null(queries["Chain_E"].Group);
    Assert.Empty(log.Messages(LogEventLevel.Warning));
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
  public void Nested_groups_are_read_to_any_depth_even_when_a_group_comes_before_its_parent()
  {
    var xml = Xml(
      GroupList(
        null,
        new Stored(Deep, "Deep", 0, Middle),
        new Stored(Middle, "Middle", 1, Root, "Holds Deep"),
        new Stored(Root, "Root", 0)),
      ("Section1/A", Entry("QueryGroupID", $"s{Deep}")),
      ("Section1/B", Entry("QueryGroupID", $"s{Middle}")));

    var metadata = QueryMetadata.Read(Section(xml));

    var middle = new QueryGroup("Middle", 1, "Holds Deep", new QueryGroup("Root", 0));
    Assert.Equal(new QueryGroup("Deep", 0, null, middle), metadata["A"].Group);
    Assert.Equal(middle, metadata["B"].Group);
  }

  [Fact]
  public void A_group_whose_parent_is_missing_or_makes_a_loop_is_kept_at_the_top_level()
  {
    var xml = Xml(
      GroupList(
        null,
        new Stored(Staging, "Orphan", 0, Guid.Parse("00000000-0000-0000-0000-000000000001")),
        new Stored(Root, "Loop A", 1, Middle),
        new Stored(Middle, "Loop B", 2, Root)),
      ("Section1/A", Entry("QueryGroupID", $"s{Staging}")),
      ("Section1/B", Entry("QueryGroupID", $"s{Root}")),
      ("Section1/C", Entry("QueryGroupID", $"s{Middle}")));

    var metadata = QueryMetadata.Read(Section(xml));

    Assert.Equal(new QueryGroup("Orphan", 0), metadata["A"].Group);
    var paths = new[] { metadata["B"].Group!.Path(), metadata["C"].Group!.Path() };
    Assert.All(paths, path => Assert.Equal(path.Count, path.Distinct().Count()));
    Assert.Contains(paths, path => path.Count == 1);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void A_group_list_in_an_unknown_layout_means_no_groups_but_keeps_the_result_types_and_warns(bool bytesLeftOver)
  {
    // Either bytes left over, or a parent flag that's neither 0 nor 1.
    var list = bytesLeftOver ? GroupList([0xFF], (Staging, "Staging", 0)) : BadFlag();
    var xml = Xml(list, ("Section1/Chain_A", Entry("ResultType", "sTable") + Entry("QueryGroupID", $"s{Staging}")));
    var log = new TestLog();

    var entry = Assert.Single(QueryMetadata.Read(Section(xml), log.CreateLogger()));

    Assert.Equal(new QueryMetadataEntry(null, "Table"), entry.Value);
    Assert.Contains("Could not read the workbook's query groups", Assert.Single(log.Messages(LogEventLevel.Warning)), StringComparison.Ordinal);
  }

  /// <summary>A list of one group whose parent flag is 2.</summary>
  private static string BadFlag()
  {
    var bytes = Convert.FromBase64String(GroupList(null, (Staging, "Staging", 0)));
    bytes[^5] = 2;
    return Convert.ToBase64String(bytes);
  }

  [Fact]
  public void A_workbook_without_groups_gives_no_warning()
  {
    var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><LocalPackageMetadataFile><Items>"
      + $"<Item><ItemLocation><ItemType>Formula</ItemType><ItemPath>Section1/A</ItemPath></ItemLocation><StableEntries>{Entry("ResultType", "sTable")}</StableEntries></Item>"
      + "</Items></LocalPackageMetadataFile>";
    var log = new TestLog();

    var entry = Assert.Single(QueryMetadata.Read(Section(xml), log.CreateLogger()));

    Assert.Equal(new QueryMetadataEntry(null, "Table"), entry.Value);
    Assert.Empty(log.Messages(LogEventLevel.Warning));
  }

  [Fact]
  public void A_query_in_a_group_the_list_does_not_have_is_in_no_group()
  {
    var xml = Xml(
      GroupList(null, (Staging, "Staging", 0)),
      ("Section1/Chain_A", Entry("QueryGroupID", $"s{Reference}")));

    Assert.Null(Assert.Single(QueryMetadata.Read(Section(xml))).Value.Group);
  }

  [Fact]
  public void An_empty_metadata_section_means_none_without_a_warning()
  {
    var log = new TestLog();

    Assert.Empty(QueryMetadata.Read([], log.CreateLogger()));
    Assert.Empty(log.Messages(LogEventLevel.Warning));
  }

  [Theory]
  [InlineData(new byte[] { 0, 0, 0 })]
  [InlineData(new byte[] { 0, 0, 0, 0, 255, 255, 255, 127 })]
  [InlineData(new byte[] { 0, 0, 0, 0, 3, 0, 0, 0, 0x3C, 0x61, 0x3E })]
  public void Metadata_that_cannot_be_read_means_none_rather_than_an_error_with_a_warning(byte[] section)
  {
    var log = new TestLog();

    Assert.Empty(QueryMetadata.Read(section, log.CreateLogger()));
    Assert.Single(log.Messages(LogEventLevel.Warning));
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
