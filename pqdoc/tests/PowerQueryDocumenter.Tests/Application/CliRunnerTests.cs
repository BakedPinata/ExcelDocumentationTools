using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExcelDocumenter.Shared;
using ExcelDocumenter.Shared.Files;
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

  /// <summary>The outputs in a folder: everything but query files and the .gitattributes written with them.</summary>
  private string[] OutputFiles(string? folder = null) =>
    Directory.GetFiles(folder ?? Queries)
      .Select(f => Path.GetFileName(f))
      .Where(n => !n.EndsWith(".m", StringComparison.Ordinal) && n != ".gitattributes")
      .Order(StringComparer.Ordinal)
      .ToArray();

  private string[] QueryFiles(string folder) =>
    Directory.GetFiles(folder, "*.m").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();

  private TestApp App(HttpMessageHandler? handler = null, IUserInteraction? interaction = null, Dictionary<string, string>? variables = null) =>
    new(temp.Path, handler, interaction, variables);

  public void Dispose() => temp.Dispose();

  [Fact]
  public async Task AC1_default_run_writes_only_the_page_next_to_the_inputs()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries);

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(new[] { "queries-powerquery.html" }, OutputFiles());
    Assert.Contains("d3js.org v7.9.0", await File.ReadAllTextAsync(QueryFile("queries-powerquery.html"), TestContext.Current.CancellationToken));
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AC2_csv_puml_and_html_are_written_side_by_side()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "csv,puml,html");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(
      new[] { "queries-powerquery.csv", "queries-powerquery.html", "queries-powerquery.puml" },
      OutputFiles());
    var expected = (await File.ReadAllTextAsync(Fixtures.ExpectedCsvPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
    var actual = (await File.ReadAllTextAsync(QueryFile("queries-powerquery.csv"), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
    Assert.Equal(expected, actual);
    Assert.StartsWith("@startuml", await File.ReadAllTextAsync(QueryFile("queries-powerquery.puml"), TestContext.Current.CancellationToken));
    Assert.Contains("d3js.org v7.9.0", await File.ReadAllTextAsync(QueryFile("queries-powerquery.html"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task AC3_a_format_list_replaces_the_default()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "csv");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(new[] { "queries-powerquery.csv" }, OutputFiles());
  }

  [Fact]
  public async Task AC4_existing_outputs_in_a_non_interactive_session_fail_with_exit_code_4_and_change_nothing()
  {
    var page = QueryFile("queries-powerquery.html");
    await File.WriteAllTextAsync(page, "original", TestContext.Current.CancellationToken);
    var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(page, timestamp);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "csv,html");

    Assert.Equal(ExitCodes.OutputRefused, exitCode);
    Assert.Equal("original", await File.ReadAllTextAsync(page, TestContext.Current.CancellationToken));
    Assert.Equal(timestamp, File.GetLastWriteTimeUtc(page));
    Assert.Equal(new[] { "queries-powerquery.html" }, OutputFiles());
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

    await app.RunAsync(Queries, "-f", "csv");

    var rows = await File.ReadAllLinesAsync(QueryFile("queries-powerquery.csv"), TestContext.Current.CancellationToken);
    Assert.Contains("\"Source\",\"Orders\"", rows);
    Assert.DoesNotContain("\"Customers\",\"Orders\"", rows);
    Assert.DoesNotContain("\"Orders\",\"Revenue & Margin (2024)\"", rows);
    Assert.DoesNotContain("\"Source\",\"Sales Region Map\"", rows);
  }

  [Fact]
  public async Task AC8_a_circular_dependency_warns_and_all_exports_still_complete()
  {
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "-f", "csv,puml,html");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("Cycle A -> Cycle B -> Cycle A", StringComparison.Ordinal));
    Assert.Equal(3, OutputFiles().Length);
    Assert.Contains("<<cycle>>", await File.ReadAllTextAsync(QueryFile("queries-powerquery.puml"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
  }

  [Fact]
  public async Task AC9_a_png_render_failure_does_not_stop_the_other_exports_and_exits_with_5()
  {
    var handler = StubHttpMessageHandler.FailingFor("png", HttpStatusCode.InternalServerError);
    using var app = App(handler);

    var exitCode = await app.RunAsync(Queries, "-f", "csv,png,svg,html", "--plantuml-server", Server);

    Assert.Equal(ExitCodes.ExportFailed, exitCode);
    Assert.Equal(
      new[] { "queries-powerquery.csv", "queries-powerquery.html", "queries-powerquery.svg" },
      OutputFiles());
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("png", StringComparison.Ordinal) && m.Contains("500", StringComparison.Ordinal));
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Rendered_formats_are_written_without_a_puml_file_unless_requested()
  {
    var handler = StubHttpMessageHandler.WorkingServer();
    using var app = App(handler);

    var exitCode = await app.RunAsync(Queries, "-f", "svg,png,pdf", "--plantuml-server", Server);

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(
      new[] { "queries-powerquery.pdf", "queries-powerquery.png", "queries-powerquery.svg" },
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

    var exitCode = await app.RunAsync(Queries, "-n", "sales-model", "-o", output);

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(new[] { "sales-model.html" }, OutputFiles(output));
    Assert.Empty(OutputFiles());
    // The heading names what's documented, whatever the files are called.
    Assert.Contains(">queries</h1>", await File.ReadAllTextAsync(Path.Combine(output, "sales-model.html"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Relative_paths_are_resolved_against_the_current_directory()
  {
    using var app = App();

    var exitCode = await app.RunAsync("queries", "-o", "out");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.True(File.Exists(temp.Combine("out", "queries-powerquery.html")));
  }

  [Fact]
  public async Task Force_overwrites_existing_outputs()
  {
    var csv = QueryFile("queries-powerquery.csv");
    await File.WriteAllTextAsync(csv, "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--force", "-f", "csv");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.StartsWith("Query Name,", (await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken)).TrimStart('﻿'));
  }

  [Fact]
  public async Task Skip_leaves_existing_outputs_and_writes_the_rest()
  {
    var csv = QueryFile("queries-powerquery.csv");
    await File.WriteAllTextAsync(csv, "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--on-exists", "skip", "-f", "csv,puml");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal("original", await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken));
    Assert.True(File.Exists(QueryFile("queries-powerquery.puml")));
  }

  [Fact]
  public async Task When_everything_is_skipped_no_success_message_is_shown()
  {
    await File.WriteAllTextAsync(QueryFile("queries-powerquery.html"), "original", TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(Queries, "--on-exists", "skip");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.DoesNotContain(app.Log.Messages(LogEventLevel.Information), m => m.StartsWith("Done.", StringComparison.Ordinal));
    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("No output files were written", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Interactive_prompt_answers_are_applied_per_file()
  {
    await File.WriteAllTextAsync(QueryFile("queries-powerquery.csv"), "keep me", TestContext.Current.CancellationToken);
    await File.WriteAllTextAsync(QueryFile("queries-powerquery.puml"), "replace me", TestContext.Current.CancellationToken);
    var interaction = new FakeUserInteraction(true, OverwriteAnswer.No, OverwriteAnswer.Yes);
    using var app = App(interaction: interaction);

    var exitCode = await app.RunAsync(Queries, "-f", "csv,puml");

    Assert.Equal(ExitCodes.Success, exitCode);
    Assert.Equal(2, interaction.Asked.Count);
    Assert.Equal("keep me", await File.ReadAllTextAsync(QueryFile("queries-powerquery.csv"), TestContext.Current.CancellationToken));
    Assert.StartsWith("@startuml", await File.ReadAllTextAsync(QueryFile("queries-powerquery.puml"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Quitting_at_the_prompt_writes_nothing_and_exits_with_4()
  {
    await File.WriteAllTextAsync(QueryFile("queries-powerquery.puml"), "original", TestContext.Current.CancellationToken);
    using var app = App(interaction: new FakeUserInteraction(true, OverwriteAnswer.Quit));

    var exitCode = await app.RunAsync(Queries, "-f", "puml");

    Assert.Equal(ExitCodes.OutputRefused, exitCode);
    Assert.Equal(new[] { "queries-powerquery.puml" }, OutputFiles());
  }

  [Fact]
  public async Task AC10_a_workbook_is_read_directly_and_its_outputs_go_in_a_folder_beside_it()
  {
    var workbook = temp.Combine("Queries.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), workbook);
    using var app = App();

    var exitCode = await app.RunAsync(workbook, "-f", "csv,html");

    Assert.Equal(ExitCodes.Success, exitCode);
    var output = temp.Combine("PowerQuery");
    Assert.Equal(new[] { "Queries-powerquery.csv", "Queries-powerquery.html" }, OutputFiles(output));
    var rows = await File.ReadAllLinesAsync(Path.Combine(output, "Queries-powerquery.csv"), TestContext.Current.CancellationToken);
    Assert.Contains(">Queries.xlsx</h1>", await File.ReadAllTextAsync(Path.Combine(output, "Queries-powerquery.html"), TestContext.Current.CancellationToken));
    Assert.Contains("\"Sales Data\",\"Chain_A\"", rows);
    Assert.Contains("\"fnClean\",\"Manual Targets\"", rows);
    Assert.Contains("\"Sales/Returns\",", rows);
    Assert.Equal(8, rows.Count(r => !r.EndsWith(',')) - 1); // every dependency, less the header
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.Contains("14 queries", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AC11_query_files_written_from_a_workbook_read_back_as_a_folder_with_the_same_names_and_dependencies()
  {
    var workbook = temp.Combine("Queries.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), workbook);
    var folder = temp.Combine("PowerQuery");
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook, "-f", "csv"));
    Assert.Contains("Sales%2FReturns.m", QueryFiles(folder));
    Assert.Contains("Discount%25.m", QueryFiles(folder));
    Assert.Equal(14, QueryFiles(folder).Length);
    Assert.Equal(ExitCodes.Success, await app.RunAsync(folder, "-f", "csv"));

    var fromWorkbook = await File.ReadAllLinesAsync(Path.Combine(folder, "Queries-powerquery.csv"), TestContext.Current.CancellationToken);
    var fromFolder = await File.ReadAllLinesAsync(Path.Combine(folder, "PowerQuery-powerquery.csv"), TestContext.Current.CancellationToken);
    Assert.Equal(fromWorkbook, fromFolder);
    Assert.Contains("\"Sales/Returns\",", fromFolder);
  }

  /// <summary>Escapes text as the page does for embedded source.</summary>
  private static string Escaped(string text) =>
    text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
      .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

  private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

  [Fact]
  public async Task FR_SRC_2_by_default_the_page_names_each_querys_file_but_holds_no_M_code()
  {
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(Queries));

    var html = await File.ReadAllTextAsync(QueryFile("queries-powerquery.html"), TestContext.Current.CancellationToken);
    var bytes = await File.ReadAllBytesAsync(QueryFile("Orders.m"), TestContext.Current.CancellationToken);
    Assert.Contains("\"sourceMode\":\"folder\"", html);
    Assert.Contains($"\"file\":\"Orders.m\",\"hash\":\"{Hash(bytes)}\"", html);
    Assert.DoesNotContain("See the Customers query for details", html);
  }

  [Fact]
  public async Task FR_SRC_2_embed_source_puts_the_folders_M_code_in_the_page_whatever_its_encoding()
  {
    byte[] wide = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("\"Zürich\"")];
    await File.WriteAllBytesAsync(QueryFile("Wide.m"), wide, TestContext.Current.CancellationToken);
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(Queries, "--embed-source"));

    var html = await File.ReadAllTextAsync(QueryFile("queries-powerquery.html"), TestContext.Current.CancellationToken);
    var orders = await File.ReadAllTextAsync(QueryFile("Orders.m"), TestContext.Current.CancellationToken);
    Assert.Contains("\"sourceMode\":\"embedded\"", html);
    Assert.Contains("data-query=\"Orders\">\n" + Escaped(orders) + "</pre>", html);
    Assert.Contains("data-query=\"Wide\">\n&quot;Zürich&quot;</pre>", html);
    Assert.Contains($"\"hash\":\"{Hash(wide)}\"", html);
  }

  [Fact]
  public async Task FR_SRC_2_M_code_embedded_from_a_workbook_matches_the_query_files_beside_the_page()
  {
    var workbook = temp.Combine("Queries.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), workbook);
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook, "--embed-source"));

    var folder = temp.Combine("PowerQuery");
    var html = await File.ReadAllTextAsync(Path.Combine(folder, "Queries-powerquery.html"), TestContext.Current.CancellationToken);
    var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, "Sales Data.m"), TestContext.Current.CancellationToken);
    Assert.Contains($"\"file\":\"Sales Data.m\",\"hash\":\"{Hash(bytes)}\"", html);
    Assert.Contains("data-query=\"Sales Data\">\n" + Escaped(Encoding.UTF8.GetString(bytes)) + "</pre>", html);
  }

  [Fact]
  public async Task Query_files_are_kept_in_step_with_the_workbook_without_asking()
  {
    var workbook = temp.Combine("Book.xlsx");
    await File.WriteAllBytesAsync(workbook, MashupWorkbook.With("section Section1;\r\nshared A = 1;\r\nshared B = A + 1;"), TestContext.Current.CancellationToken);
    temp.WriteFile(Path.Combine("PowerQuery", "A.m"), "old");
    temp.WriteFile(Path.Combine("PowerQuery", "Gone.m"), "no longer in the workbook");
    using var app = App(); // not interactive, so an existing page would be refused, but query files aren't asked about

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook));

    Assert.Equal(new[] { "A.m", "B.m" }, QueryFiles(temp.Combine("PowerQuery")));
    Assert.Equal("1", await File.ReadAllTextAsync(temp.Combine("PowerQuery", "A.m"), TestContext.Current.CancellationToken));
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.Contains("Gone.m", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Query_groups_that_cannot_be_read_give_a_warning_and_the_run_carries_on()
  {
    var workbook = temp.Combine("Book.xlsx");
    var metadata = new byte[] { 0, 0, 0, 0, 3, 0, 0, 0, 0x3C, 0x61, 0x3E }; // "<a>", which isn't the metadata's XML
    await File.WriteAllBytesAsync(
      workbook,
      MashupWorkbook.FromMashup(MashupWorkbook.Sections(0, MashupWorkbook.PackageParts("section Section1;\r\nshared A = 1;"), [], metadata, [])),
      TestContext.Current.CancellationToken);
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook));

    Assert.Contains(app.Log.Messages(LogEventLevel.Warning), m => m.Contains("Could not read the workbook's query groups", StringComparison.Ordinal));
    Assert.True(File.Exists(temp.Combine(Path.Combine("PowerQuery", "Book-powerquery.html"))));
  }

  [Fact]
  public async Task A_workbook_whose_queries_cannot_be_read_leaves_existing_query_files_alone()
  {
    var workbook = temp.Combine("Book.xlsx");
    await File.WriteAllBytesAsync(workbook, MashupWorkbook.With("section Section1;\r\nshared A = 1"), TestContext.Current.CancellationToken);
    temp.WriteFile(Path.Combine("PowerQuery", "Kept.m"), "1");
    using var app = App();

    Assert.Equal(ExitCodes.InputError, await app.RunAsync(workbook));

    Assert.Equal(new[] { "Kept.m" }, temp.FileNames("PowerQuery"));
  }

  [Fact]
  public async Task Older_outputs_from_pqdoc_are_deleted_beside_a_workbook_and_only_mentioned_in_a_folder()
  {
    const string olderPage = "<!doctype html>\n<html>\n<head>\n<meta name=\"generator\" content=\"pqdoc 1.2.0 (271f0f3)\">\n</head></html>";
    var workbook = temp.Combine("Queries.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), workbook);
    temp.WriteFile(Path.Combine("PowerQuery", "powerquery-dependencies.html"), olderPage); // from before outputs were renamed
    temp.WriteFile(Path.Combine("PowerQuery", "powerquery-dependencies.csv"), "Query Name,Referenced By Query Name");
    temp.WriteFile(Path.Combine("PowerQuery", "notes.html"), "<p>written by hand</p>");
    temp.WriteFile(Path.Combine("PowerQuery", "Queries-vba.html"), "<meta name=\"generator\" content=\"vbadoc 0.8.0 (abc1234)\">");
    temp.WriteFile(Path.Combine(Queries, "powerquery-dependencies.html"), olderPage);
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook));
    Assert.Equal(new[] { "Queries-powerquery.html", "Queries-vba.html", "notes.html" }, OutputFiles(temp.Combine("PowerQuery")));
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.Contains("Deleted", StringComparison.Ordinal) && m.Contains("powerquery-dependencies.csv", StringComparison.Ordinal));

    Assert.Equal(ExitCodes.Success, await app.RunAsync(Queries));
    Assert.Equal(new[] { "powerquery-dependencies.html", "queries-powerquery.html" }, OutputFiles());
    Assert.Contains(app.Log.Messages(LogEventLevel.Information), m => m.Contains("Older outputs from pqdoc are still in", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Outputs_are_named_after_the_input_with_characters_that_confuse_a_browser_replaced()
  {
    var workbook = temp.Combine("Sales #3 at 50%.xlsx");
    File.Copy(Fixtures.Workbook("Queries.xlsx"), workbook);
    using var app = App();

    Assert.Equal(ExitCodes.Success, await app.RunAsync(workbook));

    var page = Assert.Single(OutputFiles(temp.Combine("PowerQuery")));
    Assert.Equal("Sales _3 at 50_-powerquery.html", page);
    Assert.Contains(">Sales #3 at 50%.xlsx</h1>", await File.ReadAllTextAsync(temp.Combine("PowerQuery", page), TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task A_file_that_is_not_a_workbook_exits_with_3()
  {
    temp.WriteFile("notes.txt", "not a workbook");
    using var app = App();

    var exitCode = await app.RunAsync(temp.Combine("notes.txt"));

    Assert.Equal(ExitCodes.InputError, exitCode);
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("not an Excel workbook", StringComparison.Ordinal));
    Assert.False(Directory.Exists(temp.Combine("PowerQuery")));
  }

  [Fact]
  public async Task A_workbook_without_queries_exits_with_3()
  {
    await File.WriteAllBytesAsync(temp.Combine("Empty.xlsx"), MashupWorkbook.Zip(new() { ["xl/workbook.xml"] = "<workbook/>"u8.ToArray() }), TestContext.Current.CancellationToken);
    using var app = App();

    var exitCode = await app.RunAsync(temp.Combine("Empty.xlsx"));

    Assert.Equal(ExitCodes.InputError, exitCode);
    Assert.Contains(app.Log.Messages(LogEventLevel.Error), m => m.Contains("has no Power Query queries", StringComparison.Ordinal));
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
    Assert.True(File.Exists(QueryFile("queries-powerquery.html")));
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

    var exitCode = await app.RunAsync(Queries, "-f", "csv,puml,html");

    Assert.Equal(ExitCodes.ExportFailed, exitCode);
    Assert.Equal(new[] { "queries-powerquery.csv", "queries-powerquery.html" }, OutputFiles());
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

    var exitCode = await app.RunWithCancellationAsync(cancellation.Token, Queries, "-f", "csv");

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
