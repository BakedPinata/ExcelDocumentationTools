using System.Buffers.Binary;

namespace VbaDocumenter.Core.Importing;

/// <summary>One record of a dir stream.</summary>
/// <param name="id">The record's id.</param>
/// <param name="data">What it holds.</param>
public sealed class DirRecord(ushort id, byte[] data)
{
  /// <summary>The record's id.</summary>
  public ushort Id { get; } = id;

  /// <summary>What it holds.</summary>
  public byte[] Data { get; set; } = data;
}

/// <summary>
/// The dir stream as records that can be changed and written back, which <see cref="Extraction.DirStreamParser"/> doesn't
/// need. Each is a 2-byte id, a 4-byte size and its data, apart from PROJECTVERSION, whose size field says 4 but whose
/// data is 6 bytes. What the spec nests (a reference's extended part, a module's Unicode stream name) has the same shape,
/// so it comes out as records too.
/// </summary>
public static class DirRecords
{
  const ushort ProjectVersion = 0x0009;
  const ushort Terminator = 0x0010;

  /// <summary>Every record of a decompressed dir stream, or of part of one, in order.</summary>
  /// <exception cref="InvalidDataException">A record runs past the end.</exception>
  public static List<DirRecord> Read(byte[] dir)
  {
    ArgumentNullException.ThrowIfNull(dir);
    var records = new List<DirRecord>();
    int pos = 0;
    while (pos + 6 <= dir.Length)
    {
      ushort id = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(pos));
      int size = id == ProjectVersion ? 6 : BinaryPrimitives.ReadInt32LittleEndian(dir.AsSpan(pos + 2));
      pos += 6;
      if (size < 0 || pos + size > dir.Length)
        throw new InvalidDataException($"dir record 0x{id:X4} at offset {pos - 6} runs past the end of the stream.");
      records.Add(new DirRecord(id, dir.AsSpan(pos, size).ToArray()));
      pos += size;
      if (id == Terminator) break;
    }

    return records;
  }

  /// <summary>The records as a dir stream, before compression.</summary>
  public static byte[] Write(IEnumerable<DirRecord> records)
  {
    ArgumentNullException.ThrowIfNull(records);
    var output = new List<byte>();
    Span<byte> buffer = stackalloc byte[4];
    foreach (var record in records)
    {
      BinaryPrimitives.WriteUInt16LittleEndian(buffer, record.Id);
      output.AddRange(buffer[..2]);
      BinaryPrimitives.WriteInt32LittleEndian(buffer, record.Id == ProjectVersion ? 4 : record.Data.Length);
      output.AddRange(buffer);
      output.AddRange(record.Data);
    }

    return output.ToArray();
  }
}
