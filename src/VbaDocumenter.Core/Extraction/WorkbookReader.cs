using System.IO.Compression;
using OpenMcdf;

namespace VbaDocumenter.Core.Extraction;

/// <summary>
/// Reads the VBA project out of an Excel workbook without Excel.
/// </summary>
/// <remarks>
/// Zip-based files (.xlsm, .xlsb, .xlam) keep the project in "xl/vbaProject.bin", which is itself a
/// compound file. Legacy .xls/.xla files are compound files with the project in the
/// "_VBA_PROJECT_CUR" storage. Either way we end up with a storage containing the "VBA" storage
/// and the "PROJECT" stream, and read the same way from there.
/// </remarks>
public static class WorkbookReader
{
    public static VbaProject Read(string workbookPath)
    {
        byte[] bytes;
        try
        {
            // FileShare.ReadWrite lets us read the file while Excel has it open.
            using var file = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            bytes = memory.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExtractionException($"Can't read {workbookPath}: {ex.Message}", ex);
        }
        return Read(bytes);
    }

    public static VbaProject Read(byte[] fileBytes)
    {
        try
        {
            if (IsZip(fileBytes)) return ReadZipBased(fileBytes);
            if (IsCompoundFile(fileBytes)) return ReadCompoundFile(fileBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or OpenMcdf.FileFormatException or EndOfStreamException)
        {
            throw new ExtractionException($"The workbook or its VBA project is corrupt: {ex.Message}", ex);
        }
        throw new ExtractionException("This is not an Excel workbook (it is neither a zip-based nor a compound file).");
    }

    static VbaProject ReadZipBased(byte[] fileBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("xl/vbaProject.bin")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            ?? throw new ExtractionException("The workbook has no VBA project (no xl/vbaProject.bin). Was it saved as .xlsx?");

        using var entryStream = entry.Open();
        var vbaProjectBin = new MemoryStream();
        entryStream.CopyTo(vbaProjectBin);
        vbaProjectBin.Position = 0;

        using var root = RootStorage.Open(vbaProjectBin);
        return ReadProject(root);
    }

    static VbaProject ReadCompoundFile(byte[] fileBytes)
    {
        using var root = RootStorage.Open(new MemoryStream(fileBytes));

        // Password-protected, IRM-protected and sensitivity-labelled files wrap the whole workbook
        // in an encrypted package, so nothing inside can be read.
        if (root.ContainsEntry("EncryptedPackage"))
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

    static bool IsZip(byte[] b) => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;

    static bool IsCompoundFile(byte[] b) =>
        b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0
                      && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1;
}
