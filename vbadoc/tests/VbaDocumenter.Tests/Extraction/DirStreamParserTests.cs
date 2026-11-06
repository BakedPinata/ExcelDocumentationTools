using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Extraction;

public class DirStreamParserTests
{
  static byte[] Record(ushort id, byte[] data) => [.. BitConverter.GetBytes(id), .. BitConverter.GetBytes(data.Length), .. data];

  static byte[] Libid(string text) => [.. BitConverter.GetBytes(text.Length), .. System.Text.Encoding.ASCII.GetBytes(text), 0, 0, 0, 0, 0, 0];

  [Fact]
  public void Reads_library_references_with_what_the_editor_calls_them()
  {
    const string Scripting = @"*\G{420b2830-e718-11cf-893d-00a0c9054228}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime";
    byte[] dir =
    [
      .. Record(0x0003, [0xE4, 0x04]),
      .. Record(0x0016, "Scripting"u8.ToArray()),
      .. Record(0x003E, System.Text.Encoding.Unicode.GetBytes("Scripting")),
      .. Record(0x000D, Libid(Scripting)),
      .. Record(0x000F, [0, 0]),
      .. Record(0x0010, []),
    ];

    var reference = Assert.Single(DirStreamParser.Parse(dir).References);

    Assert.Equal("Scripting", reference.Name);
    Assert.Equal("Microsoft Scripting Runtime", reference.Description);
    Assert.Equal("{420B2830-E718-11CF-893D-00A0C9054228}", reference.Guid);
    Assert.Equal(VbaReferenceKind.Registered, reference.Kind);
    Assert.False(reference.IsStandard);
  }

  [Fact]
  public void The_references_every_project_has_are_known_as_such()
  {
    var project = WorkbookReader.Read(Fixtures.Workbook("SimpleMacro.xlsm"));

    Assert.Equal(["stdole", "Office"], project.References.Select(r => r.Name));
    Assert.All(project.References, r => Assert.True(r.IsStandard));
  }

  [Fact]
  public void A_control_reference_spread_over_several_records_is_one_reference()
  {
    var project = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm"));

    Assert.Equal(2, project.References.Count);
    Assert.Equal(VbaReferenceKind.Control, project.References[0].Kind);
    Assert.Equal(VbaReferenceKind.Registered, project.References[1].Kind);
  }

  [Fact]
  public void A_control_reference_is_known_by_the_library_its_cache_was_made_from()
  {
    var reference = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm")).References[0];

    Assert.Equal("MSForms", reference.Name);
    Assert.Equal("{0D452EE1-E08F-101A-852E-02608C4D0BB4}", reference.Guid);
    Assert.Contains("{93114339-5326-214C-A2D2-EAF4B5B60F6F}", reference.Libid); // the cache's, made on one machine
  }

  [Fact]
  public void A_reference_keeps_its_records_as_they_stand_so_it_can_be_copied()
  {
    var references = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm")).References;

    var control = DirRecords.Read(references[0].Records).Select(r => r.Id).ToList();
    Assert.Equal(0x0016, control[0]);
    Assert.Contains((ushort)0x0033, control);
    Assert.Contains((ushort)0x002F, control);
    Assert.Equal(0x0030, control[^1]);

    Assert.Equal([0x0016, 0x003E, 0x000D], DirRecords.Read(references[1].Records).Select(r => (int)r.Id));
  }

  [Fact]
  public void A_reference_to_another_VBA_project_points_at_a_file()
  {
    Assert.Equal(@"C:\Libs\Tools.xlam", VbaReference.From("Tools", @"*\CC:\Libs\Tools.xlam", VbaReferenceKind.Project).ProjectPath);
    Assert.Null(VbaReference.From("Scripting", @"*\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#scrrun.dll#Scripting", VbaReferenceKind.Registered).ProjectPath);
  }

  [Fact]
  public void Reads_project_name_code_page_and_modules()
  {
    var dir = SyntheticWorkbook.DirStream(
    [
        new SyntheticModule("Main", VbaModuleKind.Standard, ""),
            new SyntheticModule("Order", VbaModuleKind.Class, ""),
        ], codePage: 1252);

    var info = DirStreamParser.Parse(dir);

    Assert.Equal("VBAProject", info.ProjectName);
    Assert.Equal(1252, info.CodePage);
    Assert.Collection(info.Modules,
        m => Assert.Equal(("Main", "Main", true), (m.Name, m.StreamName, m.IsProcedural)),
        m => Assert.Equal(("Order", "Order", false), (m.Name, m.StreamName, m.IsProcedural)));
  }

  [Fact]
  public void Uses_the_unicode_module_name_when_present()
  {
    // MODULENAME is in the code page (Cyrillic 1251 here); MODULENAMEUNICODE is UTF-16 and wins.
    var dir = SyntheticWorkbook.DirStream([new SyntheticModule("Отчёт", VbaModuleKind.Standard, "")], codePage: 1251);

    Assert.Equal("Отчёт", DirStreamParser.Parse(dir).Modules.Single().Name);
  }

  [Fact]
  public void Rejects_a_record_that_runs_past_the_end()
  {
    // PROJECTNAME claiming 100 bytes of data when only 3 follow.
    byte[] dir = [0x04, 0x00, 100, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c'];

    Assert.Throws<InvalidDataException>(() => DirStreamParser.Parse(dir));
  }

  [Fact]
  public void Rejects_a_stream_without_terminator()
  {
    var dir = SyntheticWorkbook.DirStream([], codePage: 1252);

    Assert.Throws<InvalidDataException>(() => DirStreamParser.Parse(dir[..^6]));
  }
}
