using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Tests.Support;

/// <summary>A module to put into a synthetic workbook. Source uses any line endings; it is stored with CRLF.</summary>
public sealed record SyntheticModule(string Name, VbaModuleKind Kind, string Source);

/// <summary>
/// Builds small but structurally valid workbooks in memory, so the reader can be tested on cases that
/// no real fixture covers (forms, missing streams, unusual names, ...). The compression only uses
/// literal tokens, which is valid MS-OVBA but never what Excel writes; the real fixtures in
/// Fixtures/Workbooks cover Excel's actual compression.
/// </summary>
public static class SyntheticWorkbook
{
    /// <summary>Bytes of fake compiled code put in front of each module's source, as in real files.</summary>
    const int FakePerformanceCacheSize = 37;

    public static byte[] Xlsm(IEnumerable<SyntheticModule> modules, int codePage = 1252, Action<Storage>? tamper = null)
    {
        var vbaProjectBin = CompoundFile(root => WriteProject(root, modules.ToList(), codePage, tamper));
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(zip, "[Content_Types].xml", "<Types/>"u8.ToArray());
            WriteZipEntry(zip, "xl/vbaProject.bin", vbaProjectBin);
        }
        return zipBytes.ToArray();
    }

    public static byte[] Xls(IEnumerable<SyntheticModule> modules, int codePage = 1252)
    {
        return CompoundFile(root =>
        {
            using (var workbook = root.CreateStream("Workbook")) workbook.Write(new byte[16]);
            WriteProject(root.CreateStorage("_VBA_PROJECT_CUR"), modules.ToList(), codePage, tamper: null);
        });
    }

    /// <summary>A compound file with an EncryptedPackage stream, which is how encrypted workbooks look from outside.</summary>
    public static byte[] Encrypted() => CompoundFile(root =>
    {
        using var stream = root.CreateStream("EncryptedPackage");
        stream.Write(new byte[64]);
    });

    /// <summary>Compresses using only literal tokens. Enough for small test modules.</summary>
    public static byte[] Compress(byte[] data)
    {
        // A compressed chunk holds at most 4098 bytes; literal-only costs 9 bytes per 8 of input.
        const int maxPerChunk = 3640;
        var output = new List<byte> { 0x01 }; // signature; empty data is just this byte
        for (int start = 0; start < data.Length; start += maxPerChunk)
        {
            var chunk = data.AsSpan(start, Math.Min(maxPerChunk, data.Length - start));
            var body = new List<byte>();
            for (int i = 0; i < chunk.Length; i += 8)
            {
                body.Add(0x00); // flag byte: next 8 tokens are literals
                for (int j = i; j < Math.Min(i + 8, chunk.Length); j++) body.Add(chunk[j]);
            }
            int header = (body.Count + 2 - 3) | 0b011 << 12 | 0x8000;
            output.Add((byte)header);
            output.Add((byte)(header >> 8));
            output.AddRange(body);
        }
        return output.ToArray();
    }

    static void WriteProject(Storage projectStorage, List<SyntheticModule> modules, int codePage, Action<Storage>? tamper)
    {
        var encoding = CodePages.Get(codePage);
        var vba = projectStorage.CreateStorage("VBA");

        WriteStream(vba, "dir", Compress(DirStream(modules, codePage)));
        foreach (var m in modules)
        {
            var source = encoding.GetBytes(m.Source.ReplaceLineEndings("\r\n"));
            WriteStream(vba, m.Name, [.. new byte[FakePerformanceCacheSize], .. Compress(source)]);
        }

        var project = new StringBuilder("ID=\"{00000000-0000-0000-0000-000000000000}\"\r\n");
        foreach (var m in modules)
        {
            project.Append(m.Kind switch
            {
                VbaModuleKind.Standard => $"Module={m.Name}\r\n",
                VbaModuleKind.Class => $"Class={m.Name}\r\n",
                VbaModuleKind.Form => $"BaseClass={m.Name}\r\n",
                _ => $"Document={m.Name}/&H00000000\r\n",
            });
        }
        project.Append("Name=\"VBAProject\"\r\n\r\n[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n");
        WriteStream(projectStorage, "PROJECT", encoding.GetBytes(project.ToString()));

        tamper?.Invoke(vba);
    }

    /// <summary>A minimal dir stream: just the records the reader looks at, plus the PROJECTVERSION quirk.</summary>
    public static byte[] DirStream(IReadOnlyList<SyntheticModule> modules, int codePage)
    {
        var encoding = CodePages.Get(codePage);
        var dir = new List<byte>();
        Record(dir, 0x0001, [1, 0, 0, 0]);                                // PROJECTSYSKIND: Win32
        Record(dir, 0x0003, [(byte)codePage, (byte)(codePage >> 8)]);     // PROJECTCODEPAGE
        Record(dir, 0x0004, encoding.GetBytes("VBAProject"));             // PROJECTNAME
        // PROJECTVERSION: the size field says 4, but 6 bytes of data follow.
        Add16(dir, 0x0009); Add32(dir, 4); dir.AddRange(new byte[6]);
        Record(dir, 0x000F, [(byte)modules.Count, 0]);                    // PROJECTMODULES
        Record(dir, 0x0013, [0xFF, 0xFF]);                                // PROJECTCOOKIE
        foreach (var m in modules)
        {
            Record(dir, 0x0019, encoding.GetBytes(m.Name));               // MODULENAME
            Record(dir, 0x0047, Encoding.Unicode.GetBytes(m.Name));       // MODULENAMEUNICODE
            Record(dir, 0x001A, encoding.GetBytes(m.Name));               // MODULESTREAMNAME
            Record(dir, 0x0032, Encoding.Unicode.GetBytes(m.Name));       // MODULESTREAMNAMEUNICODE
            Record(dir, 0x0031, BitConverter.GetBytes((uint)FakePerformanceCacheSize)); // MODULEOFFSET
            Record(dir, m.Kind == VbaModuleKind.Standard ? (ushort)0x0021 : (ushort)0x0022, []); // MODULETYPE
            Record(dir, 0x002B, []);                                      // module terminator
        }
        Record(dir, 0x0010, []);                                          // dir terminator
        return dir.ToArray();
    }

    static void Record(List<byte> dir, ushort id, byte[] data)
    {
        Add16(dir, id);
        Add32(dir, data.Length);
        dir.AddRange(data);
    }

    static void Add16(List<byte> b, ushort v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(s, v); b.AddRange(s.ToArray()); }
    static void Add32(List<byte> b, int v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(s, v); b.AddRange(s.ToArray()); }

    static byte[] CompoundFile(Action<RootStorage> fill)
    {
        var buffer = new MemoryStream();
        using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
            fill(root);
        return buffer.ToArray();
    }

    static void WriteStream(Storage storage, string name, byte[] data)
    {
        using var stream = storage.CreateStream(name);
        stream.Write(data);
    }

    static void WriteZipEntry(ZipArchive zip, string name, byte[] data)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(data);
    }
}
