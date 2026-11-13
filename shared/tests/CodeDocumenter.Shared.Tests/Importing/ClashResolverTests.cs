using CodeDocumenter.Shared.CommandLine;
using CodeDocumenter.Shared.Importing;
using Serilog.Events;

namespace CodeDocumenter.Shared.Tests.Importing;

public sealed class ClashResolverTests
{
  private static readonly ImportClash Logger = new("Logger", "The target already has Logger.", "Replaces it.", "Keeps it.");
  private static readonly ImportClash Paths = new("Paths", "The target already has Paths.", "Replaces it.", "Keeps it.");
  private static readonly ImportClash Sheet = new("Sheet", "Sheet is a sheet's module in the target.", null, "Leaves it out.");

  private readonly TestLog log = new();

  private ClashResolution Resolve(FakeUserInteraction interaction, OnClashBehaviour behaviour, params ImportClash[] clashes) =>
    new ClashResolver(log.CreateLogger(), interaction).Resolve(clashes, behaviour);

  private static FakeUserInteraction Answering(params ClashAnswer[] answers)
  {
    var interaction = new FakeUserInteraction(isInteractive: true);
    foreach (var answer in answers)
    {
      interaction.ClashAnswers.Enqueue(answer);
    }

    return interaction;
  }

  [Fact]
  public void Each_clash_is_asked_about_in_turn()
  {
    var interaction = Answering(ClashAnswer.Skip, ClashAnswer.Overwrite);

    var resolution = Resolve(interaction, OnClashBehaviour.Prompt, Logger, Paths);

    Assert.Equal(["Paths"], resolution.Overwrite);
    Assert.Equal(["Logger"], resolution.Skip);
    Assert.False(resolution.Aborted);
    Assert.Equal([Logger, Paths], interaction.ClashesAsked);
  }

  [Fact]
  public void Skip_All_answers_for_every_one_after_it()
  {
    var interaction = Answering(ClashAnswer.SkipAll);

    var resolution = Resolve(interaction, OnClashBehaviour.Prompt, Logger, Paths, Sheet);

    Assert.Equal(["Logger", "Paths", "Sheet"], resolution.Skip);
    Assert.Single(interaction.ClashesAsked);
  }

  [Fact]
  public void Overwrite_All_answers_for_every_one_after_it_but_what_cant_be_replaced_is_still_asked_about()
  {
    var interaction = Answering(ClashAnswer.OverwriteAll, ClashAnswer.Skip);

    var resolution = Resolve(interaction, OnClashBehaviour.Prompt, Logger, Sheet, Paths);

    Assert.Equal(["Logger", "Paths"], resolution.Overwrite);
    Assert.Equal(["Sheet"], resolution.Skip);
    Assert.Equal([Logger, Sheet], interaction.ClashesAsked);
  }

  [Fact]
  public void Quit_stops_with_nothing_decided_even_after_earlier_answers()
  {
    var resolution = Resolve(Answering(ClashAnswer.Overwrite, ClashAnswer.Quit), OnClashBehaviour.Prompt, Logger, Paths);

    Assert.True(resolution.Aborted);
    Assert.Empty(resolution.Overwrite);
    Assert.Contains("Stopped at your request. Nothing was written", log.Messages(LogEventLevel.Error));
  }

  [Fact]
  public void Overwrite_given_for_what_cant_be_replaced_stops_rather_than_replacing_it()
  {
    Assert.True(Resolve(Answering(ClashAnswer.Overwrite), OnClashBehaviour.Prompt, Sheet).Aborted);
  }

  [Fact]
  public void With_nobody_to_ask_it_stops_naming_each_one_and_how_to_go_on()
  {
    var resolution = Resolve(new FakeUserInteraction(isInteractive: false), OnClashBehaviour.Prompt, Logger, Paths);

    Assert.True(resolution.Aborted);
    var errors = log.Messages(LogEventLevel.Error);
    Assert.Contains(Logger.Problem, errors);
    Assert.Contains(Paths.Problem, errors);
    Assert.Contains(errors, e => e.Contains("--on-clash overwrite"));
  }

  [Fact]
  public void On_clash_answers_without_asking()
  {
    var nobody = new FakeUserInteraction(isInteractive: false);

    Assert.Equal(["Logger", "Paths"], Resolve(nobody, OnClashBehaviour.Overwrite, Logger, Paths).Overwrite);
    Assert.Equal(["Logger", "Sheet"], Resolve(nobody, OnClashBehaviour.Skip, Logger, Sheet).Skip);
  }

  [Fact]
  public void On_clash_overwrite_stops_at_what_cant_be_replaced_rather_than_leaving_it_out_unasked()
  {
    var resolution = Resolve(new FakeUserInteraction(isInteractive: false), OnClashBehaviour.Overwrite, Logger, Sheet);

    Assert.True(resolution.Aborted);
    Assert.Contains(Sheet.Problem, log.Messages(LogEventLevel.Error));
  }

  [Fact]
  public void No_clashes_ask_nothing()
  {
    var resolution = Resolve(new FakeUserInteraction(isInteractive: false), OnClashBehaviour.Prompt);

    Assert.False(resolution.Aborted);
    Assert.Empty(resolution.Overwrite);
    Assert.Empty(resolution.Skip);
  }

  [Theory]
  [InlineData("s", ClashAnswer.Skip)]
  [InlineData("SKIP", ClashAnswer.Skip)]
  [InlineData("o", ClashAnswer.Overwrite)]
  [InlineData("overwrite", ClashAnswer.Overwrite)]
  [InlineData("sa", ClashAnswer.SkipAll)]
  [InlineData("skip  all", ClashAnswer.SkipAll)]
  [InlineData(" oa ", ClashAnswer.OverwriteAll)]
  [InlineData("Overwrite All", ClashAnswer.OverwriteAll)]
  [InlineData("q", ClashAnswer.Quit)]
  [InlineData("quit", ClashAnswer.Quit)]
  public void Typed_answers_are_read_ignoring_case_and_spacing(string typed, ClashAnswer expected)
  {
    Assert.Equal(expected, ConsoleUserInteraction.ReadClashAnswer(typed, canOverwrite: true));
  }

  [Theory]
  [InlineData("o", true)]
  [InlineData("oa", true)]
  [InlineData("maybe", false)]
  [InlineData("", false)]
  public void Overwrite_means_nothing_where_it_isnt_offered(string typed, bool whenOffered)
  {
    Assert.Null(ConsoleUserInteraction.ReadClashAnswer(typed, canOverwrite: false));
    Assert.Equal(whenOffered, ConsoleUserInteraction.ReadClashAnswer(typed, canOverwrite: true) is not null);
  }
}
