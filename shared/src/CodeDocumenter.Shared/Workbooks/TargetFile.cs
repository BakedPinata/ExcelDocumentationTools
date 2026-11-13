using OpenMcdf;

namespace CodeDocumenter.Shared.Workbooks;

/// <summary>Reading a workbook to write into: what can be a target, and why anything else can't.</summary>
public static class TargetFile
{
  /// <summary>The workbooks both tools can write into, as Excel accepted what they wrote. Each tool's list starts here.</summary>
  public static IReadOnlyList<string> Workbooks { get; } = [".xlsx", ".xlsm", ".xlsb"];

  /// <summary>The target's bytes. It's only read.</summary>
  /// <param name="path">The workbook.</param>
  /// <param name="items">What's written into it, for the messages, such as "modules".</param>
  /// <param name="saveAs">What an older workbook should be saved as, with its dot, such as ".xlsm".</param>
  /// <param name="extensions">What the tool can write into, with their dots. An older add-in is told to become an .xlam only if that's one.</param>
  /// <exception cref="ExtractionException">It can't be a target, and says why.</exception>
  public static byte[] ReadBytes(string path, string items, string saveAs, IReadOnlyList<string> extensions)
  {
    ArgumentNullException.ThrowIfNull(extensions);
    var bytes = WorkbookFile.ReadAllBytes(path);
    var name = Path.GetFileName(path);
    switch (WorkbookFile.ContainerOf(bytes))
    {
      case WorkbookContainer.CompoundFile:
        throw new ExtractionException(WhyNotCompoundFile(bytes, path, items, saveAs, extensions));
      case WorkbookContainer.Unknown:
        throw new ExtractionException($"{name} is not an Excel workbook.");
    }

    if (!Is(path, extensions))
    {
      throw new ExtractionException(NotOneOf(name, items, extensions));
    }

    return bytes;
  }

  private static bool Is(string path, IReadOnlyList<string> extensions) =>
    extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

  private static string NotOneOf(string name, string items, IReadOnlyList<string> extensions) =>
    $"{name} can't be a target: {items} can only be written into {string.Join(", ", extensions)} files.";

  private static string WhyNotCompoundFile(byte[] bytes, string path, string items, string saveAs, IReadOnlyList<string> extensions)
  {
    var name = Path.GetFileName(path);
    try
    {
      using var root = RootStorage.Open(new MemoryStream(bytes));
      if (WorkbookFile.IsEncrypted(root))
      {
        return $"{name} is encrypted (password, IRM or sensitivity label), so nothing can be written into it. Save a copy without encryption and use that.";
      }

      if (Is(path, [".xla"]))
      {
        return extensions.Contains(".xlam", StringComparer.OrdinalIgnoreCase)
          ? $"{name} is an older .xla add-in, which {items} can't be written into. Save it as an Excel Add-in (.xlam) and use that."
          : NotOneOf(name, items, extensions);
      }

      return $"{name} is an older .xls workbook, which {items} can't be written into. Save it as {saveAs} and use that.";
    }
    catch (OpenMcdf.FileFormatException ex)
    {
      return $"{name} is damaged: {ex.Message}";
    }
  }
}
