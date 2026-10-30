using System.Text;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class CsvExporterTests
{
  private static readonly ExportContext Context = new("test", new DateTimeOffset(2026, 10, 2, 10, 3, 0, TimeSpan.FromHours(8)));

  private static async Task<byte[]> ExportAsync(DependencyGraph graph)
  {
    using var stream = new MemoryStream();
    await new CsvExporter().ExportAsync(graph, Context, stream, TestContext.Current.CancellationToken);
    return stream.ToArray();
  }

  private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿');

  private static string[] Lines(byte[] bytes) =>
    Text(bytes).Split(Environment.NewLine).Where(l => l.Length > 0).ToArray();

  [Fact]
  public void The_header_is_defined_in_one_place_with_the_new_column_name()
  {
    Assert.Equal("Query Name,Referenced By Query Name", CsvExporter.HeaderRow);
  }

  [Fact]
  public async Task Fixture_output_matches_the_expected_file()
  {
    var graph = await Fixtures.GraphAsync();
    var expected = (await File.ReadAllTextAsync(Fixtures.ExpectedCsvPath, TestContext.Current.CancellationToken))
      .ReplaceLineEndings("\n");

    var actual = Text(await ExportAsync(graph)).ReplaceLineEndings("\n");

    Assert.Equal(expected, actual);
  }

  [Fact]
  public async Task Output_is_UTF8_with_a_byte_order_mark_and_platform_line_endings()
  {
    var bytes = await ExportAsync(Fixtures.Graph(["A>B"]));

    Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    Assert.Equal(
      $"Query Name,Referenced By Query Name{Environment.NewLine}\"A\",\"B\"{Environment.NewLine}\"B\",{Environment.NewLine}",
      Text(bytes));
  }

  [Fact]
  public async Task One_row_is_written_per_dependent_in_name_order()
  {
    var lines = Lines(await ExportAsync(Fixtures.Graph(["Source>zeta", "Source>Alpha", "Source>beta"])));

    Assert.Equal(
      new[]
      {
        CsvExporter.HeaderRow,
        "\"Alpha\",",
        "\"beta\",",
        "\"Source\",\"Alpha\"",
        "\"Source\",\"beta\"",
        "\"Source\",\"zeta\"",
        "\"zeta\",",
      },
      lines);
  }

  [Fact]
  public async Task Quotes_in_names_are_doubled()
  {
    var lines = Lines(await ExportAsync(Fixtures.Graph(["Say \"Hi\">Report, 2024"])));

    Assert.Contains("\"Say \"\"Hi\"\"\",\"Report, 2024\"", lines);
  }

  [Theory]
  [InlineData("plain", "\"plain\"")]
  [InlineData("a\"b", "\"a\"\"b\"")]
  [InlineData("", "\"\"")]
  public void Quote_escapes_embedded_quotes(string value, string expected)
  {
    Assert.Equal(expected, CsvExporter.Quote(value));
  }

  [Fact]
  public void The_exporter_describes_its_format()
  {
    var exporter = new CsvExporter();

    Assert.Equal(OutputFormat.Csv, exporter.Format);
    Assert.Equal(".csv", exporter.FileExtension);
  }
}
