using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Tests.Extraction;

public class ProjectStreamParserTests
{
    [Fact]
    public void Classifies_each_kind_of_module()
    {
        const string project =
            "ID=\"{5A3C1E2F-0000-0000-0000-000000000000}\"\r\n" +
            "Document=ThisWorkbook/&H00000000\r\n" +
            "Document=Sheet1/&H00000000\r\n" +
            "Module=Main\r\n" +
            "Class=Order\r\n" +
            "Package={AC9F2F90-E877-11CE-9F68-00AA00574A4F}\r\n" +
            "BaseClass=UserForm1\r\n" +
            "Name=\"VBAProject\"\r\n" +
            "\r\n" +
            "[Host Extender Info]\r\n" +
            "&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n" +
            "\r\n" +
            "[Workspace]\r\n" +
            "Module=10, 10, 500, 300, \r\n";

        var kinds = ProjectStreamParser.ParseModuleKinds(project);

        Assert.Equal(VbaModuleKind.Document, kinds["ThisWorkbook"]);
        Assert.Equal(VbaModuleKind.Document, kinds["sheet1"]); // lookups ignore case, like VBA
        Assert.Equal(VbaModuleKind.Standard, kinds["Main"]);
        Assert.Equal(VbaModuleKind.Class, kinds["Order"]);
        Assert.Equal(VbaModuleKind.Form, kinds["UserForm1"]);
        Assert.Equal(5, kinds.Count); // nothing picked up from the [Workspace] section
    }
}
