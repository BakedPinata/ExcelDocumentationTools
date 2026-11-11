namespace ExcelDocumenter.Desktop.Window;

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
