using System.Text;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

/// <summary>What's written into a vbaProject.bin, checked against what Excel writes in the real fixtures.</summary>
public sealed class ProjectWriterTests
{
  static readonly byte[] Source = Encoding.ASCII.GetBytes("Attribute VB_Name = \"Added\"\r\nOption Explicit\r\nPublic Sub Hello()\r\nEnd Sub\r\n");

  static byte[] RealProject() => OpcPackage.Read(File.ReadAllBytes(Fixtures.Workbook("SimpleMacro.xlsm"))).Get("xl/vbaProject.bin");

  static ModuleToWrite Module(string name, VbaModuleKind kind = VbaModuleKind.Standard) =>
    new(name, kind, Encoding.ASCII.GetBytes($"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n"));

  static byte[] Add(byte[] bin, params ModuleToWrite[] modules) => ProjectWriter.Edit(bin, new ProjectChange(modules, [], []));

  // Reads a bare vbaProject.bin as a workbook, through vbadoc's own reader.
  static VbaProject Read(byte[] bin)
  {
    var package = new OpcPackage();
    package.Set("xl/vbaProject.bin", bin);
    return WorkbookReader.Read(package.Write());
  }

  static byte[] Stream(byte[] bin, params string[] path)
  {
    using var root = RootStorage.Open(new MemoryStream(bin));
    Storage storage = root;
    foreach (var name in path[..^1]) storage = storage.OpenStorage(name);
    using var stream = storage.OpenStream(path[^1]);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }

  static List<string> Entries(byte[] bin)
  {
    using var root = RootStorage.Open(new MemoryStream(bin));
    return root.OpenStorage("VBA").EnumerateEntries().Select(e => e.Name).ToList();
  }

  static string ProjectText(byte[] bin) => Encoding.Latin1.GetString(Stream(bin, "PROJECT"));

  static List<DirRecord> Dir(byte[] bin) => DirRecords.Read(OvbaDecompressor.Decompress(Stream(bin, "VBA", "dir")));

  // One module's record ids, as a list, from its name to its terminator.
  static List<ushort> RecordsOf(byte[] bin, string module)
  {
    var dir = Dir(bin);
    int start = dir.FindIndex(r => r.Id == 0x0019 && Encoding.ASCII.GetString(r.Data) == module);
    int end = dir.FindIndex(start, r => r.Id == 0x002B);
    return dir.GetRange(start, end - start + 1).Select(r => r.Id).ToList();
  }

  [Fact]
  public void A_module_added_to_a_real_project_reads_back_with_everything_else_as_it_was()
  {
    var before = Read(RealProject());

    var after = Read(Add(RealProject(), new ModuleToWrite("Added", VbaModuleKind.Standard, Source)));

    Assert.Equal(before.Modules.Count + 1, after.Modules.Count);
    Assert.Equal(Source, after.Modules.Single(m => m.Name == "Added").SourceBytes);
    Assert.Equal(VbaModuleKind.Standard, after.Modules.Single(m => m.Name == "Added").Kind);
    foreach (var module in before.Modules)
    {
      Assert.Equal(module.SourceBytes, after.Modules.Single(m => m.Name == module.Name).SourceBytes);
      Assert.Equal(module.Kind, after.Modules.Single(m => m.Name == module.Name).Kind);
    }

    Assert.Equal(before.References.Select(r => r.Name), after.References.Select(r => r.Name));
  }

  [Fact]
  public void Compiled_code_goes_so_that_Excel_compiles_from_source()
  {
    var bin = Add(RealProject(), Module("Added"));

    Assert.Equal([0xCC, 0x61, 0xFF, 0xFF, 0x00, 0x00, 0x00], Stream(bin, "VBA", "_VBA_PROJECT"));
    Assert.DoesNotContain(Entries(bin), e => e.StartsWith("__SRP_", StringComparison.Ordinal));
  }

  [Fact]
  public void A_new_modules_records_are_those_Excel_writes_for_one_in_the_same_order()
  {
    var bin = Add(RealProject(), Module("Added"));

    Assert.Equal(RecordsOf(RealProject(), "Module1"), RecordsOf(bin, "Added"));
  }

  [Fact]
  public void The_project_counts_its_modules()
  {
    var bin = Add(RealProject(), Module("One"), Module("Two", VbaModuleKind.Class));

    var count = BitConverter.ToUInt16(Dir(bin).First(r => r.Id == 0x000F).Data);

    Assert.Equal(Read(RealProject()).Modules.Count + 2, count);
  }

  [Fact]
  public void Module_lines_go_before_the_projects_own_properties()
  {
    var lines = ProjectText(Add(RealProject(), Module("One"), Module("Two", VbaModuleKind.Class))).Split("\r\n").ToList();

    int name = lines.FindIndex(l => l.StartsWith("Name=", StringComparison.Ordinal));
    Assert.InRange(lines.IndexOf("Module=One"), 1, name - 1);
    Assert.InRange(lines.IndexOf("Class=Two"), 1, name - 1);
    Assert.StartsWith("ID=", lines[0]);
  }

  [Fact]
  public void The_name_map_lists_every_module_in_both_encodings()
  {
    var map = Stream(Add(RealProject(), Module("Added")), "PROJECTwm");

    Assert.Contains("Added\0", Encoding.ASCII.GetString(map));
    Assert.Contains("Added", Encoding.Unicode.GetString(map[^14..]) + Encoding.Unicode.GetString(map[^15..]));
  }

  [Fact]
  public void A_project_without_a_name_map_doesnt_gain_a_partial_one()
  {
    var bin = OpcPackage.Read(SyntheticWorkbook.Package([new("Main", VbaModuleKind.Standard, "Attribute VB_Name = \"Main\"\r\n")])).Get("xl/vbaProject.bin");

    using var root = RootStorage.Open(new MemoryStream(Add(bin, Module("Added"))));

    Assert.False(root.ContainsEntry("PROJECTwm"));
  }

  [Fact]
  public void Replacing_takes_out_the_old_stream_and_lines_whatever_the_names_case()
  {
    var replaced = new ModuleToWrite("MODULE1", VbaModuleKind.Standard, Source);

    var bin = ProjectWriter.Edit(RealProject(), new ProjectChange([replaced], ["module1"], []));

    Assert.Equal(Source, Read(bin).Modules.Single(m => m.Name == "MODULE1").SourceBytes);
    Assert.DoesNotContain(Read(bin).Modules, m => m.Name == "Module1");
    Assert.Single(ProjectText(bin).Split("\r\n"), l => l.Equals("Module=MODULE1", StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain("Module1=", ProjectText(bin));
  }

  [Fact]
  public void A_module_can_change_kind_when_its_replaced()
  {
    var bin = ProjectWriter.Edit(RealProject(), new ProjectChange([new("Module1", VbaModuleKind.Class, Source)], ["Module1"], []));

    Assert.Equal(VbaModuleKind.Class, Read(bin).Modules.Single(m => m.Name == "Module1").Kind);
    Assert.Contains("Class=Module1", ProjectText(bin));
  }

  [Theory]
  [InlineData("Name")]
  [InlineData("ID")]
  [InlineData("HelpContextID")]
  [InlineData("Module")]
  public void Replacing_a_module_named_like_a_project_property_leaves_the_property_alone(string name)
  {
    var bin = OpcPackage.Read(SyntheticWorkbook.Package([new(name, VbaModuleKind.Standard, $"Attribute VB_Name = \"{name}\"\r\n")])).Get("xl/vbaProject.bin");
    var before = ProjectText(bin);

    var after = ProjectText(ProjectWriter.Edit(bin, new ProjectChange([Module(name)], [name], [])));

    Assert.Contains("Name=\"VBAProject\"", after);
    Assert.StartsWith("ID=", after);
    Assert.Contains($"Module={name}\r\n", after);
    Assert.DoesNotContain($"\r\n{name}=0, 0, 0, 0, C", after);
    Assert.Contains($"\r\n{name}=0, 0, 0, 0, C", before);
  }

  [Theory]
  [InlineData("Dir")]
  [InlineData("DIR")]
  [InlineData("_vba_project")]
  public void A_module_named_like_the_projects_own_streams_gets_a_stream_of_its_own(string name)
  {
    var bin = Add(RealProject(), Module(name));

    var read = Read(bin);
    Assert.Equal($"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n", read.Modules.Single(m => m.Name == name).Source);
    Assert.NotEqual(name, read.Modules.Single(m => m.Name == name).StreamName);
    Assert.Equal(7, Stream(bin, "VBA", "_VBA_PROJECT").Length);
  }

  [Fact]
  public void A_module_named_like_the_projects_own_streams_works_in_a_new_project_too()
  {
    var bin = ProjectWriter.Create([Module("Dir")], [], 1252);

    Assert.Equal(Module("Dir").Source, Read(bin).Modules.Single().SourceBytes);
  }

  [Fact]
  public void Adding_a_module_the_project_has_is_refused_rather_than_doubled()
  {
    Assert.Throws<InvalidDataException>(() => Add(RealProject(), Module("module1")));
  }

  [Fact]
  public void Replacing_a_module_it_hasnt_got_is_refused()
  {
    Assert.Throws<InvalidDataException>(() => ProjectWriter.Edit(RealProject(), new ProjectChange([], ["Nope"], [])));
  }

  [Fact]
  public void A_real_control_reference_comes_across_as_it_stood()
  {
    var source = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm")).References[0];

    var read = Read(ProjectWriter.Edit(RealProject(), new ProjectChange([], [], [source]))).References;

    var copied = read.Single(r => r.Name == "MSForms");
    Assert.Equal(VbaReferenceKind.Control, copied.Kind);
    Assert.Equal(source.Guid, copied.Guid);
    Assert.Equal(source.Records, copied.Records);
    Assert.Equal(["stdole", "Office", "MSForms"], read.Select(r => r.Name));
  }

  [Fact]
  public void A_reference_to_another_VBA_project_comes_across_as_it_stood()
  {
    var libid = Encoding.ASCII.GetBytes(@"*\CC:\Libs\Tools.xlam");
    List<DirRecord> records =
    [
      new(0x0003, [0xE4, 0x04]), // the code page, which isn't part of the reference
      new(0x0016, Encoding.ASCII.GetBytes("Tools")),
      new(0x003E, Encoding.Unicode.GetBytes("Tools")),
      new(0x000E, [.. BitConverter.GetBytes(libid.Length), .. libid, .. BitConverter.GetBytes(libid.Length), .. libid, 1, 0, 0, 0, 0, 0]),
      new(0x0010, []),
    ];
    var source = DirStreamParser.Parse(DirRecords.Write(records)).References.Single();

    var copied = Read(ProjectWriter.Edit(RealProject(), new ProjectChange([], [], [source]))).References.Single(r => r.Name == "Tools");

    Assert.Equal(VbaReferenceKind.Project, copied.Kind);
    Assert.Equal(@"C:\Libs\Tools.xlam", copied.ProjectPath);
    Assert.Equal(source.Records, copied.Records);
  }

  [Fact]
  public void A_new_project_has_Excels_two_references_and_any_others_after_them()
  {
    var scripting = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm")).References[0];

    var read = Read(ProjectWriter.Create([Module("Main")], [scripting], 1252));

    Assert.Equal(["stdole", "Office", "MSForms"], read.References.Select(r => r.Name));
    Assert.True(read.References[0].IsStandard);
    Assert.True(read.References[1].IsStandard);
  }

  [Fact]
  public void A_new_project_is_unprotected_and_visible_under_a_key_from_its_own_id()
  {
    var lines = ProjectText(ProjectWriter.Create([Module("Main")], [], 1252)).Split("\r\n");
    string Value(string key) => lines.First(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Split('"')[1];

    var key = ProjectEncryption.ProjectKey(Value("ID"));
    void Holds(string name, byte[] expected)
    {
      var (readKey, data) = ProjectEncryption.Decrypt(Value(name));
      Assert.Equal(key, readKey);
      Assert.Equal(expected, data);
    }

    Holds("CMG", [0, 0, 0, 0]);
    Holds("DPB", [0]);
    Holds("GC", [0xFF]);
  }

  [Fact]
  public void A_new_project_has_the_same_shape_of_dir_stream_as_Excels()
  {
    static List<ushort> Header(byte[] bin) => Dir(bin).TakeWhile(r => r.Id != 0x0016).Select(r => r.Id).ToList();

    Assert.Equal(Header(RealProject()), Header(ProjectWriter.Create([Module("Main")], [], 1252)));
  }

  [Theory]
  [InlineData(1252)]
  [InlineData(950)]
  [InlineData(10000)]
  public void A_new_project_takes_the_code_page_its_source_is_in(int codePage)
  {
    var read = Read(ProjectWriter.Create([Module("Main")], [], codePage));

    Assert.Equal(codePage, read.CodePage);
  }

  [Theory]
  [InlineData(VbaModuleKind.Standard, "Module=Main")]
  [InlineData(VbaModuleKind.Class, "Class=Main")]
  [InlineData(VbaModuleKind.Document, "Document=Main/&H00000000")]
  public void A_modules_line_says_what_it_is(VbaModuleKind kind, string line)
  {
    Assert.Contains(line + "\r\n", ProjectText(ProjectWriter.Create([Module("Main", kind)], [], 1252)));
  }
}
