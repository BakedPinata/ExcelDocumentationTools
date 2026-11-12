using System.IO.Compression;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;

namespace VbaDocumenter.Core.Extraction;

/// <summary>
/// Reads the VBA project out of an Excel workbook without Excel. Opening the file, telling which kind it is and
/// spotting encryption are shared with pqdoc (<see cref="WorkbookFile"/>).
/// </summary>
/// <remarks>
/// Zip-based files (.xlsm, .xlsb, .xlam) keep the project in "xl/vbaProject.bin", which is itself a
/// compound file. Legacy .xls/.xla files are compound files with the project in the
/// "_VBA_PROJECT_CUR" storage. Either way we end up with a storage containing the "VBA" storage
/// and the "PROJECT" stream, and read the same way from there.
/// </remarks>
public static class WorkbookReader
{
  public static VbaProject Read(string workbookPath) => Read(WorkbookFile.ReadAllBytes(workbookPath));

  public static VbaProject Read(byte[] fileBytes)
  {
    try
    {
      switch (WorkbookFile.ContainerOf(fileBytes))
      {
        case WorkbookContainer.Zip: return WithSaved(ReadZipBased(fileBytes), fileBytes);
        case WorkbookContainer.CompoundFile: return WithSaved(ReadCompoundFile(fileBytes), fileBytes);
      }
    }
    catch (Exception ex) when (ex is InvalidDataException or OpenMcdf.FileFormatException or EndOfStreamException)
    {
      throw new ExtractionException($"The workbook or its VBA project is corrupt: {ex.Message}", ex);
    }
    throw new ExtractionException("This is not an Excel workbook (it is neither a zip-based nor a compound file).");
  }

  // The saved time is only for the page's summary, so reading it fails safe (WorkbookFile.LastSaved).
  static VbaProject WithSaved(VbaProject project, byte[] fileBytes) =>
      new() { Name = project.Name, CodePage = project.CodePage, Modules = project.Modules, References = project.References, Saved = WorkbookFile.LastSaved(fileBytes) };

  static VbaProject ReadZipBased(byte[] fileBytes)
  {
    using var zip = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read);
    var entry = zip.GetEntry("xl/vbaProject.bin")
        ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
        ?? throw new ExtractionException(IsAddin(fileBytes)
            ? "The add-in has no VBA project (no xl/vbaProject.bin), so it has no modules."
            : "The workbook has no VBA project (no xl/vbaProject.bin). Was it saved as .xlsx?");

    using var entryStream = entry.Open();
    var vbaProjectBin = new MemoryStream();
    entryStream.CopyTo(vbaProjectBin);
    vbaProjectBin.Position = 0;

    using var root = RootStorage.Open(vbaProjectBin);
    return ReadProject(root);
  }

  // Only for what's said when there's no project, so a package that can't say counts as a workbook.
  static bool IsAddin(byte[] fileBytes)
  {
    try
    {
      return OpcPackage.Read(fileBytes).IsAddin;
    }
    catch (Exception ex) when (ex is ExtractionException or InvalidDataException or System.Xml.XmlException)
    {
      return false;
    }
  }

  static VbaProject ReadCompoundFile(byte[] fileBytes)
  {
    using var root = RootStorage.Open(new MemoryStream(fileBytes));

    // Password-protected, IRM-protected and sensitivity-labelled files wrap the whole workbook
    // in an encrypted package, so nothing inside can be read.
    if (WorkbookFile.IsEncrypted(root))
      throw new ExtractionException(
          "The workbook is encrypted (password, IRM or sensitivity label), so its VBA can't be read. " +
          "Save a copy without encryption and run the tool on that.");

    if (!root.TryOpenStorage("_VBA_PROJECT_CUR", out var projectStorage) || projectStorage == null)
      throw new ExtractionException("The workbook has no VBA project (no _VBA_PROJECT_CUR storage).");
    return ReadProject(projectStorage);
  }

  /// <summary>Reads a project from the storage that holds the "VBA" storage and the "PROJECT" stream.</summary>
  static VbaProject ReadProject(Storage projectStorage)
  {
    if (!projectStorage.TryOpenStorage("VBA", out var vba) || vba == null)
      throw new ExtractionException("The VBA project has no VBA storage.");

    var dir = DirStreamParser.Parse(OvbaDecompressor.Decompress(ReadStream(vba, "dir")));
    var encoding = CodePages.Get(dir.CodePage);

    var kinds = projectStorage.ContainsEntry("PROJECT")
        ? ProjectStreamParser.ParseModuleKinds(encoding.GetString(ReadStream(projectStorage, "PROJECT")))
        : [];

    var modules = new List<VbaModule>();
    foreach (var m in dir.Modules)
    {
      // A module we can't read is fatal rather than a warning: exporting the rest would make the
      // exporter delete that module's existing file from the output folder.
      if (!vba.ContainsEntry(m.StreamName))
        throw new ExtractionException($"Module '{m.Name}' is listed in the project but its stream '{m.StreamName}' is missing.");

      byte[] source;
      try
      {
        source = OvbaDecompressor.Decompress(ReadStream(vba, m.StreamName), checked((int)m.TextOffset));
      }
      catch (InvalidDataException ex)
      {
        throw new ExtractionException(
            $"Module '{m.Name}' has no readable source ({ex.Message}). " +
            "The project may contain only compiled code (\"VBA stomping\") or be corrupt.", ex);
      }

      var kind = m.IsProcedural ? VbaModuleKind.Standard : kinds.GetValueOrDefault(m.Name, VbaModuleKind.Class);
      modules.Add(new VbaModule
      {
        Name = m.Name,
        Kind = kind,
        StreamName = m.StreamName,
        SourceBytes = source,
        Source = encoding.GetString(source),
      });
    }

    return new VbaProject
    {
      Name = dir.ProjectName,
      CodePage = dir.CodePage,
      References = dir.References,
      Modules = modules
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList(),
    };
  }

  static byte[] ReadStream(Storage storage, string name)
  {
    using var stream = storage.OpenStream(name);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }
}
