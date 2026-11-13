using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Importing;
using ExcelDocumenter.Desktop.Running;
using CodeDocumenter.Shared;
using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;

namespace ExcelDocumenter.Desktop.Tests.Running;

/// <summary>Runs the real tools through the app's runner, with no window: what the Run button does.</summary>
public sealed class ToolRunnerTests : IDisposable
{
  private readonly TempFolder folder = new();
  private readonly HttpClient httpClient = new();
  private readonly List<LogLine> lines = [];
  private readonly Answers interaction = new();

  public void Dispose()
  {
    httpClient.Dispose();
    folder.Dispose();
  }

  private async Task<RunResult> Run(ToolCommand command, CancellationToken? cancellationToken = null)
  {
    var result = await new ToolRunner(httpClient).RunAsync(
      command, interaction, line => { lock (lines) { lines.Add(line); } }, cancellationToken ?? TestContext.Current.CancellationToken);
    return result;
  }

  private DocumentSettings Settings(string fixture) => new() { Input = folder.Fixture(fixture) };

  [Fact]
  public async Task Vbadoc_runs_in_the_app_and_says_where_its_page_is()
  {
    var settings = Settings("SimpleMacro.xlsm");

    var result = await Run(DocumentCommands.Vbadoc(settings));

    Assert.Equal(ExitCodes.Success, result.ExitCode);
    Assert.Equal(folder.Combine("VBAProject", "SimpleMacro-vba.html"), result.Page);
    Assert.True(File.Exists(result.Page));
    Assert.Contains(lines, l => l.Kind == LogKind.Information && l.Text.StartsWith("[INF] Reading ", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Pqdoc_runs_in_the_app_and_says_where_its_page_is()
  {
    var settings = Settings("Queries.xlsx");

    var result = await Run(DocumentCommands.Pqdoc(settings));

    Assert.Equal(ExitCodes.Success, result.ExitCode);
    Assert.Equal(folder.Combine("PowerQuery", "Queries-powerquery.html"), result.Page);
    Assert.True(File.Exists(result.Page));
  }

  [Fact]
  public async Task Verbose_logs_as_the_console_does_with_the_time_and_the_detail()
  {
    var settings = Settings("SimpleMacro.xlsm");
    settings.LogDetail = LogDetail.Verbose;

    await Run(DocumentCommands.Vbadoc(settings));

    Assert.Contains(lines, l => l.Kind == LogKind.Debug && l.Text.Contains(" [DBG] ", StringComparison.Ordinal));
  }

  [Fact]
  public async Task A_tools_usage_text_comes_into_the_log_and_the_run_ends_with_its_exit_code()
  {
    var result = await Run(new ToolCommand(Tool.Vbadoc, [folder.Combine("Book.xlsm"), "--nope"]));

    Assert.Equal(ExitCodes.InvalidArguments, result.ExitCode);
    Assert.Null(result.Page);
    Assert.Contains(lines, l => l.Kind == LogKind.Error && l.Text == "[ERR] Unknown option '--nope'.");
    Assert.Contains(lines, l => l.Kind == LogKind.Plain && l.Text == "Usage: vbadoc <workbook> [options]");
  }

  [Fact]
  public async Task The_tool_asks_the_apps_question_rather_than_stopping()
  {
    var settings = Settings("SimpleMacro.xlsm");
    var page = folder.Combine("VBAProject", "SimpleMacro-vba.html");
    Assert.Equal(ExitCodes.Success, (await Run(DocumentCommands.Vbadoc(settings))).ExitCode);
    File.WriteAllText(page, "an older page");
    interaction.Answer = OverwriteAnswer.No;

    var result = await Run(DocumentCommands.Vbadoc(settings));

    Assert.Equal(ExitCodes.Success, result.ExitCode);
    Assert.Equal(new[] { page }, interaction.Asked);
    Assert.Equal("an older page", File.ReadAllText(page));
    Assert.Null(result.Page);
  }

  [Fact]
  public async Task Cancelling_ends_the_run_as_Ctrl_C_does()
  {
    using var cancellation = new CancellationTokenSource();
    await cancellation.CancelAsync();

    var result = await Run(DocumentCommands.Pqdoc(Settings("Queries.xlsx")), cancellation.Token);

    Assert.Equal(ExitCodes.Cancelled, result.ExitCode);
  }

  [Fact]
  public async Task Both_tools_run_in_one_process_and_each_page_is_stamped_by_its_own_tool()
  {
    var vbadoc = await Run(DocumentCommands.Vbadoc(Settings("SimpleMacro.xlsm")));
    var pqdoc = await Run(DocumentCommands.Pqdoc(Settings("Queries.xlsx")));

    Assert.Contains("<meta name=\"generator\" content=\"vbadoc ", File.ReadAllText(vbadoc.Page!), StringComparison.Ordinal);
    Assert.Contains("<meta name=\"generator\" content=\"pqdoc ", File.ReadAllText(pqdoc.Page!), StringComparison.Ordinal);
  }

  [Fact]
  public async Task Preview_says_what_a_choice_comes_to_and_writes_nothing_and_Run_writes_the_bundle()
  {
    var source = folder.Fixture("SimpleMacro.xlsm");
    var module = VbaDocumenter.Cli.ImportSource.Items(source).First(item => item.CanTake).Name;
    var settings = new ImportSettings { Source = source, Items = [module], Bundle = folder.Combine("Shared") };

    var preview = await Run(ImportCommands.Preview(settings)!);

    Assert.Equal(ExitCodes.Success, preview.ExitCode);
    Assert.Contains(lines, l => l.Text == $"[INF] Chosen: {module}");
    Assert.Contains(lines, l => l.Text.StartsWith("[INF] Nothing was written.", StringComparison.Ordinal));
    Assert.False(Directory.Exists(settings.Bundle));

    Assert.Equal(ExitCodes.Success, (await Run(ImportCommands.Run(settings)!)).ExitCode);
    Assert.NotEmpty(Directory.GetFiles(settings.Bundle, "*.bas"));
  }

  [Fact]
  public async Task Pqdoc_writes_a_new_workbook_from_the_app()
  {
    var settings = new ImportSettings
    {
      Source = folder.Fixture("Queries.xlsx"),
      Tool = Tool.Pqdoc,
      Items = ["Chain_A"],
      WriteTo = ImportDestination.NewWorkbook,
      NewWorkbook = folder.Combine("New.xlsx"),
    };

    Assert.Equal(ExitCodes.Success, (await Run(ImportCommands.Run(settings)!)).ExitCode);
    Assert.True(File.Exists(settings.NewWorkbook));
  }

  [Fact]
  public async Task Changing_the_target_itself_asks_first_and_a_no_changes_nothing()
  {
    var source = folder.Fixture("Queries.xlsx");
    var target = folder.Combine("Target.xlsx");
    var make = new ImportSettings { Source = source, Tool = Tool.Pqdoc, Items = ["fnClean"], WriteTo = ImportDestination.NewWorkbook, NewWorkbook = target };
    Assert.Equal(ExitCodes.Success, (await Run(ImportCommands.Run(make)!)).ExitCode);
    var before = File.ReadAllBytes(target);
    var change = new ImportSettings { Source = source, Tool = Tool.Pqdoc, Items = ["Chain_A"], WriteTo = ImportDestination.InPlace, Target = target };

    var result = await Run(ImportCommands.Run(change)!);

    Assert.Equal(ExitCodes.OutputRefused, result.ExitCode);
    Assert.StartsWith("This changes Target.xlsx itself", Assert.Single(interaction.Confirmed), StringComparison.Ordinal);
    Assert.Equal(before, File.ReadAllBytes(target));
  }

  /// <summary>Answers the tools' questions as told, and remembers what they asked about.</summary>
  private sealed class Answers : IUserInteraction
  {
    public OverwriteAnswer Answer { get; set; } = OverwriteAnswer.Quit;

    public List<string> Asked { get; } = [];

    public List<string> Confirmed { get; } = [];

    public bool IsInteractive => true;

    public OverwriteAnswer AskOverwrite(string path)
    {
      Asked.Add(path);
      return Answer;
    }

    public ClashAnswer AskClash(ImportClash clash) => throw new NotSupportedException();

    public bool Confirm(string question)
    {
      Confirmed.Add(question);
      return false;
    }
  }
}
