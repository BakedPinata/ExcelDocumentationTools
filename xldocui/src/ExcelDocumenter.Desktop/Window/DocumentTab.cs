using System.CommandLine;
using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;
using VbaDocumenter.Core.Output;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;
using static ExcelDocumenter.Desktop.Window.TabParts;

namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// The Document tab: every option of both tools' main commands, showing only what applies. A tool's own options only
/// show when it's ticked and reads the input, and the PlantUML server only when a format needs it. Tooltips are the
/// tools' own help.
/// </summary>
internal sealed class DocumentTab : UserControl
{
  private static readonly VbaCli.CliDefinition VbadocHelp = new();
  private static readonly PqCli.CliDefinition PqdocHelp = new();

  private readonly ToolTip tips = new() { AutoPopDelay = 30000 };
  private readonly TabGrid rows = new();

  private readonly ComboBox input = new FieldComboBox(240) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Name = "Input", AccessibleName = "Workbook or folder" };
  private readonly CheckBox embedSource = new() { Text = "Put the source in the page", AutoSize = true };
  private readonly ComboBox onExists = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "If a file exists" };
  private readonly ComboBox logDetail = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Log" };
  private readonly CheckBox vbadoc = new() { Text = "VBA, with vbadoc", AutoSize = true, Name = "Vbadoc" };
  private readonly Label vbadocNote = Note();
  private readonly TextBox vbadocOutput = Field("vbadoc's output folder");
  private readonly TextBox vbadocName = Field("vbadoc's page name");
  private readonly CheckBox pqdoc = new() { Text = "Power Query, with pqdoc", AutoSize = true, Name = "Pqdoc" };
  private readonly Label pqdocNote = Note();
  private readonly TextBox pqdocOutput = Field("pqdoc's output folder");
  private readonly TextBox pqdocName = Field("pqdoc's file names");
  private readonly Dictionary<OutputFormat, CheckBox> formats;
  private readonly TextBox plantUmlServer = Field("PlantUML server");
  private readonly List<Control> vbadocRows = [];
  private readonly List<Control> pqdocRows = [];
  private readonly List<Control> serverRow = [];
  private bool showing;

  public DocumentTab()
  {
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    var browse = MakeButton("&Browse…", "Choose a workbook");
    browse.Click += (_, _) => BrowseForWorkbook();
    var folder = MakeButton("&Folder…", "Choose a folder of .m files, for Power Query");
    folder.Click += (_, _) => BrowseForFolder();
    rows.Add(Caption("&Workbook"), input, Flow(browse, folder));
    rows.Add(null, Note("A workbook, or for Power Query a folder of .m files. You can also drop one on the window.", visible: true));

    onExists.Items.AddRange(new object[]
    {
      new Choice<OnExistsBehaviour>("Ask", OnExistsBehaviour.Prompt),
      new Choice<OnExistsBehaviour>("Overwrite", OnExistsBehaviour.Overwrite),
      new Choice<OnExistsBehaviour>("Skip", OnExistsBehaviour.Skip),
      new Choice<OnExistsBehaviour>("Stop", OnExistsBehaviour.Fail),
    });
    logDetail.Items.AddRange(new object[]
    {
      new Choice<LogDetail>("Normal", LogDetail.Normal),
      new Choice<LogDetail>("Verbose", LogDetail.Verbose),
      new Choice<LogDetail>("Quiet", LogDetail.Quiet),
    });
    rows.Add(Caption("Options"), Flow(embedSource, Caption("If a file &exists"), onExists, Caption("&Log"), logDetail));

    var vbadocBrowse = MakeButton("Br&owse…", "Choose where vbadoc writes");
    vbadocBrowse.Click += (_, _) => BrowseForOutput(vbadocOutput, "Choose where vbadoc writes the source files and page");
    rows.Add(null, Heading(vbadocNote, vbadoc));
    vbadocRows.AddRange(rows.Add(Caption("Output folder", indent: true), vbadocOutput, vbadocBrowse));
    vbadocRows.AddRange(rows.Add(Caption("Page name", indent: true), vbadocName));

    var pqdocBrowse = MakeButton("Bro&wse…", "Choose where pqdoc writes");
    pqdocBrowse.Click += (_, _) => BrowseForOutput(pqdocOutput, "Choose where pqdoc writes its outputs and query files");
    formats = OutputFormatExtensions.All.ToDictionary(format => format, format => new CheckBox
    {
      Text = format switch
      {
        OutputFormat.Html => "Page (html)",
        OutputFormat.Puml => "PlantUML (puml)",
        _ => format.CliName().ToUpperInvariant(),
      },
      AutoSize = true,
    });
    rows.Add(null, Heading(pqdocNote, pqdoc));
    pqdocRows.AddRange(rows.Add(Caption("Output folder", indent: true), pqdocOutput, pqdocBrowse));
    pqdocRows.AddRange(rows.Add(Caption("File names", indent: true), pqdocName));
    pqdocRows.AddRange(rows.Add(Caption("Formats", indent: true), Flow([.. formats.Values])));
    serverRow.AddRange(rows.Add(Caption("PlantUML server", indent: true), plantUmlServer));
    plantUmlServer.PlaceholderText = "Or set PLANTUML_SERVER_URL, or a .env file";

    SetTips();
    Controls.Add(rows.Panel);

    input.TextChanged += (_, _) => OnChanged();
    input.SelectionChangeCommitted += (_, _) =>
    {
      // After the combo box has finished with the choice, which Show changes.
      if (input.SelectedItem is string chosen)
      {
        BeginInvoke(() => InputChosen?.Invoke(this, chosen));
      }
    };
    foreach (var box in new[] { embedSource, vbadoc, pqdoc }.Concat(formats.Values))
    {
      box.CheckedChanged += (_, _) => OnChanged();
    }

    foreach (var box in new[] { onExists, logDetail })
    {
      box.SelectedIndexChanged += (_, _) => OnChanged();
    }

    foreach (var field in new[] { vbadocOutput, vbadocName, pqdocOutput, pqdocName, plantUmlServer })
    {
      field.TextChanged += (_, _) => OnChanged();
    }

    Show(new DocumentSettings(), []);
  }

  /// <summary>Anything on the tab changed.</summary>
  public event EventHandler? Changed;

  /// <summary>The narrowest the tab can be and show everything it shows now, with each field at its least width.</summary>
  public int LeastWidth => rows.LeastWidth;

  /// <summary>A workbook or folder was chosen with Browse, Folder or from the recent ones, rather than typed.</summary>
  public event EventHandler<string>? InputChosen;

  /// <summary>Shows settings, and the recent workbooks and folders to choose from.</summary>
  public void Show(DocumentSettings settings, IEnumerable<string> recent)
  {
    ArgumentNullException.ThrowIfNull(settings);
    showing = true;
    try
    {
      ShowRecent(recent);

      // Otherwise text that isn't one of the recent ones can lose to the recent one chosen before.
      input.SelectedIndex = -1;
      input.Text = settings.Input;
      embedSource.Checked = settings.EmbedSource;
      SelectChoice(onExists, settings.OnExists);
      SelectChoice(logDetail, settings.LogDetail);
      vbadoc.Checked = settings.Vbadoc.Run;
      vbadocOutput.Text = settings.Vbadoc.Output;
      vbadocName.Text = settings.Vbadoc.OutputName;
      pqdoc.Checked = settings.Pqdoc.Run;
      pqdocOutput.Text = settings.Pqdoc.Output;
      pqdocName.Text = settings.Pqdoc.OutputName;
      foreach (var (format, box) in formats)
      {
        box.Checked = settings.Pqdoc.Formats.Contains(format);
      }

      plantUmlServer.Text = settings.Pqdoc.PlantUmlServer;
    }
    finally
    {
      showing = false;
    }

    OnChanged();
  }

  /// <summary>Replaces the recent workbooks and folders, leaving what's typed.</summary>
  public void ShowRecent(IEnumerable<string> recent)
  {
    var text = input.Text;
    input.BeginUpdate();
    input.Items.Clear();
    input.Items.AddRange(recent.Cast<object>().ToArray());
    input.EndUpdate();
    input.Text = text;
  }

  /// <summary>What the tab is set to now.</summary>
  public DocumentSettings Read() => new()
  {
    Input = input.Text,
    EmbedSource = embedSource.Checked,
    OnExists = Selected<OnExistsBehaviour>(onExists),
    LogDetail = Selected<LogDetail>(logDetail),
    Vbadoc = new VbadocSettings { Run = vbadoc.Checked, Output = vbadocOutput.Text, OutputName = vbadocName.Text },
    Pqdoc = new PqdocSettings
    {
      Run = pqdoc.Checked,
      Output = pqdocOutput.Text,
      OutputName = pqdocName.Text,
      Formats = [.. formats.Where(f => f.Value.Checked).Select(f => f.Key)],
      PlantUmlServer = plantUmlServer.Text,
    },
  };

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      tips.Dispose();
    }

    base.Dispose(disposing);
  }

  private void OnChanged()
  {
    if (showing)
    {
      return;
    }

    ShowWhatApplies();
    Changed?.Invoke(this, EventArgs.Empty);
  }

  private void ShowWhatApplies()
  {
    var chosen = DocumentCommands.Tidy(input.Text);
    var vbadocReads = Readers.Reads(Tool.Vbadoc, chosen);
    var pqdocReads = Readers.Reads(Tool.Pqdoc, chosen);

    vbadoc.Enabled = vbadocReads;
    vbadocNote.Text = vbadocReads ? "" : $"({Readers.WhyNot(Tool.Vbadoc, chosen)})";
    vbadocNote.Visible = !vbadocReads;
    pqdoc.Enabled = pqdocReads;
    pqdocNote.Text = pqdocReads ? "" : $"({Readers.WhyNot(Tool.Pqdoc, chosen)})";
    pqdocNote.Visible = !pqdocReads;

    var showVbadoc = vbadoc.Checked && vbadocReads;
    var showPqdoc = pqdoc.Checked && pqdocReads;
    SuspendLayout();
    vbadocRows.ForEach(control => control.Visible = showVbadoc);
    pqdocRows.ForEach(control => control.Visible = showPqdoc);
    var needsServer = formats.Any(f => f.Value.Checked && f.Key.IsRendered());
    serverRow.ForEach(control => control.Visible = showPqdoc && needsServer);
    ResumeLayout(true);

    // The names the tools would use, worked out by the tools.
    vbadocName.PlaceholderText = "Default: " + (chosen.Length > 0 && vbadocReads
      ? HtmlPageWriter.DefaultName(chosen)
      : OptionHelp.Default(VbadocHelp.OutputName.Description));
    pqdocName.PlaceholderText = "Default: " + (chosen.Length > 0 && pqdocReads
      ? AnalyserOptions.DefaultOutputName(chosen, File.Exists(chosen))
      : OptionHelp.Default(PqdocHelp.OutputName.Description));
    vbadocOutput.PlaceholderText = "Default: " + OptionHelp.Default(VbadocHelp.Output.Description);
    pqdocOutput.PlaceholderText = "Default: " + OptionHelp.Default(PqdocHelp.Output.Description);
  }

  private void SetTips()
  {
    static string Both(Option vbadocOption, Option pqdocOption) =>
      $"vbadoc: {vbadocOption.Description}\n\npqdoc: {pqdocOption.Description}";

    tips.SetToolTip(input, $"vbadoc: {VbadocHelp.Workbook.Description}\n\npqdoc: {PqdocHelp.Input.Description}");
    tips.SetToolTip(embedSource, Both(VbadocHelp.EmbedSource, PqdocHelp.EmbedSource));
    tips.SetToolTip(onExists, Both(VbadocHelp.Overwrite.OnExists, PqdocHelp.OnExists) + "\n\nAsk asks in a dialog.");
    tips.SetToolTip(logDetail, $"Verbose: {VbadocHelp.Verbose.Description}\n\nQuiet: {VbadocHelp.Quiet.Description}");
    tips.SetToolTip(vbadocOutput, OptionHelp.WithoutDefault(VbadocHelp.Output.Description));
    tips.SetToolTip(vbadocName, OptionHelp.WithoutDefault(VbadocHelp.OutputName.Description));
    tips.SetToolTip(pqdocOutput, OptionHelp.WithoutDefault(PqdocHelp.Output.Description));
    tips.SetToolTip(pqdocName, OptionHelp.WithoutDefault(PqdocHelp.OutputName.Description));
    foreach (var box in formats.Values)
    {
      tips.SetToolTip(box, OptionHelp.WithoutDefault(PqdocHelp.Formats.Description));
    }

    tips.SetToolTip(plantUmlServer, PqdocHelp.PlantUmlServer.Description);
  }

  private void BrowseForWorkbook()
  {
    using var picker = new OpenFileDialog
    {
      Title = "Choose a workbook",
      Filter = "Workbooks (*.xlsm;*.xlsb;*.xlsx;*.xlam;*.xls;*.xla)|*.xlsm;*.xlsb;*.xlsx;*.xlam;*.xls;*.xla|All files (*.*)|*.*",
      InitialDirectory = FolderOf(DocumentCommands.Tidy(input.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      InputChosen?.Invoke(this, picker.FileName);
    }
  }

  private void BrowseForFolder()
  {
    using var picker = new FolderBrowserDialog
    {
      Description = "Choose a folder of .m files, for Power Query",
      UseDescriptionForTitle = true,
      InitialDirectory = FolderOf(DocumentCommands.Tidy(input.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      InputChosen?.Invoke(this, picker.SelectedPath);
    }
  }

  private void BrowseForOutput(TextBox field, string description)
  {
    using var picker = new FolderBrowserDialog
    {
      Description = description,
      UseDescriptionForTitle = true,
      ShowNewFolderButton = true,
      InitialDirectory = FolderOf(DocumentCommands.Tidy(field.Text.Length > 0 ? field.Text : input.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      field.Text = picker.SelectedPath;
    }
  }
}
