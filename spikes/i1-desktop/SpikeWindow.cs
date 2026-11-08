using System.Diagnostics;

namespace DesktopSpike;

/// <summary>
/// A file picker, a dialog and a log pane: what the real window needs, and nothing it doesn't. The Run button has the
/// shape of a real run: work off the window's thread, a question asked on it, and a Cancel that uses a token.
/// </summary>
internal sealed class SpikeWindow : Form
{
  private readonly TempFolderWatch _tempFolder;
  private readonly string? _reportTo;
  private readonly TextBox _workbook = new() { Dock = DockStyle.Fill, Name = "Workbook" };
  private readonly TextBox _log = new()
  {
    Dock = DockStyle.Fill,
    Multiline = true,
    Name = "Log",
    ReadOnly = true,
    ScrollBars = ScrollBars.Vertical,
    Font = new Font(FontFamily.GenericMonospace, 9f),
  };
  private readonly Button _run = new() { Text = "Run", AutoSize = true };
  private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
  private TimeSpan _toWindow;
  private CancellationTokenSource? _running;

  public SpikeWindow(TempFolderWatch tempFolder, string? reportTo)
  {
    _tempFolder = tempFolder;
    _reportTo = reportTo;

    Text = "xldocui spike";
    Font = SystemFonts.MessageBoxFont ?? Font;
    AutoScaleMode = AutoScaleMode.Dpi;
    ClientSize = new Size(820, 520);

    var browse = new Button { Text = "Browse…", AutoSize = true };
    browse.Click += (_, _) => PickWorkbook();
    var report = new Button { Text = "Report", AutoSize = true };
    report.Click += (_, _) => Log(StartupReport.Describe(_toWindow, _tempFolder));
    var copy = new Button { Text = "Copy the log", AutoSize = true };
    copy.Click += (_, _) => Clipboard.SetText(_log.Text.Length == 0 ? " " : _log.Text);
    _run.Click += OnRun;
    _cancel.Click += (_, _) => _running?.Cancel();

    var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
    buttons.Controls.AddRange([_run, _cancel, report, copy]);

    var layout = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 3 };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.Controls.Add(new Label { Text = "Workbook", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
    layout.Controls.Add(_workbook, 1, 0);
    layout.Controls.Add(browse, 2, 0);
    layout.Controls.Add(buttons, 0, 1);
    layout.SetColumnSpan(buttons, 3);
    layout.Controls.Add(_log, 0, 2);
    layout.SetColumnSpan(_log, 3);
    Controls.Add(layout);

    Shown += OnShown;
  }

  private void OnShown(object? sender, EventArgs e)
  {
    using var process = Process.GetCurrentProcess();
    _toWindow = DateTime.Now - process.StartTime;
    var report = StartupReport.Describe(_toWindow, _tempFolder);
    Log(report);

    if (_reportTo is not null)
    {
      File.WriteAllText(_reportTo, report);
      Close();
    }
  }

  private void PickWorkbook()
  {
    using var picker = new OpenFileDialog
    {
      Filter = "Workbooks (*.xlsx;*.xlsm;*.xlsb)|*.xlsx;*.xlsm;*.xlsb|All files (*.*)|*.*",
      Title = "Choose a workbook",
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      _workbook.Text = picker.FileName;
      Log($"Picked {picker.FileName}");
    }
  }

  private async void OnRun(object? sender, EventArgs e)
  {
    using var running = new CancellationTokenSource();
    _running = running;
    _run.Enabled = false;
    _cancel.Enabled = true;
    try
    {
      await Task.Run(() => Work(running.Token), running.Token);
      Log("Finished.");
    }
    catch (OperationCanceledException)
    {
      Log("Cancelled.");
    }
    finally
    {
      _running = null;
      _run.Enabled = true;
      _cancel.Enabled = false;
    }
  }

  // Off the window's thread, as a tool's run will be.
  private void Work(CancellationToken cancelled)
  {
    for (var step = 1; step <= 3; step++)
    {
      cancelled.ThrowIfCancellationRequested();
      Invoke(() => Log($"Working, step {step} of 3 on thread {Environment.CurrentManagedThreadId}"));
      Thread.Sleep(400);
    }

    var answer = Invoke(AskAboutClash);
    Invoke(() => Log($"Answered {answer}"));
  }

  // The dialog step I4 needs: more answers than a message box can give.
  private string AskAboutClash()
  {
    var page = new TaskDialogPage
    {
      Caption = "xldocui spike",
      Heading = "Utilities is already in the target",
      Text = "Skip keeps the one in the target. Overwrite replaces it with the one coming in.",
      Buttons =
      {
        new TaskDialogButton("Skip"),
        new TaskDialogButton("Overwrite"),
        new TaskDialogButton("Skip All"),
        new TaskDialogButton("Overwrite All"),
        new TaskDialogButton("Quit"),
      },
    };
    return TaskDialog.ShowDialog(this, page).Text ?? "";
  }

  private void Log(string text)
  {
    _log.AppendText(text.ReplaceLineEndings() + Environment.NewLine);
  }
}
