namespace DesktopSpike;

internal static class Program
{
  /// <summary>
  /// Runs the window. With <c>--report-to &lt;file&gt;</c> it writes its report there as soon as the window has shown,
  /// and closes, so start-up can be timed with nobody at the machine.
  /// </summary>
  [STAThread]
  private static void Main(string[] args)
  {
    // First, so that nothing Windows Forms puts in the temporary folder goes unseen.
    using var tempFolder = new TempFolderWatch();

    ApplicationConfiguration.Initialize();

    var reportTo = args.Length == 2 && args[0] == "--report-to" ? args[1] : null;
    using var window = new SpikeWindow(tempFolder, reportTo);
    Application.Run(window);
  }
}
