using System.Net;
using PowerQueryDocumenter.Core.Application;
using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Export;
using PowerQueryDocumenter.Core.Model;
using PowerQueryDocumenter.Core.Output;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Application;

/// <summary>
/// End-to-end runs of the tool covering the acceptance criteria (requirements section 6) and the
/// exit codes (section 5.3).
/// </summary>
public sealed class CliRunnerTests : IDisposable
{
  private const string Server = "https://plantuml.test/plantuml";

  private readonly TempDirectory temp = new();

  public CliRunnerTests()
  {
    Fixtures.CopyQueriesTo(Queries);
  }

  private string Queries => temp.Combine("queries");

  private string QueryFile(string name) => Path.Combine(Queries, name);

  private string[] OutputFiles(string? folder = null) =>
    Directory.GetFiles(folder ?? Queries)
      .Select(f => Path.GetFileName(f))
      .Where(n => !n.EndsWith(".m", StringComparison.Ordinal))
      .Order(StringComparer.Ordinal)
      .ToArray();

  private TestApp App(HttpMessageHandler? handler = null, IUserInteraction? interaction = null, Dictionary<string, string>? variables = null) =>
    new(temp.Path, handler, interaction, variables);

  public void Dispose() => temp.Dispose();

  [Fact]
  public async Task AC1_default_run_writes_only_the_csv_next_to_the_inputs()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries);

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(new[] { "powerquery-dependencies.csv" }, OutputFiles());
    var expected = (await File.ReadAllTextAsync(Fixtures.ExpectedCsvPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
    var actual = (await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.csv"), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
    Assert.Equal(expected, actual);
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AC2_puml_and_html_are_written_alongside_the_csv()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "puml,html");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(
      new[] { "powerquery-dependencies.csv", "powerquery-dependencies.html", "powerquery-dependencies.puml" },
      OutputFiles());
    Assert.StartsWith("@startuml", await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.puml"), TestContext.Current.CancellationToken));
    Assert.Contains("d3js.org v7.9.0", await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.html"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task AC3_no_csv_without_a_format_fails_with_exit_code_2_and_writes_nothing()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--no-csv");

    Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    Assert.Empty(OutputFiles());
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("--no-csv", StringComparison.Ordinal));
    Assert.Contains("Usage:", app.Error.ToString(), StringComparison.Ordinal);
  }

  [Fact]
  public async Task AC4_existing_outputs_in_a_non_interactive_session_fail_with_exit_code_4_and_change_nothing()
  {
    var csv = QueryFile("powerquery-dependencies.csv");
    await File.WriteAllTextAsync(csv, "original", TestContext.Current.CancellationToken);
    var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(csv, timestamp);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "html");

    Assert.Equal(ExitCodes.OutputRefused, exitCode);
    Assert.Equal("original", await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken));
    Assert.Equal(timestamp, File.GetLastWriteTimeUtc(csv));
    Assert.Equal(new[] { "powerquery-dependencies.csv" }, OutputFiles());
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("--force", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AC5_rendering_without_a_server_fails_with_exit_code_2_naming_all_three_routes()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "png");

    Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    Assert.Empty(OutputFiles());
    var error = Assert.Single(app.Log.Messages(LogEventLevel.Error));
    Assert.Contains("--plantuml-server", error, StringComparison.Ordinal);
    Assert.Contains("PLANTUML_SERVER_URL", error, StringComparison.Ordinal);
    Assert.Contains(".env", error, StringComparison.Ordinal);
  }

  [Fact]
  public async Task AC6_and_AC7_query_named_Source_is_found_and_comment_or_string_mentions_are_not()
  {
    using var app = App();

    await app.RunAsync(Queries);

    var rows = await File.ReadAllLinesAsync(QueryFile("powerquery-dependencies.csv"), TestContext.Current.CancellationToken);
    Assert.Contains("\"Source\",\"Orders\"", rows);
    Assert.DoesNotContain("\"Customers\",\"Orders\"", rows);
    Assert.DoesNotContain("\"Orders\",\"Revenue & Margin (2024)\"", rows);
    Assert.DoesNotContain("\"Source\",\"Sales Region Map\"", rows);
  }

  [Fact]
  public async Task AC8_a_circular_dependency_warns_and_all_exports_still_complete()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "puml,html");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("Cycle A -> Cycle B -> Cycle A", StringComparison.Ordinal));
    Assert.Equal(3, OutputFiles().Length);
    Assert.Contains("<<cycle>>", await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.puml"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
  }

  [Fact]
  public async Task AC9_a_png_render_failure_does_not_stop_the_other_exports_and_exits_with_5()
  {
    var handler = StubHttpMessageHandler.FailingFor("png", HttpStatusCode.InternalServerError);
    using var app = App(handler);

    var exitCode = await app.RunAsync(Queries, "-f", "png,svg,html", "--plantuml-server", Server);

    Assert.Equal(ExitCodes.ExportFailed, exitCode);
    Assert.Equal(
      new[] { "powerquery-dependencies.csv", "powerquery-dependencies.html", "powerquery-dependencies.svg" },
      OutputFiles());
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("png", StringComparison.Ordinal) && m.Contains("500", StringComparison.Ordinal));
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Rendered_formats_are_written_without_a_puml_file_unless_requested()
  {
    var handler = StubHttpMessageHandler.WorkingServer();
    using var app = App(handler);

    var exitCode = await app.RunAsync(Queries, "--no-csv", "-f", "svg,png,pdf", "--plantuml-server", Server);

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(
      new[] { "powerquery-dependencies.pdf", "powerquery-dependencies.png", "powerquery-dependencies.svg" },
      OutputFiles());
    Assert.Equal(3, handler.Requests.Count);
    Assert.All(handler.Requests, r => Assert.StartsWith(Server + "/", r.Uri.AbsoluteUri));
  }

  [Fact]
  public async Task The_server_can_come_from_a_dotenv_file_in_the_current_directory()
  {
    temp.WriteFile(".env", "PLANTUML_SERVER_URL=https://dotenv.test/");
    var handler = StubHttpMessageHandler.WorkingServer();
    using var app = App(handler);

    var exitCode = await app.RunAsync(Queries, "-f", "svg");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal("dotenv.test", Assert.Single(handler.Requests).Uri.Host);
  }

  [Fact]
  public async Task The_server_can_come_from_the_environment()
  {
    var handler = StubHttpMessageHandler.WorkingServer();
    using var app = App(handler, variables: new() { ["PLANTUML_SERVER_URL"] = "http://env.test:8080" });

    var exitCode = await app.RunAsync(Queries, "-f", "svg");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal("env.test", Assert.Single(handler.Requests).Uri.Host);
  }

  [Fact]
  public async Task Output_name_and_folder_are_honoured_and_the_folder_is_created()
  {
    using var app = App();
    var output = temp.Combine("docs", "deep");

    var exitCode = await app.RunAsync(Queries, "-n", "sales-model", "-o", output, "-f", "html");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(new[] { "sales-model.csv", "sales-model.html" }, OutputFiles(output));
    Assert.Empty(OutputFiles());
    Assert.Contains("<h1>sales-model</h1>", await File.ReadAllTextAsync(Path.Combine(output, "sales-model.html"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Relative_paths_are_resolved_against_the_current_directory()
  {
    using var app = App();

    var exitCode = await app.RunAsync("queries", "-o", "out");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.True(File.Exists(temp.Combine("out", "powerquery-dependencies.csv")));
  }

  [Fact]
  public async Task Force_overwrites_existing_outputs()
  {
    var csv = QueryFile("powerquery-dependencies.csv");
    await File.WriteAllTextAsync(csv, "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--force");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.StartsWith("Query Name,", (await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken)).TrimStart('﻿'));
  }

  [Fact]
  public async Task Skip_leaves_existing_outputs_and_writes_the_rest()
  {
    var csv = QueryFile("powerquery-dependencies.csv");
    await File.WriteAllTextAsync(csv, "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--on-exists", "skip", "-f", "puml");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal("original", await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken));
    Assert.True(File.Exists(QueryFile("powerquery-dependencies.puml")));
  }

  [Fact]
  public async Task When_everything_is_skipped_no_success_message_is_shown()
  {
    await File.WriteAllTextAsync(QueryFile("powerquery-dependencies.csv"), "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--on-exists", "skip");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("No output files were written", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Interactive_prompt_answers_are_applied_per_file()
  {
    await File.WriteAllTextAsync(QueryFile("powerquery-dependencies.csv"), "keep me", TestContext.Current.CancellationToken);
    await File.WriteAllTextAsync(QueryFile("powerquery-dependencies.puml"), "replace me", TestContext.Current.CancellationToken);
    var interaction = new FakeUserInteraction(true, OverwriteAnswer.No, OverwriteAnswer.Yes);
    using var app = App(interaction: interaction);

    var exitCode = await app.RunAsync(Queries, "-f", "puml");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(2, interaction.Asked.Count);
    Assert.Equal("keep me", await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.csv"), TestContext.Current.CancellationToken));
    Assert.StartsWith("@startuml", await File.ReadAllTextAsync(QueryFile("powerquery-dependencies.puml"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Quitting_at_the_prompt_writes_nothing_and_exits_with_4()
  {
    await File.WriteAllTextAsync(QueryFile("powerquery-dependencies.puml"), "original", TestContext.Current.CancellationToken);
    using var app = App(interaction: new FakeUserInteraction(true, OverwriteAnswer.Quit));

    var exitCode = await app.RunAsync(Queries, "-f", "puml");

    Assert.Equal(ExitCodes.OutputRefused, exitCode);
    Assert.Equal(new[] { "powerquery-dependencies.puml" }, OutputFiles());
  }

  [Fact]
  public async Task A_missing_input_folder_exits_with_3()
  {
    using var app = App();

    var exitCode = await app.RunAsync(temp.Combine("missing"));

    Assert.Equal(ExitCodes.InputError, exitCode);
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("does not exist", StringComparison.Ordinal));
  }

  [Fact]
  public async Task A_folder_without_m_files_exits_with_3_and_reports_no_success()
  {
    var empty = temp.Combine("empty");
    Directory.CreateDirectory(empty);
    using var app = App();

    var exitCode = await app.RunAsync(empty);

    Assert.Equal(ExitCodes.InputError, exitCode);
    Assert.Empty(Directory.GetFiles(empty));
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("No .m files", StringComparison.Ordinal));
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task An_unreadable_query_file_still_produces_outputs_but_exits_with_3()
  {
    try
    {
      File.CreateSymbolicLink(QueryFile("Broken.m"), temp.Combine("does-not-exist.m"));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
    {
      Assert.Skip("Creating a symbolic link is not permitted here, so an unreadable file cannot be simulated.");
    }

    using var app = App();

    var exitCode = await app.RunAsync(Queries);

    Assert.Equal(ExitCodes.InputError, exitCode);
    Assert.True(File.Exists(QueryFile("powerquery-dependencies.csv")));
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("Broken.m", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Help_and_version_exit_with_0()
  {
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync("--help"));
    Assert.Contains("--output-name", app.Output.ToString(), StringComparison.Ordinal);
    Assert.Equal(ExitCodes.Success, await app.RunAsync("--version"));
  }

  [Fact]
  public async Task Quiet_shows_warnings_and_errors_only()
  {
    using var app = App();

    await app.RunAsync(Queries, "-q");

    Assert.DoesNotContain(app.Log.Events, e => e.Level < LogEventLevel.Warning);
    Assert.Contains(app.Log.Events, e => e.Level == LogEventLevel.Warning);
  }

  [Fact]
  public async Task Verbose_shows_debug_output()
  {
    using var app = App();

    await app.RunAsync(Queries, "--verbose");

    Assert.Contains(app.Log.Events, e => e.Level == LogEventLevel.Debug);
  }

  [Fact]
  public async Task The_summary_lists_counts_and_files()
  {
    using var app = App();

    await app.RunAsync(Queries);

    var summary = Assert.Single(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Found ", StringComparison.Ordinal));
    Assert.Equal("Found 11 queries and 11 dependencies (roots: 3, leaves: 1, isolated: 1, circular dependencies: 1)", summary);
  }

  [Fact]
  public async Task One_failing_exporter_does_not_stop_the_others()
  {
    using var app = new TestApp(temp.Path)
    {
      CreateExporters = _ => new ExporterRegistry([new CsvExporter(), new ThrowingExporter(OutputFormat.Puml, new IOException("Disk full")), new HtmlExporter()]),
    };

    var exitCode = await app.RunAsync(Queries, "-f", "puml,html");

    Assert.Equal(ExitCodes.ExportFailed, exitCode);
    Assert.Equal(new[] { "powerquery-dependencies.csv", "powerquery-dependencies.html" }, OutputFiles());
  }

  [Fact]
  public async Task An_unexpected_error_exits_with_1_without_a_stack_trace()
  {
    using var app = new TestApp(temp.Path) { CreateExporters = _ => throw new InvalidOperationException("Something odd") };

    var exitCode = await app.RunAsync(Queries);

    Assert.Equal(ExitCodes.UnexpectedError, exitCode);
    var error = Assert.Single(app.Log.Events, e => e.Level == LogEventLevel.Error);
    Assert.Contains("Something odd", error.RenderMessage(), StringComparison.Ordinal);
    Assert.Null(error.Exception);
    Assert.DoesNotContain(app.Log.Events, e => e.Exception is not null);
  }

  [Fact]
  public async Task Verbose_output_includes_the_exception_detail()
  {
    using var app = new TestApp(temp.Path) { CreateExporters = _ => throw new InvalidOperationException("Something odd") };

    var exitCode = await app.RunAsync(Queries, "--verbose");

    Assert.Equal(ExitCodes.UnexpectedError, exitCode);
    Assert.Contains(app.Log.Events, e => e.Level == LogEventLevel.Debug && e.Exception is InvalidOperationException);
  }

  [Fact]
  public async Task Cancellation_exits_with_130_and_leaves_no_partial_files()
  {
    using var cancellation = new CancellationTokenSource();
    using var app = new TestApp(temp.Path)
    {
      CreateExporters = _ => new ExporterRegistry([new CancellingExporter(cancellation)]),
    };

    var exitCode = await app.RunWithCancellationAsync(cancellation.Token, Queries);

    Assert.Equal(ExitCodes.Cancelled, exitCode);
    Assert.Empty(OutputFiles());
  }

  private sealed class ThrowingExporter(OutputFormat format, Exception exception) : IExporter
  {
    public OutputFormat Format => format;

    public string FileExtension => format.FileExtension();

    public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
    {
      await output.WriteAsync("partial"u8.ToArray(), cancellationToken);
      throw exception;
    }
  }

  private sealed class CancellingExporter(CancellationTokenSource cancellation) : IExporter
  {
    public OutputFormat Format => OutputFormat.Csv;

    public string FileExtension => ".csv";

    public async Task ExportAsync(DependencyGraph graph, ExportContext context, Stream output, CancellationToken cancellationToken)
    {
      await output.WriteAsync("partial"u8.ToArray(), cancellationToken);
      await cancellation.CancelAsync();
      cancellationToken.ThrowIfCancellationRequested();
    }
  }
}
