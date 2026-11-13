using CodeDocumenter.Shared.Workbooks;
using OpenMcdf;

namespace CodeDocumenter.Shared.Tests.Workbooks;

/// <summary>What a tool can write into is its own list, so only a tool that writes into add-ins takes them.</summary>
public sealed class TargetFileTests : IDisposable
{
  private static readonly string[] WithAddins = [.. TargetFile.Workbooks, ".xlam"];

  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private string Save(string name, byte[] bytes)
  {
    var path = Path.Combine(temp.Path, name);
    File.WriteAllBytes(path, bytes);
    return path;
  }

  private string CompoundFile(string name)
  {
    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      using var stream = root.CreateStream("Workbook");
      stream.Write(new byte[64]);
    }

    return Save(name, buffer.ToArray());
  }

  [Fact]
  public void An_add_in_is_a_target_only_for_a_tool_whose_list_has_it()
  {
    var bytes = BlankWorkbook.Build(macroEnabled: true).Write();
    var path = Save("Tools.xlam", bytes);

    Assert.Equal(bytes, TargetFile.ReadBytes(path, "modules", ".xlsm", WithAddins));
    var error = Assert.Throws<ExtractionException>(() => TargetFile.ReadBytes(path, "queries", ".xlsx", TargetFile.Workbooks));
    Assert.Equal("Tools.xlam can't be a target: queries can only be written into .xlsx, .xlsm, .xlsb files.", error.Message);
  }

  [Fact]
  public void An_older_add_in_is_told_to_become_an_xlam_only_by_a_tool_that_writes_into_them()
  {
    var path = CompoundFile("Tools.xla");

    var vba = Assert.Throws<ExtractionException>(() => TargetFile.ReadBytes(path, "modules", ".xlsm", WithAddins));
    var queries = Assert.Throws<ExtractionException>(() => TargetFile.ReadBytes(path, "queries", ".xlsx", TargetFile.Workbooks));

    Assert.Equal("Tools.xla is an older .xla add-in, which modules can't be written into. Save it as an Excel Add-in (.xlam) and use that.", vba.Message);
    Assert.Equal("Tools.xla can't be a target: queries can only be written into .xlsx, .xlsm, .xlsb files.", queries.Message);
  }

  [Fact]
  public void An_older_workbook_is_told_what_to_be_saved_as()
  {
    var path = CompoundFile("Tools.xls");

    var error = Assert.Throws<ExtractionException>(() => TargetFile.ReadBytes(path, "modules", ".xlsm", WithAddins));

    Assert.Equal("Tools.xls is an older .xls workbook, which modules can't be written into. Save it as .xlsm and use that.", error.Message);
  }
}
