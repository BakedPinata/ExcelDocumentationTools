using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests;

/// <summary>Checks the fixtures are copied next to the test assembly, so other tests fail for the right reason.</summary>
public class FixtureTests
{
    [Fact]
    public void SampleVba_has_twelve_modules()
    {
        var files = Directory.GetFiles(Fixtures.SampleVbaFolder).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(12, files.Count);
        Assert.Contains("IOrderRepository.cls", files);
        Assert.Contains("ThisWorkbook.doc.cls", files);
    }

    [Fact]
    public void Workbook_fixtures_are_present()
    {
        Assert.True(File.Exists(Fixtures.Workbook("SimpleMacro.xlsm")));
    }
}
