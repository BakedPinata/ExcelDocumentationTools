using CodeDocumenter.Desktop.Asking;
using CodeDocumenter.Shared.CommandLine;
using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Desktop.Tests.Asking;

/// <summary>The tools' questions, answered by a stand-in for the dialogs.</summary>
public sealed class DialogInteractionTests
{
  private const string Page = @"C:\Work\VBAProject\Sales-vba.html";

  private static readonly ImportClash Replaceable = new("Utilities", "Utilities differs from the one in Sales.xlsm.", "Replaces the target's version.", "Keeps the target's version.");
  private static readonly ImportClash Fixed = new("Sheet1", "Sheet1 is one of the target's sheets.", null, "Keeps the target's version.");

  private readonly StandIn standIn = new();

  private DialogInteraction Interaction() => new(standIn);

  [Theory]
  [InlineData("Overwrite", OverwriteAnswer.Yes)]
  [InlineData("Keep it", OverwriteAnswer.No)]
  [InlineData("Overwrite all", OverwriteAnswer.All)]
  [InlineData("Quit", OverwriteAnswer.Quit)]
  public void Each_answer_to_the_overwrite_question_means_what_the_consoles_does(string label, OverwriteAnswer answer)
  {
    standIn.Answer = label;

    Assert.Equal(answer, Interaction().AskOverwrite(Page));

    var question = Assert.Single(standIn.Asked);
    Assert.Equal("Sales-vba.html is already there", question.Heading);
    Assert.Equal("Keep it", question.Answers[question.DefaultAnswer]);
  }

  [Theory]
  [InlineData("Skip", ClashAnswer.Skip)]
  [InlineData("Overwrite", ClashAnswer.Overwrite)]
  [InlineData("Skip all", ClashAnswer.SkipAll)]
  [InlineData("Overwrite all", ClashAnswer.OverwriteAll)]
  [InlineData("Quit", ClashAnswer.Quit)]
  public void Each_answer_to_a_clash_means_what_the_consoles_does(string label, ClashAnswer answer)
  {
    standIn.Answer = label;

    Assert.Equal(answer, Interaction().AskClash(Replaceable));

    var question = Assert.Single(standIn.Asked);
    Assert.Equal(new[] { "Skip", "Overwrite", "Skip all", "Overwrite all", "Quit" }, question.Answers);
    Assert.Equal("Skip", question.Answers[question.DefaultAnswer]);
  }

  [Fact]
  public void A_clash_says_whats_in_the_way_and_what_Skip_and_Overwrite_mean()
  {
    standIn.Answer = "Skip";

    Interaction().AskClash(Replaceable);

    var question = Assert.Single(standIn.Asked);
    Assert.Equal("Utilities is already in the target", question.Heading);
    Assert.Contains("Utilities differs from the one in Sales.xlsm.", question.Text, StringComparison.Ordinal);
    Assert.Contains("Overwrite: Replaces the target's version.", question.Text, StringComparison.Ordinal);
    Assert.Contains("Skip: Keeps the target's version.", question.Text, StringComparison.Ordinal);
  }

  [Fact]
  public void What_cannot_be_replaced_is_not_offered_Overwrite_as_at_the_console()
  {
    standIn.Answer = "Skip all";

    Assert.Equal(ClashAnswer.SkipAll, Interaction().AskClash(Fixed));

    var question = Assert.Single(standIn.Asked);
    Assert.Equal(new[] { "Skip", "Skip all", "Quit" }, question.Answers);
    Assert.DoesNotContain("Overwrite", question.Text, StringComparison.Ordinal);
    Assert.All(question.Answers, label => Assert.NotNull(ConsoleUserInteraction.ReadClashAnswer(label, canOverwrite: false)));
  }

  [Fact]
  public void The_dialogs_labels_are_answers_the_console_understands_too()
  {
    standIn.Answer = "Skip";
    Interaction().AskClash(Replaceable);

    Assert.All(standIn.Asked[0].Answers, label =>
      Assert.Equal(ClashQuestion.Answers(Replaceable).Single(a => a.Label == label).Answer, ConsoleUserInteraction.ReadClashAnswer(label, canOverwrite: true)));
  }

  [Fact]
  public void Closing_a_dialog_is_Quit_as_end_of_input_is_at_the_console_and_stops_the_rest_of_the_run()
  {
    var interaction = Interaction();
    standIn.Answer = null;

    Assert.Equal(OverwriteAnswer.Quit, interaction.AskOverwrite(Page));
    Assert.True(interaction.QuitAnswered);

    interaction.Reset();
    Assert.False(interaction.QuitAnswered);
    Assert.Equal(ClashAnswer.Quit, interaction.AskClash(Replaceable));
    Assert.True(interaction.QuitAnswered);
  }

  [Theory]
  [InlineData("Yes", true)]
  [InlineData("No", false)]
  [InlineData(null, false)]
  public void Going_ahead_with_what_cannot_be_undone_needs_a_yes(string? label, bool goAhead)
  {
    standIn.Answer = label;

    Assert.Equal(goAhead, Interaction().Confirm("This changes Target.xlsm itself. Are you sure?"));

    var question = Assert.Single(standIn.Asked);
    Assert.Equal("This changes Target.xlsm itself. Are you sure?", question.Text);
    Assert.Equal("No", question.Answers[question.DefaultAnswer]);
  }

  [Fact]
  public void Someone_is_always_there_to_answer()
  {
    Assert.True(Interaction().IsInteractive);
  }

  /// <summary>Answers by label, or closes the dialog when the answer is null, and keeps what it was asked.</summary>
  private sealed class StandIn : IAsk
  {
    public string? Answer { get; set; }

    public List<Question> Asked { get; } = [];

    public int? Ask(Question question)
    {
      Asked.Add(question);
      return Answer is null ? null : question.Answers.ToList().IndexOf(Answer);
    }
  }
}
