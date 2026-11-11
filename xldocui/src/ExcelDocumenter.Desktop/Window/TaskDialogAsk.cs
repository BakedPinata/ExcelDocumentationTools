using ExcelDocumenter.Desktop.Asking;

namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// Asks a question in a task dialog over the window. A tool asks from its own thread, so the question is passed to the
/// window's thread and the tool waits for the answer, as it would for a typed one.
/// </summary>
internal sealed class TaskDialogAsk(Control owner) : IAsk
{
  public int? Ask(Question question) => owner.Invoke(() =>
  {
    var buttons = question.Answers.Select(answer => new TaskDialogButton(answer)).ToList();
    var page = new TaskDialogPage
    {
      Caption = "xldocui",
      Heading = question.Heading,
      Text = question.Text,
      Icon = TaskDialogIcon.Warning,
      AllowCancel = true,
      DefaultButton = buttons[question.DefaultAnswer],
    };
    foreach (var button in buttons)
    {
      page.Buttons.Add(button);
    }

    var index = buttons.IndexOf(TaskDialog.ShowDialog(owner, page));
    return index >= 0 ? index : (int?)null;
  });
}
