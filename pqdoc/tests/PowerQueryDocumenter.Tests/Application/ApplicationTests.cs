using PowerQueryDocumenter.Core.Application;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Tests.TestSupport;

namespace PowerQueryDocumenter.Tests.Application;

public sealed class ErrorDescriberTests
{
  [Fact]
  public void Render_failures_keep_their_own_message()
  {
    var message = ErrorDescriber.DescribeOutputFailure(new PlantUmlRenderException("the server returned HTTP 500"));

    Assert.Equal("the server returned HTTP 500", message);
  }

  [Fact]
  public void Permission_problems_are_explained()
  {
    Assert.Contains("permission denied", ErrorDescriber.DescribeOutputFailure(new UnauthorizedAccessException("Access to the path is denied.")), StringComparison.Ordinal);
  }

  [Fact]
  public void Invalid_paths_are_explained()
  {
    Assert.Contains("does not exist", ErrorDescriber.DescribeOutputFailure(new DirectoryNotFoundException()), StringComparison.Ordinal);
    Assert.Contains("too long", ErrorDescriber.DescribeOutputFailure(new PathTooLongException()), StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(unchecked((int)0x80070070))]
  [InlineData(unchecked((int)0x80070027))]
  [InlineData(28)]
  public void A_full_disk_is_recognised(int hresult)
  {
    var exception = new IOException("No space left on device", hresult);

    Assert.True(ErrorDescriber.IsDiskFull(exception));
    Assert.Equal("there is not enough disk space", ErrorDescriber.DescribeOutputFailure(exception));
  }

  [Fact]
  public void Other_IO_errors_suggest_the_file_may_be_open()
  {
    var message = ErrorDescriber.DescribeOutputFailure(new IOException("The process cannot access the file."));

    Assert.Contains("open in another program", message, StringComparison.Ordinal);
  }
}

public sealed class ExporterRegistryTests
{
  [Fact]
  public void Without_a_server_only_local_formats_are_available()
  {
    using var client = new HttpClient(StubHttpMessageHandler.Unused());

    var registry = ExporterRegistry.CreateDefault(client, null, TimeSpan.FromSeconds(30));

    Assert.Equal(
      new[] { OutputFormat.Csv, OutputFormat.Puml, OutputFormat.Html },
      registry.Formats.Order().ToArray());
    var exception = Assert.Throws<InvalidOperationException>(() => registry.Get(OutputFormat.Png));
    Assert.Contains("png", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void With_a_server_every_format_is_available()
  {
    using var client = new HttpClient(StubHttpMessageHandler.Unused());

    var registry = ExporterRegistry.CreateDefault(client, new Uri("https://plantuml.test"), TimeSpan.FromSeconds(30));

    Assert.Equal(Enum.GetValues<OutputFormat>(), registry.Formats.Order().ToArray());
    Assert.All(Enum.GetValues<OutputFormat>(), format => Assert.Equal(format, registry.Get(format).Format));
  }

  [Fact]
  public void Every_exporter_extension_matches_its_format()
  {
    using var client = new HttpClient(StubHttpMessageHandler.Unused());
    var registry = ExporterRegistry.CreateDefault(client, new Uri("https://plantuml.test"), TimeSpan.FromSeconds(30));

    Assert.All(Enum.GetValues<OutputFormat>(), format => Assert.Equal("." + format.CliName(), registry.Get(format).FileExtension));
  }
}
