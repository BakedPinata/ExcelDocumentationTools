using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// The tools' questions, asked in dialogs over the window. A tool asks from its own thread, so each question is passed
/// to the window's thread and the tool waits for the answer, as it would for a typed one.
/// </summary>
internal sealed class DialogInteraction(Control owner) : IUserInteraction
{
  /// <summary>Someone is always there to answer, so the tools ask rather than stop with exit code 4.</summary>
  public bool IsInteractive => true;

  /// <summary>Whether Quit was answered since <see cref="Reset"/>, so the rest of a run can stop too.</summary>
  public bool QuitAnswered { get; private set; }

  /// <summary>Starts a new run.</summary>
  public void Reset() => QuitAnswered = false;

  public OverwriteAnswer AskOverwrite(string path)
  {
    var answer = owner.Invoke(() =>
    {
      var buttons = OverwriteQuestion.Answers.Select(a => (Button: new TaskDialogButton(a.Label), a.Answer)).ToList();
      var page = new TaskDialogPage
      {
        Caption = "xldocui",
        Heading = OverwriteQuestion.Heading(path),
        Text = OverwriteQuestion.Text(path),
        Icon = TaskDialogIcon.Warning,
        AllowCancel = true,
        DefaultButton = buttons.First(b => b.Answer == OverwriteAnswer.No).Button,
      };
      foreach (var (button, _) in buttons)
      {
        page.Buttons.Add(button);
      }

      var chosen = TaskDialog.ShowDialog(owner, page);
      return buttons.FirstOrDefault(b => b.Button == chosen) is { Button: not null } match ? match.Answer : OverwriteQuestion.Closed;
    });
    QuitAnswered |= answer == OverwriteAnswer.Quit;
    return answer;
  }

  // Only import asks this, and its dialog comes with the Import tab in step I4.
  public ClashAnswer AskClash(ImportClash clash) =>
    throw new NotSupportedException("Importing isn't in the app yet, so nothing should ask about a clash.");

  public bool Confirm(string question) => owner.Invoke(() =>
  {
    var page = new TaskDialogPage
    {
      Caption = "xldocui",
      Text = question,
      Icon = TaskDialogIcon.Warning,
      AllowCancel = true,
      Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
    };
    page.DefaultButton = TaskDialogButton.No;
    return TaskDialog.ShowDialog(owner, page) == TaskDialogButton.Yes;
  });
}
