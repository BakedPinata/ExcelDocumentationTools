using System.CommandLine;
using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Importing;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;
using static ExcelDocumenter.Desktop.Window.TabParts;

namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// The Import tab: a source, what to take from it, ticked in a list the tool reads from the source or chosen with a
/// manifest, and where it goes. Only what applies is shown, and the tooltips and notes are the tool's own help.
/// </summary>
internal sealed class ImportTab : UserControl
{
  private static readonly VbaCli.CliDefinition VbadocHelp = new();
  private static readonly PqCli.CliDefinition PqdocHelp = new();

  private readonly ToolTip tips = new() { AutoPopDelay = 30000 };
  private readonly TabGrid rows = new();
  private readonly ComboBox source = new FieldComboBox(240) { Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Source" };
  private readonly RadioButton vbadoc = Radio("Modules and classes, with vbadoc");
  private readonly RadioButton pqdoc = Radio("Queries, with pqdoc");
  private readonly Label toolNote = Note();
  private readonly RadioButton fromList = Radio("Tick them in the list");
  private readonly RadioButton fromManifest = Radio("From a manifest");
  private readonly ListView items = new()
  {
    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
    CheckBoxes = true,
    FullRowSelect = true,
    HeaderStyle = ColumnHeaderStyle.Nonclickable,
    Height = 170,
    View = View.Details,
    AccessibleName = "What to take",
  };
  private readonly Label itemsNote = Note(visible: true);
  private readonly TextBox manifest = Field("Manifest");
  private readonly RadioButton toBundle = Radio("A bundle folder");
  private readonly RadioButton toNew = Radio("A new workbook");
  private readonly RadioButton toCopy = Radio("A copy of a target");
  private readonly RadioButton toTarget = Radio("The target itself");
  private readonly TextBox bundle = Field("Bundle folder");
  private readonly TextBox newWorkbook = Field("New workbook");
  private readonly TextBox target = Field("Target workbook");
  private readonly Label destinationNote = Note(visible: true);
  private readonly Label onClashCaption = Caption("If it's in the &target");
  private readonly ComboBox onClash = Choices(
    "If it's in the target",
    new Choice<OnClashBehaviour>("Ask", OnClashBehaviour.Prompt),
    new Choice<OnClashBehaviour>("Overwrite", OnClashBehaviour.Overwrite),
    new Choice<OnClashBehaviour>("Skip", OnClashBehaviour.Skip));
  private readonly Label onExistsCaption = Caption("If a file e&xists");
  private readonly ComboBox onExists = Choices(
    "If a file exists",
    new Choice<OnExistsBehaviour>("Ask", OnExistsBehaviour.Prompt),
    new Choice<OnExistsBehaviour>("Overwrite", OnExistsBehaviour.Overwrite),
    new Choice<OnExistsBehaviour>("Skip", OnExistsBehaviour.Skip),
    new Choice<OnExistsBehaviour>("Stop", OnExistsBehaviour.Fail));
  private readonly ComboBox logDetail = Choices(
    "Log",
    new Choice<LogDetail>("Normal", LogDetail.Normal),
    new Choice<LogDetail>("Verbose", LogDetail.Verbose),
    new Choice<LogDetail>("Quiet", LogDetail.Quiet));
  private readonly FlowLayoutPanel options;
  private readonly CheckBox noReferences = new() { Text = "Leave out library references", AutoSize = true };
  private readonly CheckBox dropSignature = new() { Text = "Remove the target's signature, if it has one", AutoSize = true };
  private readonly List<Control> listRows = [];
  private readonly List<Control> manifestRow = [];
  private readonly List<Control> bundleRow = [];
  private readonly List<Control> newRow = [];
  private readonly List<Control> targetRow = [];
  private readonly List<Control> vbadocRow = [];
  private readonly System.Windows.Forms.Timer readSoon = new() { Interval = 500 };
  private List<string> ticked = [];
  private string? listed;
  private int reading;
  private bool showing;

  public ImportTab()
  {
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    options = Flow(onClashCaption, onClash, onExistsCaption, onExists, Caption("&Log"), logDetail);

    var browse = MakeButton("&Browse…", "Choose a workbook to take from");
    browse.Click += (_, _) => BrowseForSource();
    var folder = MakeButton("&Folder…", "Choose a folder of .m files to take queries from");
    folder.Click += (_, _) => BrowseForFolder();
    rows.Add(Caption("&Source"), source, Flow(browse, folder));
    rows.Add(null, Note("A workbook, or for queries a folder of .m files. You can also drop one on the window, or a manifest.", visible: true));
    rows.Add(Caption("Take"), Flow(vbadoc, pqdoc, toolNote));
    rows.Add(Caption("Choose"), Flow(fromList, fromManifest));

    items.Columns.Add("Name");
    items.Columns.Add("What it is");
    items.Columns.Add("Why it can't come");
    var again = MakeButton("Read &again", "Read the list from the source again");
    again.Click += async (_, _) => await ReadListAsync(again: true);
    var all = MakeButton("&All", "Tick everything that can come");
    all.Click += (_, _) => TickAll(true);
    var none = MakeButton("&None", "Untick everything");
    none.Click += (_, _) => TickAll(false);
    var listButtons = Flow(again, all, none);
    listButtons.FlowDirection = FlowDirection.TopDown;
    listButtons.Anchor = AnchorStyles.Left | AnchorStyles.Top;
    listRows.AddRange(rows.Add(Caption("", indent: true), items, listButtons));
    listRows.AddRange(rows.Add(Caption("", indent: true), itemsNote));

    var browseManifest = MakeButton("Br&owse…", "Choose a manifest");
    browseManifest.Click += (_, _) => BrowseForManifest();
    manifestRow.AddRange(rows.Add(Caption("Manifest", indent: true), manifest, browseManifest));

    rows.Add(Caption("Write to"), Flow(toBundle, toNew, toCopy, toTarget));
    var browseBundle = MakeButton("Brow&se…", "Choose a folder for the bundle");
    browseBundle.Click += (_, _) => BrowseForBundle();
    bundleRow.AddRange(rows.Add(Caption("Folder", indent: true), bundle, browseBundle));
    var browseNew = MakeButton("Brow&se…", "Choose a name for the new workbook");
    browseNew.Click += (_, _) => BrowseForNewWorkbook();
    newRow.AddRange(rows.Add(Caption("Workbook", indent: true), newWorkbook, browseNew));
    var browseTarget = MakeButton("Brow&se…", "Choose the target workbook");
    browseTarget.Click += (_, _) => BrowseForTarget();
    targetRow.AddRange(rows.Add(Caption("Target", indent: true), target, browseTarget));
    rows.Add(Caption("", indent: true), destinationNote);

    rows.Add(Caption("Options"), options);
    vbadocRow.AddRange(rows.Add(Caption("", indent: true), Flow(noReferences, dropSignature)));
    Controls.Add(rows.Panel);

    source.TextChanged += (_, _) =>
    {
      OnChanged();
      ReadSoon();
    };
    source.SelectionChangeCommitted += (_, _) =>
    {
      if (source.SelectedItem is string chosen)
      {
        BeginInvoke(() => ChooseSource(chosen));
      }
    };
    foreach (var radio in new[] { vbadoc, pqdoc, fromList })
    {
      radio.CheckedChanged += (_, _) =>
      {
        OnChanged();
        ReadSoon();
      };
    }

    foreach (var radio in new[] { fromManifest, toBundle, toNew, toCopy, toTarget })
    {
      radio.CheckedChanged += (_, _) => OnChanged();
    }

    foreach (var box in new[] { noReferences, dropSignature })
    {
      box.CheckedChanged += (_, _) => OnChanged();
    }

    foreach (var box in new[] { onClash, onExists, logDetail })
    {
      box.SelectedIndexChanged += (_, _) => OnChanged();
    }

    foreach (var field in new[] { manifest, bundle, newWorkbook, target })
    {
      field.TextChanged += (_, _) => OnChanged();
    }

    items.ItemCheck += (_, e) =>
    {
      // What can't come can't be ticked; the list says why.
      if (items.Items[e.Index].Tag is ImportableItem { CanTake: false })
      {
        e.NewValue = e.CurrentValue;
      }
    };
    items.ItemChecked += (_, _) =>
    {
      if (!showing)
      {
        ticked = [.. items.CheckedItems.Cast<ListViewItem>().Select(item => item.Text)];
        ShowSummary();
        OnChanged();
      }
    };
    // After the layout has settled, as a resize comes in steps.
    items.ClientSizeChanged += (_, _) =>
    {
      if (items.IsHandleCreated)
      {
        items.BeginInvoke(FitColumns);
      }
    };
    readSoon.Tick += async (_, _) =>
    {
      readSoon.Stop();
      await ReadListAsync();
    };

    Show(new ImportSettings(), []);
  }

  /// <summary>Anything on the tab changed.</summary>
  public event EventHandler? Changed;

  /// <summary>The narrowest the tab can be and show everything it shows now.</summary>
  public int LeastWidth => rows.LeastWidth;

  /// <summary>Shows settings, and the recent workbooks and folders to choose from.</summary>
  public void Show(ImportSettings settings, IEnumerable<string> recent)
  {
    ArgumentNullException.ThrowIfNull(settings);
    showing = true;
    try
    {
      ShowRecent(recent);
      source.SelectedIndex = -1;
      source.Text = settings.Source;
      (settings.Tool == Tool.Pqdoc ? pqdoc : vbadoc).Checked = true;
      (settings.ChooseFrom == ChooseFrom.Manifest ? fromManifest : fromList).Checked = true;
      manifest.Text = settings.Manifest;
      ticked = [.. settings.Items];
      (settings.WriteTo switch
      {
        ImportDestination.NewWorkbook => toNew,
        ImportDestination.Copy => toCopy,
        ImportDestination.InPlace => toTarget,
        _ => toBundle,
      }).Checked = true;
      bundle.Text = settings.Bundle;
      newWorkbook.Text = settings.NewWorkbook;
      target.Text = settings.Target;
      SelectChoice(onClash, settings.OnClash);
      SelectChoice(onExists, settings.OnExists);
      SelectChoice(logDetail, settings.LogDetail);
      noReferences.Checked = settings.NoReferences;
      dropSignature.Checked = settings.DropSignature;
    }
    finally
    {
      showing = false;
    }

    OnChanged();
    ReadSoon();
  }

  /// <summary>Replaces the recent workbooks and folders, leaving what's typed.</summary>
  public void ShowRecent(IEnumerable<string> recent)
  {
    var text = source.Text;
    source.BeginUpdate();
    source.Items.Clear();
    source.Items.AddRange(recent.Cast<object>().ToArray());
    source.EndUpdate();
    source.Text = text;
  }

  /// <summary>What the tab is set to now.</summary>
  public ImportSettings Read() => new()
  {
    Source = source.Text,
    Tool = pqdoc.Checked ? Tool.Pqdoc : Tool.Vbadoc,
    ChooseFrom = fromManifest.Checked ? ChooseFrom.Manifest : ChooseFrom.List,
    Manifest = manifest.Text,
    Items = [.. ticked],
    WriteTo = toNew.Checked ? ImportDestination.NewWorkbook
      : toCopy.Checked ? ImportDestination.Copy
      : toTarget.Checked ? ImportDestination.InPlace
      : ImportDestination.Bundle,
    Bundle = bundle.Text,
    NewWorkbook = newWorkbook.Text,
    Target = target.Text,
    OnClash = Selected<OnClashBehaviour>(onClash),
    OnExists = Selected<OnExistsBehaviour>(onExists),
    LogDetail = Selected<LogDetail>(logDetail),
    NoReferences = noReferences.Checked,
    DropSignature = dropSignature.Checked,
  };

  /// <summary>A file dropped on the window: a manifest is the choice, anything else the source.</summary>
  public void Drop(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
    {
      manifest.Text = path;
      fromManifest.Checked = true;
    }
    else
    {
      ChooseSource(path);
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      tips.Dispose();
      readSoon.Dispose();
    }

    base.Dispose(disposing);
  }

  private static RadioButton Radio(string text) => new() { Text = text, AutoSize = true };

  // A source chosen rather than typed: its list is read now, and what was ticked for another source is let go.
  private void ChooseSource(string path)
  {
    if (!string.Equals(DocumentCommands.Tidy(source.Text), DocumentCommands.Tidy(path), StringComparison.OrdinalIgnoreCase))
    {
      ticked = [];
    }

    source.Text = path;
    readSoon.Stop();
    _ = ReadListAsync();
  }

  private void ReadSoon()
  {
    // Typing a path shouldn't read every half-typed one.
    readSoon.Stop();
    readSoon.Start();
  }

  private async Task ReadListAsync(bool again = false)
  {
    if (!fromList.Checked)
    {
      return;
    }

    var settings = Read();
    var path = DocumentCommands.Tidy(settings.Source);
    if (path.Length == 0 || ImportCommands.ToolFor(settings) is not { } tool)
    {
      ShowList([], "Choose a source, and its list is read from it.");
      return;
    }

    if (!File.Exists(path) && !Directory.Exists(path))
    {
      ShowList([], $"{path} isn't there.");
      return;
    }

    var key = $"{tool}|{Path.GetFullPath(path)}";
    if (key == listed && !again)
    {
      return;
    }

    var generation = ++reading;
    itemsNote.Text = $"Reading {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}…";
    try
    {
      var found = await ImportItems.ReadAsync(tool, path, CancellationToken.None);
      if (generation == reading)
      {
        listed = key;
        ShowList(found, null);
      }
    }
    catch (Exception ex) when (ImportItems.WhyUnreadable(ex) is { } why)
    {
      if (generation == reading)
      {
        listed = null;
        ShowList([], why);
      }
    }
  }

  private void ShowList(IReadOnlyList<ImportableItem> found, string? note)
  {
    if (note is not null)
    {
      listed = null;
    }

    showing = true;
    items.BeginUpdate();
    try
    {
      items.Items.Clear();
      foreach (var item in found)
      {
        items.Items.Add(new ListViewItem([item.Name, item.Kind, item.WhyNot ?? ""])
        {
          Tag = item,
          Checked = item.CanTake && ticked.Contains(item.Name, StringComparer.OrdinalIgnoreCase),
          ForeColor = item.CanTake ? SystemColors.WindowText : SystemColors.GrayText,
        });
      }
    }
    finally
    {
      items.EndUpdate();
      showing = false;
    }

    // What was ticked and isn't in this source is let go, so the command only names what's there.
    if (found.Count > 0)
    {
      ticked = [.. items.CheckedItems.Cast<ListViewItem>().Select(item => item.Text)];
    }

    FitColumns();
    if (note is null)
    {
      ShowSummary();
    }
    else
    {
      itemsNote.Text = note;
    }

    OnChanged();
  }

  private void ShowSummary() =>
    itemsNote.Text = ImportItems.Summary([.. items.Items.Cast<ListViewItem>().Select(item => (ImportableItem)item.Tag!)], ticked.Count);

  private void TickAll(bool tick)
  {
    foreach (ListViewItem item in items.Items)
    {
      if (item.Tag is ImportableItem { CanTake: true })
      {
        item.Checked = tick;
      }
    }
  }

  private void FitColumns()
  {
    var width = items.ClientSize.Width;
    if (items.Columns.Count == 3 && width > 0)
    {
      items.Columns[0].Width = width * 40 / 100;
      items.Columns[1].Width = width * 18 / 100;

      // Room is always left for a vertical scroll bar, which comes and goes with the items after this is worked out;
      // otherwise the columns come out wider than the list, with a horizontal scroll bar.
      items.Columns[2].Width = Math.Max(
        LogicalToDeviceUnits(60),
        width - items.Columns[0].Width - items.Columns[1].Width - SystemInformation.VerticalScrollBarWidth);
    }
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
    var settings = Read();
    var path = DocumentCommands.Tidy(settings.Source);
    var vbadocReads = Readers.Reads(Tool.Vbadoc, path);
    var pqdocReads = Readers.Reads(Tool.Pqdoc, path);
    vbadoc.Enabled = vbadocReads;
    pqdoc.Enabled = pqdocReads;

    // The tool that doesn't read the source gives way to the one that does.
    var tool = ImportCommands.ToolFor(settings);
    if (tool is { } reader && (reader == Tool.Pqdoc) != pqdoc.Checked)
    {
      showing = true;
      (reader == Tool.Pqdoc ? pqdoc : vbadoc).Checked = true;
      showing = false;
    }

    var whyNot = !vbadocReads ? Readers.WhyNot(Tool.Vbadoc, path) : !pqdocReads ? Readers.WhyNot(Tool.Pqdoc, path) : null;
    toolNote.Text = whyNot is null ? "" : $"({whyNot})";
    toolNote.Visible = whyNot is not null;

    var help = tool == Tool.Pqdoc ? (CliHelp)PqdocHelp : VbadocHelp;
    var writeTo = settings.WriteTo;
    var hasTarget = writeTo is ImportDestination.Copy or ImportDestination.InPlace;
    SuspendLayout();
    listRows.ForEach(control => control.Visible = fromList.Checked);
    manifestRow.ForEach(control => control.Visible = fromManifest.Checked);
    bundleRow.ForEach(control => control.Visible = writeTo == ImportDestination.Bundle);
    newRow.ForEach(control => control.Visible = writeTo == ImportDestination.NewWorkbook);
    targetRow.ForEach(control => control.Visible = hasTarget);
    // Each caption before its box, and the row laid out again after: shown together, a box could end up before its caption.
    onClashCaption.Visible = hasTarget;
    onClash.Visible = hasTarget;
    onExistsCaption.Visible = writeTo != ImportDestination.InPlace;
    onExists.Visible = writeTo != ImportDestination.InPlace;
    vbadocRow.ForEach(control => control.Visible = tool != Tool.Pqdoc);
    dropSignature.Visible = hasTarget;
    ResumeLayout(true);
    options.PerformLayout();

    destinationNote.Text = OptionHelp.WithoutDefault(writeTo switch
    {
      ImportDestination.Bundle => help.Import.Bundle.Description,
      ImportDestination.NewWorkbook => help.Target.New.Description,
      ImportDestination.Copy => help.Target.Into.Description,
      _ => help.Target.InPlace.Description,
    });
    SetTips(help);
  }

  private void SetTips(CliHelp help)
  {
    tips.SetToolTip(source, help.Import.Source.Description);
    tips.SetToolTip(manifest, help.Import.Manifest.Description);
    tips.SetToolTip(items, help.Import.Items.Description);
    tips.SetToolTip(bundle, help.Import.Bundle.Description);
    tips.SetToolTip(newWorkbook, help.Target.New.Description);
    tips.SetToolTip(target, help.Target.Into.Description);
    tips.SetToolTip(toTarget, help.Target.InPlace.Description);
    tips.SetToolTip(onClash, $"{OptionHelp.WithoutDefault(help.Target.OnClash.Description)}\n\nAsk asks in a dialog.");
    tips.SetToolTip(onExists, $"{OptionHelp.WithoutDefault(help.Import.Overwrite.OnExists.Description)}\n\nAsk asks in a dialog.");
    tips.SetToolTip(noReferences, VbadocHelp.NoReferences.Description);
    tips.SetToolTip(dropSignature, VbadocHelp.DropSignature.Description);
  }

  private void BrowseForSource()
  {
    using var picker = new OpenFileDialog
    {
      Title = "Choose a workbook to take from",
      Filter = "Workbooks (*.xlsm;*.xlsb;*.xlsx;*.xlam;*.xls;*.xla)|*.xlsm;*.xlsb;*.xlsx;*.xlam;*.xls;*.xla|All files (*.*)|*.*",
      InitialDirectory = FolderOf(DocumentCommands.Tidy(source.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      ChooseSource(picker.FileName);
    }
  }

  private void BrowseForFolder()
  {
    using var picker = new FolderBrowserDialog
    {
      Description = "Choose a folder of .m files to take queries from",
      UseDescriptionForTitle = true,
      InitialDirectory = FolderOf(DocumentCommands.Tidy(source.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      ChooseSource(picker.SelectedPath);
    }
  }

  private void BrowseForManifest()
  {
    using var picker = new OpenFileDialog
    {
      Title = "Choose a manifest, as the page's Export tab downloads",
      Filter = "Manifests (*.json)|*.json|All files (*.*)|*.*",
      InitialDirectory = FolderOf(DocumentCommands.Tidy(manifest.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      manifest.Text = picker.FileName;
    }
  }

  private void BrowseForBundle()
  {
    using var picker = new FolderBrowserDialog
    {
      Description = "Choose a folder for the bundle",
      UseDescriptionForTitle = true,
      ShowNewFolderButton = true,
      InitialDirectory = FolderOf(DocumentCommands.Tidy(bundle.Text.Length > 0 ? bundle.Text : source.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      bundle.Text = picker.SelectedPath;
    }
  }

  private void BrowseForNewWorkbook()
  {
    // The tool asks before overwriting, so the picker doesn't.
    using var picker = new SaveFileDialog
    {
      Title = "Choose a name for the new workbook",
      Filter = Writers.NewFileFilter(pqdoc.Checked ? Tool.Pqdoc : Tool.Vbadoc),
      OverwritePrompt = false,
      InitialDirectory = FolderOf(DocumentCommands.Tidy(newWorkbook.Text.Length > 0 ? newWorkbook.Text : source.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      newWorkbook.Text = picker.FileName;
    }
  }

  private void BrowseForTarget()
  {
    using var picker = new OpenFileDialog
    {
      Title = "Choose the target workbook",
      Filter = Writers.TargetFilter(pqdoc.Checked ? Tool.Pqdoc : Tool.Vbadoc),
      InitialDirectory = FolderOf(DocumentCommands.Tidy(target.Text.Length > 0 ? target.Text : source.Text)),
    };
    if (picker.ShowDialog(this) == DialogResult.OK)
    {
      target.Text = picker.FileName;
    }
  }

  /// <summary>The <c>import</c> options of one tool, for its help.</summary>
  private sealed record CliHelp(ImportOptions Import, ImportTargetOptions Target)
  {
    public static implicit operator CliHelp(VbaCli.CliDefinition cli) => new(cli.Import, cli.ImportTarget);

    public static implicit operator CliHelp(PqCli.CliDefinition cli) => new(cli.Import, cli.ImportTarget);
  }
}
