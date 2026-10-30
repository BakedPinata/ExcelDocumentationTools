using System.Buffers.Binary;
using System.Text;

namespace VbaDocumenter.Core.Extraction;

/// <summary>A module entry from the dir stream.</summary>
public sealed record DirModule(string Name, string StreamName, uint TextOffset, bool IsProcedural);

/// <summary>What the dir stream tells us about the project.</summary>
public sealed record DirInfo(string ProjectName, int CodePage, IReadOnlyList<DirModule> Modules);

/// <summary>
/// Parses the decompressed "VBA/dir" stream (MS-OVBA section 2.3.4.2).
/// </summary>
/// <remarks>
/// The stream is a flat list of records: a 2-byte id, a 4-byte size, then that many bytes of data.
/// We only read the handful of records we need and skip the rest by their size. The one exception
/// is PROJECTVERSION (0x0009), whose size field says 4 but whose data is actually 6 bytes.
/// </remarks>
public static class DirStreamParser
{
    const ushort ProjectCodePage = 0x0003;
    const ushort ProjectName = 0x0004;
    const ushort ProjectVersion = 0x0009;
    const ushort Terminator = 0x0010;
    const ushort ModuleName = 0x0019;
    const ushort ModuleStreamName = 0x001A;
    const ushort ModuleTypeProcedural = 0x0021;
    const ushort ModuleTerminator = 0x002B;
    const ushort ModuleOffset = 0x0031;
    const ushort ModuleStreamNameUnicode = 0x0032;
    const ushort ModuleNameUnicode = 0x0047;

    public static DirInfo Parse(byte[] dir)
    {
        string projectName = "";
        int codePage = 1252;
        var modules = new List<DirModule>();
        ModuleBuilder? current = null;
        int pos = 0;

        while (pos + 6 <= dir.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(pos));
            int size = BinaryPrimitives.ReadInt32LittleEndian(dir.AsSpan(pos + 2));
            pos += 6;
            if (id == ProjectVersion) size = 6;
            if (size < 0 || pos + size > dir.Length)
                throw new InvalidDataException($"dir record 0x{id:X4} at offset {pos - 6} runs past the end of the stream.");
            var data = dir.AsSpan(pos, size);

            switch (id)
            {
                case ProjectCodePage:
                    codePage = BinaryPrimitives.ReadUInt16LittleEndian(data);
                    break;
                case ProjectName:
                    projectName = CodePages.Get(codePage).GetString(data);
                    break;
                case ModuleName:
                    // The probe decoded this as Latin-1; the spec says it uses the project's code page.
                    current = new ModuleBuilder { Name = CodePages.Get(codePage).GetString(data) };
                    break;
                case ModuleNameUnicode when current != null:
                    current.Name = Encoding.Unicode.GetString(data);
                    break;
                case ModuleStreamName when current != null:
                    current.StreamName = CodePages.Get(codePage).GetString(data);
                    break;
                case ModuleStreamNameUnicode when current != null:
                    current.StreamName = Encoding.Unicode.GetString(data);
                    break;
                case ModuleOffset when current != null:
                    current.TextOffset = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    break;
                case ModuleTypeProcedural when current != null:
                    current.IsProcedural = true;
                    break;
                case ModuleTerminator when current != null:
                    modules.Add(current.Build());
                    current = null;
                    break;
                case Terminator:
                    return new DirInfo(projectName, codePage, modules);
            }
            pos += size;
        }

        throw new InvalidDataException("The dir stream ended without its terminator record.");
    }

    sealed class ModuleBuilder
    {
        public string Name = "";
        public string StreamName = "";
        public uint TextOffset;
        public bool IsProcedural;

        public DirModule Build() => new(Name, StreamName == "" ? Name : StreamName, TextOffset, IsProcedural);
    }
}
