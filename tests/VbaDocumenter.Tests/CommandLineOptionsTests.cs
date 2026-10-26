namespace VbaDocumenter.Tests;

public class CommandLineOptionsTests
{
    static readonly string Workbook = Path.GetFullPath(Path.Combine("books", "MyBook.xlsm"));

    [Fact]
    public void Defaults_to_a_VBAProject_folder_next_to_the_workbook()
    {
        var options = CommandLineOptions.Parse([Workbook], out var error);

        Assert.Null(error);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Workbook)!, "VBAProject"), options!.OutputFolder);
        Assert.False(options.EmbedSource);
    }

    [Fact]
    public void Accepts_an_output_folder_and_embed_source_in_any_order()
    {
        var options = CommandLineOptions.Parse(["--embed-source", Workbook, "--out", "elsewhere"], out _);

        Assert.Equal(Path.GetFullPath("elsewhere"), options!.OutputFolder);
        Assert.True(options.EmbedSource);
    }

    [Theory]
    [InlineData(new string[0], "No workbook")]
    [InlineData(new[] { "a.xlsm", "--out" }, "--out needs a folder")]
    [InlineData(new[] { "a.xlsm", "--bogus" }, "Unknown option")]
    [InlineData(new[] { "a.xlsm", "b.xlsm" }, "Only one workbook")]
    public void Rejects_invalid_arguments(string[] args, string expectedError)
    {
        Assert.Null(CommandLineOptions.Parse(args, out var error));
        Assert.Contains(expectedError, error);
    }
}
