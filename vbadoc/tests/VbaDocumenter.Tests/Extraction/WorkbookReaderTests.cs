using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Extraction;

public class WorkbookReaderTests
{
  // ---- Real workbooks saved by Excel (see Fixtures/Workbooks/README.md) ----

  [Theory]
  [InlineData("SimpleMacro.xlsm")]
  [InlineData("SimpleMacro.xls")]
  public void Reads_the_same_project_from_zip_based_and_legacy_files(string fileName)
  {
    var project = WorkbookReader.Read(Fixtures.Workbook(fileName));

    Assert.Equal("VBAProject", project.Name);
    Assert.Equal(1252, project.CodePage);
    Assert.Equal(["Module1", "Sheet1", "Sheet2", "Sheet3", "ThisWorkbook"], project.Modules.Select(m => m.Name));
    Assert.Equal(VbaModuleKind.Standard, project.Modules[0].Kind);
    Assert.All(project.Modules.Skip(1), m => Assert.Equal(VbaModuleKind.Document, m.Kind));

    var module1 = project.Modules[0].Source;
    Assert.StartsWith("Attribute VB_Name = \"Module1\"\r\n", module1);
    Assert.Contains("Sub TestMacro()", module1);
    Assert.Contains("    ActiveCell.FormulaR1C1 = \"This is a macro workbook\"\r\n", module1);
  }

  [Fact]
  public void Sorts_modules_by_name()
  {
    var project = WorkbookReader.Read(Fixtures.Workbook("SquareMacro.xls"));

    Assert.Equal(["Sheet1", "Sheet2", "Sheet3", "ThisWorkbook", "UserDefinedFunctions"], project.Modules.Select(m => m.Name));
    Assert.Equal(VbaModuleKind.Standard, project.Modules[^1].Kind);
  }

  [Theory]
  [InlineData("45431.xlsm", 950)]
  [InlineData("suffix-generator.xlsm", 10000)]
  public void Reads_projects_in_other_code_pages(string fileName, int expectedCodePage)
  {
    var project = WorkbookReader.Read(Fixtures.Workbook(fileName));

    Assert.Equal(expectedCodePage, project.CodePage);
    Assert.Contains(project.Modules, m => m.Kind == VbaModuleKind.Standard && m.Source.Contains("End Sub"));
  }

  [Fact]
  public void Keeps_non_ascii_source_bytes_and_decodes_them()
  {
    var project = WorkbookReader.Read(Fixtures.Workbook("57181.xlsm"));
    var module = project.Modules.Single(m => m.Name == "Summary_graph");

    Assert.Contains("Redirigés_Smry_FormPil", module.Source);
    Assert.Contains((byte)0xE9, module.SourceBytes); // é in code page 1252
    Assert.Contains(project.Modules, m => m.Name == "DieseArbeitsmappe" && m.Kind == VbaModuleKind.Document);
  }

  [Fact]
  public void Reports_an_encrypted_workbook()
  {
    var ex = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(Fixtures.Workbook("protected_passtika.xlsb")));
    Assert.Contains("encrypted", ex.Message);
  }

  [Fact]
  public void Reports_a_workbook_without_vba()
  {
    var ex = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(Fixtures.Workbook("xlmmacro.xlsm")));
    Assert.Contains("no VBA project", ex.Message);
  }

  [Fact]
  public void Reads_a_workbook_that_is_open_for_writing_elsewhere()
  {
    // Excel holds the file open with write access; FileShare.ReadWrite lets us read it anyway.
    var copy = Path.Combine(Path.GetTempPath(), $"vbadoc-{Guid.NewGuid():N}.xlsm");
    File.Copy(Fixtures.Workbook("SimpleMacro.xlsm"), copy);
    try
    {
      using var heldOpen = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
      Assert.Equal(5, WorkbookReader.Read(copy).Modules.Count);
    }
    finally
    {
      File.Delete(copy);
    }
  }

  // ---- Synthetic workbooks, for cases no real fixture covers ----

  [Fact]
  public void Reads_all_SampleVba_modules_with_their_kinds()
  {
    var modules = Fixtures.SampleVbaModules();
    var project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(modules));

    Assert.Equal(12, project.Modules.Count);
    Assert.Equal(VbaModuleKind.Document, project.Modules.Single(m => m.Name == "ThisWorkbook").Kind);
    Assert.Equal(VbaModuleKind.Class, project.Modules.Single(m => m.Name == "OrderService").Kind);
    Assert.Equal(VbaModuleKind.Standard, project.Modules.Single(m => m.Name == "Main").Kind);
    foreach (var expected in modules)
      Assert.Equal(expected.Source.ReplaceLineEndings("\r\n"), project.Modules.Single(m => m.Name == expected.Name).Source);
  }

  [Fact]
  public void Reads_a_legacy_compound_file()
  {
    var project = WorkbookReader.Read(SyntheticWorkbook.Xls(Fixtures.SampleVbaModules()));

    Assert.Equal(12, project.Modules.Count);
  }

  [Fact]
  public void Classifies_forms()
  {
    var project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(
        [new SyntheticModule("UserForm1", VbaModuleKind.Form, "Attribute VB_Name = \"UserForm1\"\r\n")]));

    Assert.Equal(VbaModuleKind.Form, project.Modules.Single().Kind);
  }

  [Fact]
  public void Reports_an_encrypted_compound_file()
  {
    Assert.Throws<ExtractionException>(() => WorkbookReader.Read(SyntheticWorkbook.Encrypted()));
  }

  [Fact]
  public void Missing_module_stream_is_an_error_not_a_skipped_module()
  {
    // If it were skipped, the exporter would then delete that module's existing file.
    var workbook = SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules(), tamper: vba => vba.Delete("Order"));

    var ex = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(workbook));
    Assert.Contains("'Order'", ex.Message);
  }

  [Fact]
  public void Module_without_source_is_reported_clearly()
  {
    // Replace the module stream with bytes that hold no compressed container at the source offset.
    var workbook = SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules(), tamper: vba =>
    {
      vba.Delete("Utils");
      using var stream = vba.CreateStream("Utils");
      stream.Write(new byte[100]);
    });

    var ex = Assert.Throws<ExtractionException>(() => WorkbookReader.Read(workbook));
    Assert.Contains("'Utils'", ex.Message);
    Assert.Contains("stomping", ex.Message);
  }

  [Fact]
  public void Reports_a_file_that_is_not_a_workbook()
  {
    var ex = Assert.Throws<ExtractionException>(() => WorkbookReader.Read("not a workbook"u8.ToArray()));
    Assert.Contains("not an Excel workbook", ex.Message);
  }

  [Fact]
  public void Reports_a_corrupt_zip()
  {
    var workbook = SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules());

    Assert.Throws<ExtractionException>(() => WorkbookReader.Read(workbook[..(workbook.Length / 2)]));
  }
}
