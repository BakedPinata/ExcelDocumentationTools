namespace VbaDocumenter.Tests;

/// <summary>Checks the SampleVba fixture is copied next to the test assembly. Later stages parse it.</summary>
public class FixtureTests
{
    public static string SampleVbaFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleVba");

    [Fact]
    public void SampleVba_has_twelve_modules()
    {
        var files = Directory.GetFiles(SampleVbaFolder).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(12, files.Count);
        Assert.Contains("IOrderRepository.cls", files);
        Assert.Contains("ThisWorkbook.doc.cls", files);
    }
}
