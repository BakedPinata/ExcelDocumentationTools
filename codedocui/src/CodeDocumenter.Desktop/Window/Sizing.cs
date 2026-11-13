namespace CodeDocumenter.Desktop.Window;

/// <summary>Sizes for controls made in code rather than by a designer.</summary>
internal static class Sizing
{
  /// <summary>
  /// A button as big as its text, with room either side. Windows Forms already scales a new control's default size for
  /// the screen, and the form scales it again, so a button that kept its default size would be twice as tall. A size
  /// worked out from the text, whose font is in points, is right at any scale.
  /// </summary>
  public static Button SizedToText(this Button button)
  {
    button.AutoSize = true;
    button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
    button.Padding = new Padding(12, 2, 12, 2);
    return button;
  }
}

/// <summary>
/// A text box that stretches with the window. Asked how wide it would like to be, it gives its least width rather than
/// the width of its text, so a long path doesn't make the window's least width wider.
/// </summary>
/// <param name="leastWidth">The narrowest it should be, at 96 dpi.</param>
internal sealed class FieldTextBox(int leastWidth) : TextBox
{
  public override Size GetPreferredSize(Size proposedSize) =>
    new(LogicalToDeviceUnits(leastWidth), Multiline ? Height : base.GetPreferredSize(proposedSize).Height);
}

/// <summary>A combo box that stretches with the window, asking for its least width as <see cref="FieldTextBox"/> does.</summary>
/// <param name="leastWidth">The narrowest it should be, at 96 dpi.</param>
internal sealed class FieldComboBox(int leastWidth) : ComboBox
{
  public override Size GetPreferredSize(Size proposedSize) =>
    new(LogicalToDeviceUnits(leastWidth), base.GetPreferredSize(proposedSize).Height);
}
