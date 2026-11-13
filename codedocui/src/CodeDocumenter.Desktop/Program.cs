using CodeDocumenter.Desktop.Settings;
using CodeDocumenter.Desktop.Window;

namespace CodeDocumenter.Desktop;

internal static class Program
{
  /// <summary>Opens the window. A workbook or folder given on the command line is chosen to start with.</summary>
  [STAThread]
  private static void Main(string[] args)
  {
    ApplicationConfiguration.Initialize();
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    Application.ThreadException += (_, e) =>
      MessageBox.Show($"Something went wrong in codedocui: {e.Exception.Message}", "codedocui", MessageBoxButtons.OK, MessageBoxIcon.Error);

    using var window = new MainWindow(SettingsStore.DefaultPath, args.Length > 0 ? args[0] : null);
    Application.Run(window);
  }
}
