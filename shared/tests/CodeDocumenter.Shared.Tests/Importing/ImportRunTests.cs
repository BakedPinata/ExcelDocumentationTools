using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;
using CodeDocumenter.Shared.Workbooks;
using Serilog.Events;

namespace CodeDocumenter.Shared.Tests.Importing;

/// <summary>Writing a workbook, new or in place of the target.</summary>
public sealed class ImportRunTests : IDisposable
{
  private static readonly byte[] Before = [1, 1, 1];
  private static readonly byte[] After = [2, 2, 2];

  private readonly TempDirectory folder = new();
  private readonly TestLog log = new();

  private string Target => Path.Combine(folder.Path, "Target.xlsm");

  public ImportRunTests() => File.WriteAllBytes(Target, Before);

  public void Dispose() => folder.Dispose();

  private ImportRun Run(FakeUserInteraction interaction) => new(log.CreateLogger(), interaction, "vbadoc");

  private static FakeUserInteraction Answering(bool answer)
  {
    var interaction = new FakeUserInteraction(isInteractive: true);
    interaction.Confirmations.Enqueue(answer);
    return interaction;
  }

  private static byte[] Built() => After;

  [Theory]
  [InlineData(".xlsm", "Target-imported.xlsm")]
  [InlineData(".xlsb", "Target-imported.xlsb")]
  public void A_copy_goes_beside_the_target(string extension, string expected)
  {
    Assert.Equal(Path.Combine(folder.Path, expected), ImportRun.CopyOf(Target, extension));
  }

  [Fact]
  public void A_new_file_is_written_without_asking()
  {
    var path = Path.Combine(folder.Path, "New.xlsm");

    Assert.Equal(ExitCodes.Success, Run(new FakeUserInteraction(isInteractive: false)).WriteWorkbook(path, OnExistsBehaviour.Prompt, Built));

    Assert.Equal(After, File.ReadAllBytes(path));
  }

  [Theory]
  [InlineData(OnExistsBehaviour.Fail, ExitCodes.OutputRefused)]
  [InlineData(OnExistsBehaviour.Prompt, ExitCodes.OutputRefused)]
  [InlineData(OnExistsBehaviour.Skip, ExitCodes.Success)]
  public void A_file_thats_there_stays_unless_overwriting_is_allowed(OnExistsBehaviour onExists, int exitCode)
  {
    var built = false;

    Assert.Equal(exitCode, Run(new FakeUserInteraction(isInteractive: false)).WriteWorkbook(Target, onExists, () => { built = true; return After; }));

    Assert.Equal(Before, File.ReadAllBytes(Target));
    Assert.False(built);
  }

  [Fact]
  public void On_exists_overwrite_replaces_a_file_thats_there()
  {
    Assert.Equal(ExitCodes.Success, Run(new FakeUserInteraction(isInteractive: false)).WriteWorkbook(Target, OnExistsBehaviour.Overwrite, Built));

    Assert.Equal(After, File.ReadAllBytes(Target));
  }

  [Fact]
  public void In_place_asks_saying_what_will_change_and_a_yes_goes_ahead()
  {
    var interaction = Answering(true);

    Assert.Equal(ExitCodes.Success, Run(interaction).ChangeInPlace(Target, force: false, "2 modules added", Built));

    Assert.Equal(After, File.ReadAllBytes(Target));
    Assert.Equal("This changes Target.xlsm itself: 2 modules added. It can't be undone. Are you sure?", Assert.Single(interaction.Questions));
  }

  [Fact]
  public void In_place_answered_no_changes_nothing()
  {
    Assert.Equal(ExitCodes.OutputRefused, Run(Answering(false)).ChangeInPlace(Target, force: false, "2 modules added", Built));

    Assert.Equal(Before, File.ReadAllBytes(Target));
    Assert.Contains("Stopped at your request. Nothing was changed", log.Messages(LogEventLevel.Error));
  }

  [Fact]
  public void In_place_with_force_doesnt_ask_so_it_can_run_unattended()
  {
    var nobody = new FakeUserInteraction(isInteractive: false);

    Assert.Equal(ExitCodes.Success, Run(nobody).ChangeInPlace(Target, force: true, "2 modules added", Built));

    Assert.Equal(After, File.ReadAllBytes(Target));
    Assert.Empty(nobody.Questions);
  }

  [Fact]
  public void In_place_with_nobody_to_ask_and_no_force_stops_and_says_how_to_go_on()
  {
    Assert.Equal(ExitCodes.OutputRefused, Run(new FakeUserInteraction(isInteractive: false)).ChangeInPlace(Target, force: false, "2 modules added", Built));

    Assert.Equal(Before, File.ReadAllBytes(Target));
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("--force"));
  }

  [Fact]
  public void A_failure_making_the_workbook_leaves_the_target_as_it_was()
  {
    var run = Run(new FakeUserInteraction(isInteractive: false));

    Assert.Throws<ExtractionException>(() => run.ChangeInPlace(Target, force: true, "a change", () => throw new ExtractionException("It didn't read back.")));

    Assert.Equal(Before, File.ReadAllBytes(Target));
    Assert.Equal(["Target.xlsm"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
  }

  [Fact]
  public void A_target_held_open_is_said_before_anything_is_asked_and_stays_as_it_was()
  {
    var interaction = new FakeUserInteraction(isInteractive: true);
    using (new FileStream(Target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
    {
      Assert.Equal(ExitCodes.ExportFailed, Run(interaction).ChangeInPlace(Target, force: false, "a change", Built));
    }

    Assert.Empty(interaction.Questions);
    Assert.Equal(Before, File.ReadAllBytes(Target));
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("Is it open in Excel?"));
    Assert.DoesNotContain(log.Messages(LogEventLevel.Error), m => m.Contains("add-in"));
  }

  [Fact]
  public void An_add_in_held_open_says_how_to_let_it_go()
  {
    var addin = Path.Combine(folder.Path, "Tools.xlam");
    File.WriteAllBytes(addin, Before);
    using (new FileStream(addin, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
    {
      Assert.Equal(ExitCodes.ExportFailed, Run(new FakeUserInteraction(isInteractive: false)).ChangeInPlace(addin, force: true, "a change", Built));
    }

    Assert.Equal(Before, File.ReadAllBytes(addin));
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("Is it open in Excel? A loaded add-in is: untick it in the Add-ins dialog, or close Excel."));
  }

  [Fact]
  public void Replacing_keeps_the_files_creation_time()
  {
    var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    File.SetCreationTimeUtc(Target, created);

    AtomicFileWriter.Replace(Target, After);

    Assert.Equal(After, File.ReadAllBytes(Target));
    Assert.Equal(["Target.xlsm"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    if (OperatingSystem.IsWindows())
    {
      Assert.Equal(created, File.GetCreationTimeUtc(Target));
    }
  }
}
