using System.Text;
using ExcelDocumenter.Shared.Importing;
using ExcelDocumenter.Shared.Workbooks;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

/// <summary>What a target can be, what comes through untouched, and how a choice compares with what's there.</summary>
public sealed class TargetWorkbookTests : IDisposable
{
  const string LabelInfo = "docMetadata/LabelInfo.xml";
  const string Custom = "docProps/custom.xml";

  static readonly byte[] Label = Encoding.UTF8.GetBytes("<clbl:labelList xmlns:clbl=\"x\"><clbl:label id=\"{1}\" enabled=\"1\"/></clbl:labelList>");
  static readonly byte[] Properties = Encoding.UTF8.GetBytes("<Properties><property name=\"MSIP_Label_1_Enabled\"/></Properties>");

  readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-target-{Guid.NewGuid():N}");

  public TargetWorkbookTests() => Directory.CreateDirectory(_folder);

  public void Dispose() => Directory.Delete(_folder, recursive: true);

  string Save(string name, byte[] bytes)
  {
    var path = Path.Combine(_folder, name);
    File.WriteAllBytes(path, bytes);
    return path;
  }

  static VbaModule Incoming(string name, VbaModuleKind kind = VbaModuleKind.Standard, string? body = null)
  {
    var source = $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{body}";
    return new VbaModule { Name = name, Kind = kind, StreamName = name, Source = source, SourceBytes = Encoding.ASCII.GetBytes(source) };
  }

  static ModuleChange Adding(params VbaModule[] modules) => new(modules, [], [], 1252);

  static SyntheticModule Existing(string name, VbaModuleKind kind = VbaModuleKind.Standard, string? body = null) =>
    new(name, kind, $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{body}");

  static byte[] Labelled(OpcPackage package)
  {
    package.Set(LabelInfo, Label);
    package.SetOverride(LabelInfo, "application/vnd.ms-office.classificationlabels+xml");
    package.AddRelationship("", "http://schemas.microsoft.com/office/2020/02/relationships/classificationlabels", LabelInfo);
    package.Set(Custom, Properties);
    package.SetOverride(Custom, "application/vnd.openxmlformats-officedocument.custom-properties+xml");
    return package.Write();
  }

  [Fact]
  public void A_new_workbook_has_the_modules_and_a_document_module_for_itself_and_its_sheet()
  {
    var read = WorkbookReader.Read(TargetWorkbook.New().Write(Adding(Incoming("Main"))));

    Assert.Equal(["Main", "Sheet1", "ThisWorkbook"], read.Modules.Select(m => m.Name));
    Assert.Equal(VbaModuleKind.Standard, read.Modules[0].Kind);
    Assert.Equal(VbaModuleKind.Document, read.Modules[1].Kind);
    Assert.Equal(VbaModuleKind.Document, read.Modules[2].Kind);
    Assert.Contains("00020819", read.Modules[2].Source); // the workbook's base
    Assert.Contains("00020820", read.Modules[1].Source); // a sheet's base
  }

  [Fact]
  public void A_new_workbook_is_tied_to_its_document_modules_by_code_names()
  {
    var package = OpcPackage.Read(TargetWorkbook.New().Write(Adding(Incoming("Main"))));

    Assert.Equal("ThisWorkbook", (string?)package.Xml("xl/workbook.xml").Root!.Element(OpcPackage.Main + "workbookPr")!.Attribute("codeName"));
    Assert.Equal("Sheet1", (string?)package.Xml("xl/worksheets/sheet1.xml").Root!.Element(OpcPackage.Main + "sheetPr")!.Attribute("codeName"));
  }

  [Fact]
  public void A_new_workbook_is_macro_enabled_with_a_typed_and_related_project()
  {
    var package = OpcPackage.Read(TargetWorkbook.New().Write(Adding(Incoming("Main"))));
    var types = Encoding.UTF8.GetString(package.Get(OpcPackage.ContentTypesPart));

    Assert.Contains(BlankWorkbook.MacroEnabledContentType, types);
    Assert.Contains("application/vnd.ms-office.vbaProject", types);
    Assert.Contains(package.RelationshipsOf("xl/workbook.xml"), r => r.Target == "vbaProject.bin");
  }

  [Fact]
  public void A_code_name_given_to_a_sheet_keeps_clear_of_the_modules_coming_in()
  {
    var read = WorkbookReader.Read(TargetWorkbook.New().Write(Adding(Incoming("Sheet1"), Incoming("ThisWorkbook"))));

    var documents = read.Modules.Where(m => m.Kind == VbaModuleKind.Document).Select(m => m.Name);
    Assert.Equal(["Sheet2", "ThisWorkbook1"], documents);
    Assert.Equal(4, read.Modules.Count);
  }

  [Fact]
  public void An_xlsx_gains_a_project_and_keeps_the_code_names_it_had()
  {
    var package = BlankWorkbook.Build(macroEnabled: false);
    var sheet = package.Xml("xl/worksheets/sheet1.xml");
    sheet.Root!.AddFirst(new System.Xml.Linq.XElement(OpcPackage.Main + "sheetPr", new System.Xml.Linq.XAttribute("codeName", "Summary")));
    package.SetXml("xl/worksheets/sheet1.xml", sheet);

    var read = WorkbookReader.Read(TargetWorkbook.Read(Save("Target.xlsx", package.Write())).Write(Adding(Incoming("Main"))));

    Assert.Equal(["Main", "Summary", "ThisWorkbook"], read.Modules.Select(m => m.Name));
  }

  [Fact]
  public void A_macro_enabled_workbook_with_no_project_gets_one()
  {
    var target = TargetWorkbook.Read(Fixtures.Workbook("xlmmacro.xlsm"));

    Assert.Null(target.Project);
    Assert.Contains(WorkbookReader.Read(target.Write(Adding(Incoming("Main")))).Modules, m => m.Name == "Main");
  }

  [Fact]
  public void A_real_workbooks_project_keeps_its_modules()
  {
    var target = TargetWorkbook.Read(Fixtures.Workbook("SimpleMacro.xlsm"));
    var before = target.Project!.Modules.ToDictionary(m => m.Name, m => m.SourceBytes);

    var after = WorkbookReader.Read(target.Write(Adding(Incoming("Main")))).Modules;

    Assert.Equal(before.Count + 1, after.Count);
    Assert.All(after.Where(m => m.Name != "Main"), m => Assert.Equal(before[m.Name], m.SourceBytes));
  }

  [Fact]
  public void An_xlsb_needs_no_code_names_and_its_workbook_part_is_left_alone()
  {
    var bytes = TargetWorkbook.Read(Save("Target.xlsb", SyntheticWorkbook.Binary())).Write(Adding(Incoming("Main")));

    var package = OpcPackage.Read(bytes);
    Assert.Equal([1, 2, 3], package.Get("xl/workbook.bin"));
    Assert.DoesNotContain(BlankWorkbook.MacroEnabledContentType, Encoding.UTF8.GetString(package.Get(OpcPackage.ContentTypesPart)));
    Assert.Equal(["Main"], WorkbookReader.Read(bytes).Modules.Select(m => m.Name));
  }

  [Fact]
  public void A_label_comes_through_byte_for_byte_and_stays_related_when_the_workbook_gains_a_project()
  {
    var path = Save("Target.xlsx", Labelled(BlankWorkbook.Build(macroEnabled: false)));

    var package = OpcPackage.Read(TargetWorkbook.Read(path).Write(Adding(Incoming("Main"))));

    Assert.Equal(Label, package.Get(LabelInfo));
    Assert.Equal(Properties, package.Get(Custom));
    Assert.Contains(package.RelationshipsOf(""), r => r.Target == LabelInfo);
  }

  [Fact]
  public void Only_the_project_and_nothing_else_changes_in_a_workbook_that_has_one()
  {
    var original = OpcPackage.Read(SyntheticWorkbook.Package([Existing("Main")]));
    var bytes = Labelled(original);

    var after = OpcPackage.Read(TargetWorkbook.Read(Save("Target.xlsm", bytes)).Write(Adding(Incoming("Added"))));

    var before = OpcPackage.Read(bytes);
    var changed = before.Names.Where(n => !before.Get(n).AsSpan().SequenceEqual(after.Get(n))).ToList();
    Assert.Equal(["xl/vbaProject.bin"], changed);
    Assert.Equal(before.Names.Order(), after.Names.Order());
  }

  [Fact]
  public void An_encrypted_workbook_cant_be_a_target()
  {
    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.Read(Fixtures.Workbook("protected_passtika.xlsb")));

    Assert.Contains("encrypted", error.Message);
    Assert.Contains("sensitivity label", error.Message);
  }

  [Fact]
  public void An_older_xls_cant_be_a_target_and_it_says_what_to_do()
  {
    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.Read(Fixtures.Workbook("SimpleMacro.xls")));

    Assert.Contains("Save it as .xlsm", error.Message);
  }

  [Fact]
  public void What_isnt_a_workbook_cant_be_a_target()
  {
    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.Read(Save("Notes.xlsm", [1, 2, 3])));

    Assert.Contains("not an Excel workbook", error.Message);
  }

  [Theory]
  [InlineData("Template.xltm")]
  [InlineData("Old.xla")]
  public void Only_the_kinds_tried_in_Excel_can_be_targets(string name)
  {
    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.Read(Save(name, BlankWorkbook.Build(true).Write())));

    Assert.Contains("can only be written into .xlsx, .xlsm, .xlsb, .xlam files", error.Message);
  }

  [Fact]
  public void A_signed_project_is_known_and_loses_its_signature_when_changed()
  {
    const string Signature = "http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature";
    var bytes = SyntheticWorkbook.Package([Existing("Main")], change: package =>
    {
      package.Set("xl/vbaProjectSignature.bin", [9, 9]);
      package.SetOverride("xl/vbaProjectSignature.bin", "application/vnd.ms-office.vbaProjectSignature");
      package.AddRelationship("xl/vbaProject.bin", Signature, "vbaProjectSignature.bin");
    });
    var target = TargetWorkbook.Read(Save("Target.xlsm", bytes));
    Assert.True(target.IsSigned);

    var package = OpcPackage.Read(target.Write(Adding(Incoming("Added"))));

    Assert.False(package.Has("xl/vbaProjectSignature.bin"));
    Assert.False(package.Has("xl/_rels/vbaProject.bin.rels"));
    Assert.DoesNotContain("vbaProjectSignature", Encoding.UTF8.GetString(package.Get(OpcPackage.ContentTypesPart)));
  }

  [Fact]
  public void A_locked_project_stays_locked()
  {
    const string Id = "{00000000-0000-0000-0000-000000000000}";
    var key = ProjectEncryption.ProjectKey(Id);
    var protection = $"CMG=\"{ProjectEncryption.Encrypt([1, 0, 0, 0], key, 2)}\"\r\nDPB=\"{ProjectEncryption.Encrypt([1, 2, 3], key, 4)}\"\r\nGC=\"{ProjectEncryption.Encrypt([0], key, 6)}\"\r\n";
    var target = TargetWorkbook.Read(Save("Target.xlsm", SyntheticWorkbook.Package([Existing("Main")], protection: protection)));
    Assert.True(target.IsLocked);

    var again = TargetWorkbook.Read(Save("After.xlsm", target.Write(Adding(Incoming("Added")))));

    Assert.True(again.IsLocked);
    Assert.Equal(["Added", "Main"], again.Project!.Modules.Select(m => m.Name));
  }

  [Fact]
  public void A_project_with_nothing_protecting_it_isnt_locked()
  {
    Assert.False(TargetWorkbook.Read(Fixtures.Workbook("SimpleMacro.xlsm")).IsLocked);
    Assert.False(TargetWorkbook.New().IsLocked);
    Assert.False(TargetWorkbook.New().IsSigned);
  }

  [Fact]
  public void Source_in_the_same_code_page_keeps_its_bytes_even_if_they_dont_decode()
  {
    var module = new VbaModule { Name = "A", Kind = VbaModuleKind.Standard, StreamName = "A", Source = "?", SourceBytes = [0x81, 0x8D] };

    Assert.Same(module.SourceBytes, TargetWorkbook.SourceIn(module, 1252, 1252));
  }

  [Fact]
  public void Source_goes_into_the_targets_code_page_from_a_real_workbook_in_another()
  {
    var source = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm"));
    var module = source.Modules.First(m => m.Kind == VbaModuleKind.Standard);
    var target = TargetWorkbook.Read(Save("Target.xlsm", SyntheticWorkbook.Package([Existing("Main")], codePage: 1252)));

    var read = WorkbookReader.Read(target.Write(new ModuleChange([module], [], [], source.CodePage)));

    Assert.Equal(module.Source, read.Modules.Single(m => m.Name == module.Name).Source);
  }

  [Fact]
  public void What_the_targets_code_page_cant_hold_stops_it_rather_than_turning_into_question_marks()
  {
    var source = "Attribute VB_Name = \"A\"\r\n' 日本語\r\n";
    var module = new VbaModule { Name = "A", Kind = VbaModuleKind.Standard, StreamName = "A", Source = source, SourceBytes = CodePages.Get(932).GetBytes(source) };

    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.SourceIn(module, 932, 1252));

    Assert.Contains("code page 1252", error.Message);
    Assert.Contains("A has a character", error.Message);
  }

  [Fact]
  public void A_new_workbook_takes_its_sources_code_page_so_nothing_has_to_change()
  {
    var source = WorkbookReader.Read(Fixtures.Workbook("45431.xlsm"));
    var module = source.Modules.First(m => m.Kind == VbaModuleKind.Standard);

    var read = WorkbookReader.Read(TargetWorkbook.New().Write(new ModuleChange([module], [], [], source.CodePage)));

    Assert.Equal(950, read.CodePage);
    Assert.Equal(module.SourceBytes, read.Modules.Single(m => m.Name == module.Name).SourceBytes);
  }

  [Fact]
  public void What_doesnt_read_back_as_written_is_refused()
  {
    var output = TargetWorkbook.New().Write(Adding(Incoming("Main")));

    var error = Assert.Throws<ExtractionException>(() => TargetWorkbook.CheckReadsBack(output, [("Main", [1, 2, 3])]));
    Assert.Contains("didn't read back", error.Message);

    Assert.Throws<ExtractionException>(() => TargetWorkbook.CheckReadsBack(output, [("Lost", [])]));
  }

  // The source for what follows: Paths uses ILogger.
  static readonly SyntheticModule[] Source =
  [
    new("ILogger", VbaModuleKind.Class, "Attribute VB_Name = \"ILogger\"\r\nOption Explicit\r\nPublic Sub Log(ByVal m As String)\r\nEnd Sub\r\n"),
    new("Paths", VbaModuleKind.Standard, "Attribute VB_Name = \"Paths\"\r\nOption Explicit\r\nPrivate m_log As ILogger\r\nPublic Sub Pick()\r\n    m_log.Log \"x\"\r\nEnd Sub\r\n"),
  ];

  TargetComparison Compare(TargetWorkbook target, params string[] picks)
  {
    var project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(Source));
    var import = new ModuleImport(DependencyAnalyser.Analyse(ProjectParser.Parse(project)));
    return ModuleClashes.Compare(project, "Tools.xlsm", import.Plan(picks), import, target);
  }

  TargetWorkbook Target(params SyntheticModule[] modules) => TargetWorkbook.Read(Save($"{Guid.NewGuid():N}.xlsm", SyntheticWorkbook.Package(modules)));

  [Fact]
  public void A_target_without_them_clashes_with_nothing()
  {
    var compared = Compare(Target(Existing("Main")), "Paths");

    Assert.Equal(["Paths", "ILogger"], compared.New);
    Assert.Empty(compared.Identical);
    Assert.Empty(compared.Clashes);
  }

  [Fact]
  public void An_identical_module_is_no_clash_whatever_blank_lines_end_it()
  {
    var compared = Compare(Target(Source[1] with { Source = Source[1].Source + "\r\n\r\n" }), "Paths");

    Assert.Equal(["Paths"], compared.Identical);
    Assert.Equal(["ILogger"], compared.New);
    Assert.Empty(compared.Clashes);
  }

  [Fact]
  public void Names_are_matched_ignoring_case_as_VBA_does()
  {
    Assert.Equal("Paths", Assert.Single(Compare(Target(Existing("PATHS")), "Paths").Clashes).Item);
  }

  [Fact]
  public void A_different_module_says_what_uses_it_in_the_target_and_that_nothing_coming_does()
  {
    var target = Target(Existing("Paths", body: "Public Sub Pick()\r\nEnd Sub\r\n"), Existing("Report", body: "Public Sub Run()\r\n    Paths.Pick\r\nEnd Sub\r\n"));

    var clash = Compare(target, "Paths").Clashes.Single();

    Assert.Equal("The target already has Paths, and it differs from the one in Tools.xlsm.", clash.Problem);
    Assert.Equal("Replaces the target's version, used there by Report.", clash.IfOverwritten);
    Assert.Equal("Keeps the target's version.", clash.IfSkipped);
    Assert.True(clash.CanOverwrite);
  }

  [Fact]
  public void A_dependency_already_there_says_what_coming_would_use_the_targets_version()
  {
    var compared = Compare(Target(Existing("ILogger", VbaModuleKind.Class)), "Paths");

    var clash = compared.Clashes.Single();
    Assert.Equal("ILogger", clash.Item);
    Assert.Equal("Keeps the target's version, which may differ, for Paths to use.", clash.IfSkipped);
    Assert.Equal("Replaces the target's version, which nothing else there uses.", clash.IfOverwritten);
    Assert.Equal(["Paths"], compared.New);
  }

  [Fact]
  public void A_different_kind_of_module_says_so()
  {
    var clash = Compare(Target(Existing("ILogger", VbaModuleKind.Standard)), "ILogger").Clashes.Single();

    Assert.Equal("The target's ILogger is a standard module, and the one in Tools.xlsm is a class.", clash.Problem);
    Assert.True(clash.CanOverwrite);
  }

  [Theory]
  [InlineData(VbaModuleKind.Document, "a sheet's or the workbook's module")]
  [InlineData(VbaModuleKind.Form, "a UserForm")]
  public void What_belongs_to_a_sheet_or_a_form_cant_be_replaced(VbaModuleKind kind, string what)
  {
    var clash = Compare(Target(Existing("ILogger", kind)), "Paths").Clashes.Single();

    Assert.Equal($"ILogger is {what} in the target, which can't be replaced.", clash.Problem);
    Assert.False(clash.CanOverwrite);
    Assert.Equal("Leaves this one out, so Paths will find the target's ILogger instead.", clash.IfSkipped);
  }

  [Fact]
  public void A_sheets_code_name_in_a_workbook_with_no_project_counts_too()
  {
    var package = BlankWorkbook.Build(macroEnabled: false);
    var sheet = package.Xml("xl/worksheets/sheet1.xml");
    sheet.Root!.AddFirst(new System.Xml.Linq.XElement(OpcPackage.Main + "sheetPr", new System.Xml.Linq.XAttribute("codeName", "Paths")));
    package.SetXml("xl/worksheets/sheet1.xml", sheet);

    var clash = Compare(TargetWorkbook.Read(Save("Target.xlsx", package.Write())), "Paths").Clashes.Single();

    Assert.False(clash.CanOverwrite);
    Assert.Contains("can't be replaced", clash.Problem);
  }

  [Fact]
  public void A_new_workbook_has_nothing_to_clash_with()
  {
    Assert.Empty(Compare(TargetWorkbook.New(), "Paths").Clashes);
  }
}
