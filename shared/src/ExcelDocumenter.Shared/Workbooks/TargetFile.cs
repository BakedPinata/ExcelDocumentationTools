using OpenMcdf;

namespace ExcelDocumenter.Shared.Workbooks;

/// <summary>Reading a workbook to write into: what can be a target, and why anything else can't.</summary>
public static class TargetFile
{
  private static readonly string[] Extensions = [".xlsx", ".xlsm", ".xlsb"];

  /// <summary>The target's bytes. It's only read.</summary>
  /// <param name="path">The workbook.</param>
  /// <param name="items">What's written into it, for the messages, such as "modules".</param>
  /// <param name="saveAs">What an older workbook should be saved as, with its dot, such as ".xlsm".</param>
  /// <exception cref="ExtractionException">It can't be a target, and says why.</exception>
  public static byte[] ReadBytes(string path, string items, string saveAs)
  {
    var bytes = WorkbookFile.ReadAllBytes(path);
    var name = Path.GetFileName(path);
    switch (WorkbookFile.ContainerOf(bytes))
    {
      case WorkbookContainer.CompoundFile:
        throw new ExtractionException(WhyNotCompoundFile(bytes, name, items, saveAs));
      case WorkbookContainer.Unknown:
        throw new ExtractionException($"{name} is not an Excel workbook.");
    }

    if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
    {
      throw new ExtractionException($"{name} can't be a target: {items} can only be written into {string.Join(", ", Extensions)} files.");
    }

    return bytes;
  }

  private static string WhyNotCompoundFile(byte[] bytes, string name, string items, string saveAs)
  {
    try
    {
      using var root = RootStorage.Open(new MemoryStream(bytes));
      return WorkbookFile.IsEncrypted(root)
        ? $"{name} is encrypted (password, IRM or sensitivity label), so nothing can be written into it. Save a copy without encryption and use that."
        : $"{name} is an older .xls workbook, which {items} can't be written into. Save it as {saveAs} and use that.";
    }
    catch (OpenMcdf.FileFormatException ex)
    {
      return $"{name} is damaged: {ex.Message}";
    }
  }
}
