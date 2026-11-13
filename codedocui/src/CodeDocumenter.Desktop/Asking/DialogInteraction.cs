using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Desktop.Asking;

/// <summary>
/// The tools' questions, asked through <see cref="IAsk"/>: in the window, a dialog for each. The tools see someone to ask,
/// so they ask rather than stop with exit code 4, and each answer means what the console's does.
/// </summary>
internal sealed class DialogInteraction(IAsk ask) : IUserInteraction
{
  /// <summary>Someone is always there to answer.</summary>
  public bool IsInteractive => true;

  /// <summary>Whether Quit was answered since <see cref="Reset"/>, so the rest of a run can stop too.</summary>
  public bool QuitAnswered { get; private set; }

  /// <summary>Starts a new run.</summary>
  public void Reset() => QuitAnswered = false;

  public OverwriteAnswer AskOverwrite(string path)
  {
    var answers = OverwriteQuestion.Answers;
    var chosen = ask.Ask(new Question(
      OverwriteQuestion.Heading(path),
      OverwriteQuestion.Text(path),
      [.. answers.Select(a => a.Label)],
      IndexOf(answers, OverwriteAnswer.No)));
    var answer = chosen is { } index ? answers[index].Answer : OverwriteQuestion.Closed;
    QuitAnswered |= answer == OverwriteAnswer.Quit;
    return answer;
  }

  public ClashAnswer AskClash(ImportClash clash)
  {
    var answers = ClashQuestion.Answers(clash);
    var chosen = ask.Ask(new Question(
      ClashQuestion.Heading(clash),
      ClashQuestion.Text(clash),
      [.. answers.Select(a => a.Label)],
      IndexOf(answers, ClashQuestion.Default)));
    var answer = chosen is { } index ? answers[index].Answer : ClashQuestion.Closed;
    QuitAnswered |= answer == ClashAnswer.Quit;
    return answer;
  }

  /// <summary>Asks about what can't be undone, such as changing a target in place. No is the default, and closing is no.</summary>
  public bool Confirm(string question) => ask.Ask(new Question("Are you sure?", question, ["Yes", "No"], 1)) == 0;

  private static int IndexOf<T>(IReadOnlyList<(string Label, T Answer)> answers, T answer) =>
    answers.Select((a, i) => (a.Answer, i)).First(a => EqualityComparer<T>.Default.Equals(a.Answer, answer)).i;
}
