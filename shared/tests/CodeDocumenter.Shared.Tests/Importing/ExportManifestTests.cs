using CodeDocumenter.Shared.Importing;
using CodeDocumenter.Shared.Workbooks;

namespace CodeDocumenter.Shared.Tests.Importing;

/// <summary>Reading the file the page's Export tab writes.</summary>
public class ExportManifestTests
{
  [Fact]
  public void A_manifest_gives_the_picks_the_references_to_leave_out_and_where_it_came_from()
  {
    var manifest = ExportManifest.Parse(
      """{ "formatVersion": 1, "tool": "vbadoc", "workbook": "Sales.xlsm", "items": ["Logger", "Paths"], "leaveOutReferences": ["Scripting"] }""",
      "m.json", "vbadoc");

    Assert.Equal(["Logger", "Paths"], manifest.Items);
    Assert.Equal(["Scripting"], manifest.LeaveOutReferences);
    Assert.Equal("Sales.xlsm", manifest.Workbook);
  }

  [Fact]
  public void What_a_manifest_can_go_without_it_can_go_without()
  {
    var manifest = ExportManifest.Parse("""{ "formatVersion": 1, "tool": "pqdoc", "items": [] }""", "m.json", "pqdoc");

    Assert.Empty(manifest.Items);
    Assert.Empty(manifest.LeaveOutReferences);
    Assert.Null(manifest.Workbook);
  }

  [Theory]
  [InlineData("not JSON", "m.json isn't an export manifest.")]
  [InlineData("[1, 2]", "m.json isn't an export manifest.")]
  [InlineData("""{ "tool": "vbadoc", "items": [] }""", "m.json isn't an export manifest.")]
  [InlineData("""{ "formatVersion": 1, "tool": "pqdoc", "items": [] }""", "m.json holds pqdoc's choices, not vbadoc's.")]
  [InlineData("""{ "formatVersion": 1, "items": [] }""", "m.json holds another tool's choices, not vbadoc's.")]
  [InlineData("""{ "formatVersion": 2, "tool": "vbadoc", "items": [] }""", "m.json was written by a newer vbadoc. Update vbadoc to read it.")]
  public void A_file_that_isnt_a_manifest_for_this_tool_is_refused_saying_why(string text, string message)
  {
    var error = Assert.Throws<ExtractionException>(() => ExportManifest.Parse(text, "m.json", "vbadoc"));
    Assert.Equal(message, error.Message);
  }

  [Fact]
  public void A_file_that_cant_be_read_says_so()
  {
    var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
    Assert.Throws<ExtractionException>(() => ExportManifest.Read(missing, "vbadoc"));
  }
}
