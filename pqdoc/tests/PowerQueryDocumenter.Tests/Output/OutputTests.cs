using PowerQueryDocumenter.Core.Configuration;
using PowerQueryDocumenter.Core.Output;
using PowerQueryDocumenter.Tests.TestSupport;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.Output;

public sealed class OverwriteResolverTests
{
  private static readonly PlannedOutput Csv = new(OutputFormat.Csv, "/out/model.csv");
  private static readonly PlannedOutput Puml = new(OutputFormat.Puml, "/out/model.puml");
  private static readonly PlannedOutput Html = new(OutputFormat.Html, "/out/model.html");
  private static readonly PlannedOutput[] All = [Csv, Puml, Html];

  private readonly TestLog log = new();

  private OverwriteResolution Resolve(
    OnExistsBehaviour behaviour,
    FakeUserInteraction interaction,
    params PlannedOutput[] existing) =>
    new OverwriteResolver(log.CreateLogger(), interaction).Resolve(All, behaviour, path => existing.Any(e => e.Path == path));

  [Theory]
  [InlineData(OnExistsBehaviour.Prompt)]
  [InlineData(OnExistsBehaviour.Overwrite)]
  [InlineData(OnExistsBehaviour.Skip)]
  [InlineData(OnExistsBehaviour.Fail)]
  public void Nothing_existing_means_everything_is_written(OnExistsBehaviour behaviour)
  {
    var interaction = new FakeUserInteraction(isInteractive: true);

    var resolution = Resolve(behaviour, interaction);

    Assert.False(resolution.Aborted);
    Assert.Equal(All, resolution.ToWrite);
    Assert.Empty(interaction.Asked);
  }

  [Fact]
  public void Overwrite_replaces_existing_files_and_logs_it()
  {
    var resolution = Resolve(OnExistsBehaviour.Overwrite, new FakeUserInteraction(false), Puml);

    Assert.Equal(All, resolution.ToWrite);
    Assert.Contains(log.Messages(LogEventLevel.Information), m => m.Contains("model.puml", StringComparison.Ordinal));
  }

  [Fact]
  public void Skip_leaves_existing_files_and_warns()
  {
    var resolution = Resolve(OnExistsBehaviour.Skip, new FakeUserInteraction(false), Puml);

    Assert.False(resolution.Aborted);
    Assert.Equal(new[] { Csv, Html }, resolution.ToWrite);
    Assert.Equal(new[] { Puml }, resolution.Skipped);
    Assert.Contains(log.Messages(LogEventLevel.Warning), m => m.Contains("model.puml", StringComparison.Ordinal));
  }

  [Fact]
  public void Fail_aborts_before_anything_is_written()
  {
    var resolution = Resolve(OnExistsBehaviour.Fail, new FakeUserInteraction(true), Html);

    Assert.True(resolution.Aborted);
    Assert.Empty(resolution.ToWrite);
    Assert.Contains(log.Messages(LogEventLevel.Error), m => m.Contains("model.html", StringComparison.Ordinal));
  }

  [Fact]
  public void Prompt_in_a_non_interactive_session_behaves_as_fail_and_explains_how_to_proceed()
  {
    var interaction = new FakeUserInteraction(isInteractive: false);

    var resolution = Resolve(OnExistsBehaviour.Prompt, interaction, Csv);

    Assert.True(resolution.Aborted);
    Assert.Empty(interaction.Asked);
    var errors = string.Join("\n", log.Messages(LogEventLevel.Error));
    Assert.Contains("not interactive", errors, StringComparison.Ordinal);
    Assert.Contains("--force", errors, StringComparison.Ordinal);
    Assert.Contains("--on-exists", errors, StringComparison.Ordinal);
  }

  [Fact]
  public void Prompt_asks_once_per_existing_file()
  {
    var interaction = new FakeUserInteraction(true, OverwriteAnswer.Yes, OverwriteAnswer.No);

    var resolution = Resolve(OnExistsBehaviour.Prompt, interaction, Csv, Html);

    Assert.Equal(new[] { Csv.Path, Html.Path }, interaction.Asked);
    Assert.Equal(new[] { Csv, Puml }, resolution.ToWrite);
    Assert.Equal(new[] { Html }, resolution.Skipped);
  }

  [Fact]
  public void Answering_all_stops_further_prompts()
  {
    var interaction = new FakeUserInteraction(true, OverwriteAnswer.All);

    var resolution = Resolve(OnExistsBehaviour.Prompt, interaction, Csv, Puml, Html);

    Assert.Equal(new[] { Csv.Path }, interaction.Asked);
    Assert.Equal(All, resolution.ToWrite);
  }

  [Fact]
  public void Answering_quit_aborts_without_writing_anything()
  {
    var interaction = new FakeUserInteraction(true, OverwriteAnswer.Yes, OverwriteAnswer.Quit);

    var resolution = Resolve(OnExistsBehaviour.Prompt, interaction, Csv, Html);

    Assert.True(resolution.Aborted);
    Assert.Empty(resolution.ToWrite);
  }
}
