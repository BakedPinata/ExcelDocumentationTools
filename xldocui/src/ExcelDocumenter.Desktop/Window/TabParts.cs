namespace ExcelDocumenter.Desktop.Window;

/// <summary>
/// A tab's rows: a caption, a field that stretches with the window, and a button, laid out by a table so they're right
/// at any screen scale.
/// </summary>
internal sealed class TabGrid
{
  private int tabIndex;

  public TabGrid()
  {
    Panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    Panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
  }

  public TableLayoutPanel Panel { get; } = new()
  {
    AutoSize = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink,
    ColumnCount = 3,
    Dock = DockStyle.Top,
  };

  /// <summary>The narrowest the rows can be and show everything they show now, with each field at its least width.</summary>
  public int LeastWidth => Panel.GetPreferredSize(Size.Empty).Width + Panel.Margin.Horizontal;

  /// <summary>Adds a row: a caption, a field across the rest or the middle, and a button. Returns its controls.</summary>
  public List<Control> Add(Control? caption, Control field, Control? button = null)
  {
    var row = Panel.RowCount++;
    Panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    var controls = new List<Control>();
    if (caption is not null)
    {
      Place(caption, 0, row);
      controls.Add(caption);
    }

    Place(field, caption is null ? 0 : 1, row);
    Panel.SetColumnSpan(field, (caption is null ? 3 : 2) - (button is null ? 0 : 1));
    controls.Add(field);
    if (button is not null)
    {
      Place(button, 2, row);
      controls.Add(button);
    }

    return controls;
  }

  private void Place(Control control, int column, int row)
  {
    Panel.Controls.Add(control, column, row);
    control.TabIndex = tabIndex++;
  }
}

/// <summary>The controls the tabs are made of, the same on each.</summary>
internal static class TabParts
{
  public static Label Caption(string text, bool indent = false) => new()
  {
    Text = text,
    AutoSize = true,
    Anchor = AnchorStyles.Left,
    Margin = new Padding(indent ? 24 : 3, 6, 3, 6),
  };

  public static Label Note(string text = "", bool visible = false) => new()
  {
    Text = text,
    AutoSize = true,
    Anchor = AnchorStyles.Left,
    ForeColor = SystemColors.GrayText,
    Visible = visible,
  };

  public static TextBox Field(string name) => new FieldTextBox(200)
  {
    Anchor = AnchorStyles.Left | AnchorStyles.Right,
    AccessibleName = name,
  };

  public static Button MakeButton(string text, string name) => new Button
  {
    Text = text,
    AccessibleName = name,
  }.SizedToText();

  public static FlowLayoutPanel Flow(params Control[] controls)
  {
    var flow = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Anchor = AnchorStyles.Left,
      Margin = Padding.Empty,
      // A flow that may wrap says it's as tall as it would be wrapped, which leaves gaps; the window is kept wide
      // enough instead.
      WrapContents = false,
    };
    foreach (var control in controls)
    {
      if (control is Label)
      {
        control.Margin = new Padding(12, 6, 0, 3);
      }

      flow.Controls.Add(control);
    }

    return flow;
  }

  /// <summary>Choices with the note on why they're off, a little apart from the rows above.</summary>
  public static FlowLayoutPanel Heading(Label note, params Control[] choices)
  {
    var flow = Flow([.. choices, note]);
    flow.Margin = new Padding(0, 10, 0, 0);
    note.Margin = new Padding(6, 4, 0, 0);
    return flow;
  }

  public static ComboBox Choices<T>(string name, params Choice<T>[] choices)
  {
    var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
    box.Items.AddRange(choices.Cast<object>().ToArray());
    box.SelectedIndex = 0;
    return box;
  }

  public static void SelectChoice<T>(ComboBox box, T value) =>
    box.SelectedItem = box.Items.Cast<Choice<T>>().FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value))
      ?? box.Items[0];

  public static T Selected<T>(ComboBox box) => (box.SelectedItem ?? box.Items[0]) is Choice<T> choice
    ? choice.Value
    : throw new InvalidOperationException($"{box.AccessibleName} has no choices.");

  /// <summary>The folder a path is in, or the folder itself, for a picker to start in; empty if there's none.</summary>
  public static string FolderOf(string path)
  {
    if (path.Length == 0)
    {
      return "";
    }

    if (Directory.Exists(path))
    {
      return path;
    }

    var parent = Path.GetDirectoryName(path);
    return parent is not null && Directory.Exists(parent) ? parent : "";
  }

  /// <summary>A choice in a list, shown by its label.</summary>
  public sealed record Choice<T>(string Label, T Value)
  {
    public override string ToString() => Label;
  }
}
