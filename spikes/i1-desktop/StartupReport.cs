using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSpike;

/// <summary>What step I1 asks: how big, how quick to start, and whether anything was unpacked or left beside it.</summary>
internal static class StartupReport
{
  public static string Describe(TimeSpan? toWindow, TempFolderWatch tempFolder)
  {
    var executable = Environment.ProcessPath ?? "(unknown)";
    var folder = Path.GetDirectoryName(executable) ?? "";

    var report = new StringBuilder();
    report.AppendLine($"Executable:        {executable}");
    report.AppendLine($"Size:              {new FileInfo(executable).Length / 1_048_576.0:F1} MB");
    report.AppendLine($".NET:              {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.RuntimeIdentifier}");
    report.AppendLine($"Windows:           {RuntimeInformation.OSDescription}");
    if (toWindow is { } elapsed)
    {
      report.AppendLine($"Start to window:   {elapsed.TotalMilliseconds:F0} ms");
    }

    // Not the executable's folder if the program was unpacked to run.
    report.AppendLine($"Base directory:    {AppContext.BaseDirectory}");

    Section(report, "Beside the executable", Directory.GetFiles(folder).Select(Path.GetFileName)!);
    Section(report, "Loaded from outside Windows", ModulesOutsideWindows(executable));
    Section(report, "Unpacked to .net in the temporary folder", UnpackedFolders());
    Section(report, "Created in the temporary folder since starting", tempFolder.Created);
    return report.ToString();
  }

  private static void Section(StringBuilder report, string heading, IEnumerable<string> lines)
  {
    var items = lines.Order(StringComparer.OrdinalIgnoreCase).ToList();
    report.AppendLine();
    report.AppendLine(items.Count == 0 ? $"{heading}: none" : $"{heading}:");
    foreach (var item in items)
    {
      report.AppendLine($"  {item}");
    }
  }

  private static IEnumerable<string> ModulesOutsideWindows(string executable)
  {
    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar;
    using var process = Process.GetCurrentProcess();
    return process.Modules.Cast<ProcessModule>()
      .Select(module => module.FileName)
      .Where(file => !file.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
      .Where(file => !file.Equals(executable, StringComparison.OrdinalIgnoreCase))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  // .NET's default place to unpack a single-file program, if it's asked to.
  private static IEnumerable<string> UnpackedFolders()
  {
    var extractBase = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
    var root = string.IsNullOrEmpty(extractBase) ? Path.Combine(Path.GetTempPath(), ".net") : extractBase;
    var folder = Path.Combine(root, Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? ""));
    return Directory.Exists(folder) ? Directory.GetFileSystemEntries(folder) : [];
  }
}
