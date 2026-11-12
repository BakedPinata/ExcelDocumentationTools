using System.Text;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

/// <summary>
/// Add-ins, with the samples Excel made in step J1 (see the fixtures' README): each reads as its workbook does, and is
/// written into as one is, staying an add-in.
/// </summary>
public sealed class AddinTests : IDisposable
{
  readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-addin-{Guid.NewGuid():N}");

  public AddinTests() => Directory.CreateDirectory(_folder);

  public void Dispose() => Directory.Delete(_folder, recursive: true);

  string Save(string name, byte[] bytes)
  {
    var path = Path.Combine(_folder, name);
    File.WriteAllBytes(path, bytes);
    return path;
  }

  static VbaModule Incoming(string name, string? body = null)
  {
    var source = $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{body}";
    return new VbaModule { Name = name, Kind = VbaModuleKind.Standard, StreamName = name, Source = source, SourceBytes = Encoding.ASCII.GetBytes(source) };
  }

  static ModuleChange Adding(params VbaModule[] modules) => new(modules, [], [], 1252);

  // The PROJECT stream's lines that say who the project is and how it's protected.
  static string[] Protection(byte[] workbook)
  {
    using var root = RootStorage.Open(new MemoryStream(OpcPackage.Read(workbook).Get("xl/vbaProject.bin")));
    using var stream = root.OpenStream("PROJECT");
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    string[] keys = ["ID=", "CMG=", "DPB=", "GC="];
    return [.. Encoding.Latin1.GetString(buffer.ToArray()).Split("\r\n").Where(line => keys.Any(key => line.StartsWith(key, StringComparison.Ordinal)))];
  }

  [Theory]
  [InlineData("Toolkit.xlam")]
  [InlineData("LockedToolkit.xlam")]
  [InlineData("Toolkit.xla")]
  public void Each_sample_reads_as_its_workbook_does(string name)
  {
    var workbook = WorkbookReader.Read(Fixtures.Workbook("Toolkit.xlsm"));

    var addin = WorkbookReader.Read(Fixtures.Workbook(name));

    Assert.Equal(workbook.Modules.Select(m => (m.Name, m.Kind, m.Source)), addin.Modules.Select(m => (m.Name, m.Kind, m.Source)));
    Assert.Equal(workbook.References.Select(r => r.Libid), addin.References.Select(r => r.Libid));
    Assert.Contains("Scripting", addin.References.Select(r => r.Name));
  }

  [Fact]
  public void An_add_in_with_no_VBA_says_so_as_an_add_in()
  {
    var error = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(Fixtures.Workbook("Empty.xlam")));

    Assert.Equal("The add-in has no VBA project (no xl/vbaProject.bin), so it has no modules.", error.Message);
  }

  [Fact]
  public void A_workbook_with_no_VBA_still_asks_whether_it_was_saved_as_an_xlsx()
  {
    var error = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(BlankWorkbook.Build(macroEnabled: false).Write()));

    Assert.EndsWith("Was it saved as .xlsx?", error.Message);
  }

  [Fact]
  public void Only_its_content_type_makes_an_add_in()
  {
    Assert.True(TargetWorkbook.Read(Fixtures.Workbook("Toolkit.xlam")).IsAddin);
    Assert.True(TargetWorkbook.Read(Fixtures.Workbook("Empty.xlam")).IsAddin);
    Assert.False(TargetWorkbook.Read(Fixtures.Workbook("Toolkit.xlsm")).IsAddin);
  }

  [Fact]
  public void An_add_in_gains_and_replaces_modules_and_only_its_project_changes()
  {
    var original = File.ReadAllBytes(Fixtures.Workbook("Toolkit.xlam"));
    var target = TargetWorkbook.Read(Fixtures.Workbook("Toolkit.xlam"));
    var kept = target.Project!.Modules.Where(m => m.Name != "StandardModule").ToDictionary(m => m.Name, m => m.SourceBytes);
    var replacement = Incoming("StandardModule", "Public Sub Demo()\r\nEnd Sub\r\n");

    var bytes = target.Write(new ModuleChange([replacement, Incoming("Added")], ["StandardModule"], [], 1252));

    var before = OpcPackage.Read(original);
    var after = OpcPackage.Read(bytes);
    Assert.Equal(before.Names.Order(), after.Names.Order());
    Assert.Equal(["xl/vbaProject.bin"], before.Names.Where(n => !before.Get(n).AsSpan().SequenceEqual(after.Get(n))));
    Assert.True(after.IsAddin);

    var read = WorkbookReader.Read(bytes).Modules.ToDictionary(m => m.Name, m => m.SourceBytes);
    Assert.Equal(replacement.SourceBytes, read["StandardModule"]);
    Assert.Contains("Added", read.Keys);
    Assert.All(kept, module => Assert.Equal(module.Value, read[module.Key]));
  }

  [Fact]
  public void A_locked_add_in_stays_locked_with_the_same_password()
  {
    var original = File.ReadAllBytes(Fixtures.Workbook("LockedToolkit.xlam"));
    var target = TargetWorkbook.Read(Fixtures.Workbook("LockedToolkit.xlam"));
    Assert.True(target.IsLocked);

    var bytes = target.Write(Adding(Incoming("Added")));

    var again = TargetWorkbook.Read(Save("After.xlam", bytes));
    Assert.True(again.IsLocked);
    Assert.True(again.IsAddin);
    Assert.Equal(Protection(original), Protection(bytes));
    Assert.Contains("Added", again.Project!.Modules.Select(m => m.Name));
  }

  [Fact]
  public void An_add_in_with_no_VBA_gains_a_project_and_stays_an_add_in()
  {
    var target = TargetWorkbook.Read(Fixtures.Workbook("Empty.xlam"));
    Assert.Null(target.Project);

    var bytes = target.Write(Adding(Incoming("Main")));

    Assert.True(OpcPackage.Read(bytes).IsAddin);
    var modules = WorkbookReader.Read(bytes).Modules;
    Assert.Contains(modules, m => m.Name == "Main" && m.Kind == VbaModuleKind.Standard);
    Assert.Contains(modules, m => m.Kind == VbaModuleKind.Document && m.Source.Contains("00020819")); // the workbook's own
  }

  [Fact]
  public void A_new_add_in_is_a_new_workbook_with_the_add_ins_content_type()
  {
    var addin = OpcPackage.Read(TargetWorkbook.New(addin: true).Write(Adding(Incoming("Main"))));
    var workbook = OpcPackage.Read(TargetWorkbook.New().Write(Adding(Incoming("Main"))));

    Assert.True(addin.IsAddin);
    Assert.False(workbook.IsAddin);
    Assert.Equal(BlankWorkbook.MacroEnabledContentType, workbook.ContentTypeOf(workbook.WorkbookPart));
    Assert.Equal(workbook.Names.Order(), addin.Names.Order());

    // A new project gets an ID of its own, so its part differs; the content types are the point.
    string[] differ = [OpcPackage.ContentTypesPart, "xl/vbaProject.bin"];
    Assert.All(workbook.Names.Except(differ), name => Assert.Equal(workbook.Get(name), addin.Get(name)));
  }

  [Fact]
  public void An_older_add_in_cant_be_a_target_and_it_says_to_save_it_as_an_xlam()
  {
    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.Read(Fixtures.Workbook("Toolkit.xla")));

    Assert.Equal("Toolkit.xla is an older .xla add-in, which modules can't be written into. Save it as an Excel Add-in (.xlam) and use that.", error.Message);
  }
}
