using System.ComponentModel;
using System.Diagnostics;
using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Desktop.Settings;
using ExcelDocumenter.Shared;
using PowerQueryDocumenter.Core.Export;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// The window: the tabs, the command they make with Copy, Run and Cancel, and the log of the run. A run takes each
/// command in turn, off the window's thread, and says how each ended.
/// </summary>
internal sealed class MainWindow : Form
{
  private readonly string settingsPath;
  private readonly AppSettings settings;
  private readonly HttpClient plantUmlClient = PlantUmlClient.CreateHttpClient();
  private readonly ToolRunner runner;
  private readonly DialogInteraction interaction;
  private readonly DocumentTab document = new() { Dock = DockStyle.Top };
  private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
  private readonly TabPage documentPage = new("Document") { Padding = new Padding(8), AutoScroll = true, UseVisualStyleBackColor = true };
  private readonly TableLayoutPanel top = new() { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 4), RowCount = 2 };
  private readonly TableLayoutPanel commandPanel = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
  private readonly SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1 };
  private readonly TextBox command = new()
  {
    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
    Multiline = true,
    ReadOnly = true,
    ScrollBars = ScrollBars.Horizontal,
    WordWrap = false,
    Font = new Font("Consolas", 9.75f),
    AccessibleName = "Command",
  };
  private readonly Label problems = new() { AutoSize = true, ForeColor = Color.FromArgb(0xB0, 0x2A, 0x1E), Margin = new Padding(3, 0, 3, 6) };
  private readonly Button copy = new Button { Text = "C&opy", Anchor = AnchorStyles.Top }.SizedToText();
  private readonly Button run = new Button { Text = "&Run" }.SizedToText();
  private readonly Button cancel = new Button { Text = "&Cancel", Enabled = false }.SizedToText();
  private readonly Button openPages = new Button { Text = "Open the &page", Enabled = false }.SizedToText();
  private readonly CheckBox openWhenWritten = new() { Text = "Open each page when it's written", AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
  private readonly LogPane log = new();
  private readonly ToolStripStatusLabel versions = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
  private readonly List<string> pages = [];
  private readonly string? initialInput;
  private CancellationTokenSource? running;
  private bool closeWhenDone;

  /// <param name="settingsPath">Where the settings are kept.</param>
  /// <param name="input">A workbook or folder to start with, as given on the command line, or null.</param>
  public MainWindow(string settingsPath, string? input)
  {
    this.settingsPath = settingsPath;
    settings = SettingsStore.Load(settingsPath);
    runner = new ToolRunner(plantUmlClient);
    interaction = new DialogInteraction(this);

    SuspendLayout();
    AutoScaleDimensions = new SizeF(96F, 96F);
    AutoScaleMode = AutoScaleMode.Dpi;
    Text = "xldocui";
    ClientSize = new Size(900, 780);
    MinimumSize = new Size(820, 600);
    StartPosition = FormStartPosition.CenterScreen;

    documentPage.Controls.Add(document);
    tabs.TabPages.Add(documentPage);

    commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    commandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    var caption = new Label { Text = "Command", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    commandPanel.Controls.Add(caption, 0, 0);
    commandPanel.SetColumnSpan(caption, 2);
    commandPanel.Controls.Add(command, 0, 1);
    commandPanel.Controls.Add(copy, 1, 1);
    commandPanel.Controls.Add(problems, 0, 2);
    commandPanel.SetColumnSpan(problems, 2);
    var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
    buttons.Controls.AddRange([run, cancel, openWhenWritten, openPages]);
    commandPanel.Controls.Add(buttons, 0, 3);
    commandPanel.SetColumnSpan(buttons, 2);

    top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    top.Controls.Add(tabs, 0, 0);
    top.Controls.Add(commandPanel, 0, 1);
    split.Panel1.Controls.Add(top);
    split.Panel2.Controls.Add(log.Box);
    split.Panel2.Padding = new Padding(8, 0, 8, 0);

    var status = new StatusStrip { SizingGrip = true };
    status.Items.Add(versions);
    ShowVersions();

    Controls.Add(status);
    Controls.Add(split);
    split.BringToFront();
    ResumeLayout(false);
    PerformLayout();

    document.Changed += (_, _) => ShowCommand();
    document.InputChosen += (_, chosen) => Choose(chosen);
    copy.Click += (_, _) => CopyCommand();
    run.Click += async (_, _) => await RunAsync();
    cancel.Click += (_, _) => running?.Cancel();
    openPages.Click += (_, _) => pages.ForEach(OpenPage);
    openWhenWritten.Checked = settings.OpenPages;
    AcceptDrops(this);

    document.Show(settings.Document, settings.Recent.Select(r => r.Input));
    initialInput = input;
  }

  /// <summary>Makes a workbook or folder the input, with the outputs last used for it.</summary>
  public void Choose(string input)
  {
    if (running is not null)
    {
      return;
    }

    settings.Document = document.Read();
    settings.Choose(input);
    document.Show(settings.Document, settings.Recent.Select(r => r.Input));
  }

  protected override void OnLoad(EventArgs e)
  {
    base.OnLoad(e);

    // Once the controls exist: a combo box made before then puts back its earlier choice when it's created.
    if (initialInput is not null)
    {
      Choose(initialInput);
    }

    FitCommand();
    FitTabs();
  }

  // Once the window has its size at the screen's scale.
  protected override void OnShown(EventArgs e)
  {
    base.OnShown(e);
    FitTabs();
  }

  protected override void OnDpiChanged(DpiChangedEventArgs e)
  {
    base.OnDpiChanged(e);
    FitCommand();
    FitTabs();
  }

  protected override void OnFormClosing(FormClosingEventArgs e)
  {
    // A run finishes what it's doing first, as it would after Ctrl+C, and the window closes when it has.
    if (running is not null)
    {
      e.Cancel = true;
      closeWhenDone = true;
      running.Cancel();
      return;
    }

    settings.Document = document.Read();
    settings.OpenPages = openWhenWritten.Checked;
    SaveSettings();
    base.OnFormClosing(e);
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      plantUmlClient.Dispose();
      log.Dispose();
      command.Font.Dispose();
    }

    base.Dispose(disposing);
  }

  private async Task RunAsync()
  {
    var current = document.Read();
    var commands = DocumentCommands.For(current);
    if (running is not null || commands.Count == 0 || DocumentCommands.Problems(current).Count > 0)
    {
      return;
    }

    settings.Document = current;
    settings.OpenPages = openWhenWritten.Checked;
    settings.Remember(current);
    document.ShowRecent(settings.Recent.Select(r => r.Input));
    SaveSettings();

    using var cancellation = new CancellationTokenSource();
    running = cancellation;
    pages.Clear();
    log.Clear();
    interaction.Reset();
    ShowState();
    try
    {
      foreach (var next in commands)
      {
        if (next != commands[0])
        {
          log.Add(new LogLine("", LogKind.Plain));
        }

        log.Add(new LogLine(next.Text, LogKind.Command));
        var result = await runner.RunAsync(next, interaction, log.Post, cancellation.Token);
        log.Add(new LogLine(
          $"{next.Tool.Name()}: {ExitCodeText.Describe(result.ExitCode)}",
          result.ExitCode == ExitCodes.Success ? LogKind.Done : LogKind.Failed));
        if (result.Page is not null)
        {
          pages.Add(result.Page);
        }

        if (result.ExitCode == ExitCodes.Cancelled || interaction.QuitAnswered)
        {
          break;
        }
      }
    }
    finally
    {
      running = null;
      ShowState();
    }

    if (closeWhenDone)
    {
      Close();
    }
    else if (openWhenWritten.Checked)
    {
      pages.ForEach(OpenPage);
    }
  }

  // The commands the tab makes, and anything that stops them running.
  private void ShowCommand()
  {
    var current = document.Read();
    command.Text = string.Join(Environment.NewLine, DocumentCommands.For(current).Select(c => c.Text));
    var found = DocumentCommands.Problems(current);
    problems.Text = string.Join(Environment.NewLine, found);
    problems.Visible = found.Count > 0;
    ShowState();
    FitTabs();
  }

  private void ShowState()
  {
    var idle = running is null;
    var current = document.Read();
    run.Enabled = idle && DocumentCommands.For(current).Count > 0 && DocumentCommands.Problems(current).Count == 0;
    cancel.Enabled = !idle;
    document.Enabled = idle;
    copy.Enabled = command.TextLength > 0;
    openPages.Enabled = idle && pages.Count > 0;
    openPages.Text = pages.Count > 1 ? "Open the &pages" : "Open the &page";
  }

  private void CopyCommand()
  {
    if (command.TextLength > 0)
    {
      Clipboard.SetText(command.Text);
    }
  }

  private void OpenPage(string page)
  {
    try
    {
      Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
    {
      log.Add(new LogLine($"Couldn't open {page}: {ex.Message}", LogKind.Error));
    }
  }

  private void SaveSettings()
  {
    try
    {
      SettingsStore.Save(settingsPath, settings);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      log.Add(new LogLine($"Couldn't save the settings in {settingsPath}: {ex.Message}", LogKind.Warning));
    }
  }

  private void ShowVersions()
  {
    ToolVersion[] all =
    [
      ToolVersion.Of(typeof(MainWindow).Assembly),
      ToolVersion.Of(typeof(VbaCli.CliRunner).Assembly),
      ToolVersion.Of(typeof(PqCli.CliRunner).Assembly),
    ];
    versions.Text = string.Join("   ·   ", all.Select(v => v.ToString()));
    versions.ToolTipText = string.Join(
      Environment.NewLine,
      all.Select(v => v.Commit is null
        ? $"{v}, built outside git"
        : $"{v.Product} {v.Version}, commit {v.Commit}{(v.Dirty ? ", built with uncommitted changes" : "")}"));
  }

  // Two lines of commands and the scroll bar, at whatever the screen's scale is.
  private void FitCommand() =>
    command.Height = (TextRenderer.MeasureText("X", command.Font).Height * 2) + SystemInformation.HorizontalScrollBarHeight + 8;

  // The tab as tall as what it shows, leaving the rest to the log.
  private void FitTabs()
  {
    if (!IsHandleCreated)
    {
      return;
    }

    // The tab and the command panel are laid out by now, so their heights are what they show.
    var header = tabs.Height - tabs.DisplayRectangle.Height;
    var needed = document.Height + documentPage.Padding.Vertical + header + commandPanel.Height + top.Padding.Vertical
      + LogicalToDeviceUnits(8);
    var room = split.Height - split.SplitterWidth - LogicalToDeviceUnits(120);
    if (room > split.Panel1MinSize)
    {
      split.SplitterDistance = Math.Clamp(needed, split.Panel1MinSize, room);
    }
  }

  // A workbook or folder dropped anywhere on the window is chosen. Every control takes the drop, so there's no part of
  // the window that refuses it.
  private void AcceptDrops(Control control)
  {
    control.AllowDrop = true;
    control.DragEnter += (_, e) => e.Effect = running is null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
      ? DragDropEffects.Copy
      : DragDropEffects.None;
    control.DragDrop += (_, e) =>
    {
      if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
      {
        Choose(paths[0]);
      }
    };
    foreach (Control child in control.Controls)
    {
      AcceptDrops(child);
    }
  }
}
