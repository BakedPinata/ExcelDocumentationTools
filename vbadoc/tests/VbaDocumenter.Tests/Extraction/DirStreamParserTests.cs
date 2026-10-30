using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Extraction;

public class DirStreamParserTests
{
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
