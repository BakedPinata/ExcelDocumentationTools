using System.Text;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Output;

public sealed class ProjectExporterTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), $"vbadoc-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    static VbaProject SampleProject() => WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()));

    static VbaModule Module(string name, VbaModuleKind kind, byte[] source) =>
        new() { Name = name, Kind = kind, StreamName = name, SourceBytes = source, Source = Encoding.Latin1.GetString(source) };

    static VbaProject Project(params VbaModule[] modules) =>
        new() { Name = "VBAProject", CodePage = 1252, Modules = modules };

    string[] FilesInFolder() => Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Writes_one_file_per_module_with_the_right_extension()
    {
        ProjectExporter.Export(SampleProject(), _folder);

        Assert.Equal(
        [
            ".gitattributes", "AppInit.bas", "CsvOrderRepository.cls", "IOrderRepository.cls", "Logger.bas", "Main.bas",
            "Order.cls", "OrderService.cls", "ProgressReporter.cls", "Sheet1.cls", "SheetOrderRepository.cls",
            "ThisWorkbook.cls", "Utils.bas",
        ], FilesInFolder());
    }

    [Fact]
    public void Class_and_document_files_get_the_VBA_editor_header_and_standard_modules_do_not()
    {
        ProjectExporter.Export(SampleProject(), _folder);

        var order = File.ReadAllText(Path.Combine(_folder, "Order.cls"));
        var sheet = File.ReadAllText(Path.Combine(_folder, "Sheet1.cls"));
        var main = File.ReadAllText(Path.Combine(_folder, "Main.bas"));

        Assert.StartsWith(ProjectExporter.ClassHeader + "Attribute VB_Name = \"Order\"\r\n", order);
        Assert.StartsWith(ProjectExporter.ClassHeader + "Attribute VB_Name = \"Sheet1\"\r\n", sheet);
        Assert.StartsWith("Attribute VB_Name = \"Main\"\r\n", main);
    }

    [Fact]
    public void Forms_are_written_as_frm_without_a_designer_header()
    {
        var form = Module("UserForm1", VbaModuleKind.Form, "Attribute VB_Name = \"UserForm1\"\r\n"u8.ToArray());

        Assert.Equal("UserForm1.frm", ProjectExporter.FileNameFor(form));
        Assert.Equal(form.SourceBytes, ProjectExporter.FileContent(form));
    }

    [Fact]
    public void Writes_CRLF_and_keeps_code_page_bytes_unchanged()
    {
        // "é" as a single code page 1252 byte, with a mix of line endings.
        byte[] source = [.. "Attribute VB_Name = \"M\"\n' caf"u8, 0xE9, .. "\r' x\r\n' y"u8];

        var content = ProjectExporter.FileContent(Module("M", VbaModuleKind.Standard, source));

        Assert.Equal([.. "Attribute VB_Name = \"M\"\r\n' caf"u8, 0xE9, .. "\r\n' x\r\n' y"u8], content);
    }

    [Fact]
    public void Running_twice_writes_nothing_the_second_time()
    {
        ProjectExporter.Export(SampleProject(), _folder);
        var before = Directory.GetFiles(_folder).ToDictionary(f => f, File.ReadAllBytes);

        var second = ProjectExporter.Export(SampleProject(), _folder);

        Assert.Empty(second.Written);
        Assert.Equal(12, second.Unchanged.Count);
        Assert.False(second.CreatedGitAttributes);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Deletes_module_files_that_are_no_longer_in_the_workbook_and_nothing_else()
    {
        Directory.CreateDirectory(_folder);
        foreach (var name in new[] { "Removed.bas", "OldClass.cls", "OldForm.frm", "README.md", "notes.txt", "OldForm.frx" })
            File.WriteAllText(Path.Combine(_folder, name), "x");
        Directory.CreateDirectory(Path.Combine(_folder, ".git"));
        File.WriteAllText(Path.Combine(_folder, ".git", "Keep.bas"), "x"); // only the top level is ours

        var result = ProjectExporter.Export(SampleProject(), _folder);

        Assert.Equal(["OldClass.cls", "OldForm.frm", "Removed.bas"], result.Deleted);
        Assert.True(File.Exists(Path.Combine(_folder, "README.md")));
        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_folder, "OldForm.frx")));
        Assert.True(File.Exists(Path.Combine(_folder, ".git", "Keep.bas")));
    }

    [Fact]
    public void A_module_renamed_only_by_case_gets_the_new_file_name()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "utils.bas"), "old");

        ProjectExporter.Export(Project(Module("Utils", VbaModuleKind.Standard, "Attribute VB_Name = \"Utils\"\r\n"u8.ToArray())), _folder);

        Assert.Contains("Utils.bas", FilesInFolder());
        Assert.DoesNotContain("utils.bas", FilesInFolder());
    }

    [Fact]
    public void Never_overwrites_an_existing_gitattributes()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, ".gitattributes"), "mine");

        var result = ProjectExporter.Export(SampleProject(), _folder);

        Assert.False(result.CreatedGitAttributes);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_folder, ".gitattributes")));
    }

    [Fact]
    public void Created_gitattributes_turns_off_conversion_for_source_files()
    {
        ProjectExporter.Export(SampleProject(), _folder);

        var attributes = File.ReadAllText(Path.Combine(_folder, ".gitattributes"));
        Assert.Contains("*.bas -text diff", attributes);
        Assert.Contains("*.cls -text diff", attributes);
        Assert.Contains("*.frm -text diff", attributes);
    }

    [Fact]
    public void Output_is_identical_across_separate_exports()
    {
        var otherFolder = _folder + "-2";
        try
        {
            ProjectExporter.Export(SampleProject(), _folder);
            ProjectExporter.Export(SampleProject(), otherFolder);

            foreach (var path in Directory.GetFiles(_folder))
                Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(Path.Combine(otherFolder, Path.GetFileName(path))));
        }
        finally
        {
            Directory.Delete(otherFolder, recursive: true);
        }
    }
}
