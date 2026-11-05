using System.Buffers.Binary;
using System.Text;

namespace ImportSpike;

/// <summary>
/// The parts of [MS-OVBA] the tools' readers don't need: compression, the dir stream as records that can be written
/// back, and the "encryption" of the PROJECT stream's CMG, DPB and GC values.
/// </summary>
static class Ovba
{
  /// <summary>
  /// Compresses as MS-OVBA 2.4.1.3 describes: chunks of 4096 bytes, each a run of literals and copy tokens that point
  /// back into the same chunk. A full chunk that wouldn't get smaller is stored raw, as the format allows.
  /// </summary>
  public static byte[] Compress(byte[] data)
  {
    var output = new List<byte> { 0x01 }; // signature
    for (int start = 0; start < data.Length; start += 4096)
    {
      int end = Math.Min(start + 4096, data.Length);
      var body = CompressChunk(data, start, end);
      if (body.Count > 4096)
      {
        // Only a full chunk can be stored raw: every chunk but the last decompresses to exactly 4096 bytes.
        if (end - start != 4096) throw new InvalidOperationException("The last chunk doesn't compress into 4096 bytes.");
        int raw = 4095 | 0b011 << 12; // size less 3, signature 0b011, flag 0 (raw)
        output.Add((byte)raw);
        output.Add((byte)(raw >> 8));
        output.AddRange(data.AsSpan(start, 4096).ToArray());
        continue;
      }
      int header = (body.Count + 2 - 3) | 0b011 << 12 | 0x8000;
      output.Add((byte)header);
      output.Add((byte)(header >> 8));
      output.AddRange(body);
    }
    return output.ToArray();
  }

  static List<byte> CompressChunk(byte[] data, int start, int end)
  {
    var body = new List<byte>();
    int current = start;
    while (current < end)
    {
      int flagAt = body.Count;
      body.Add(0);
      byte flags = 0;
      for (int bit = 0; bit < 8 && current < end; bit++)
      {
        // The split between a copy token's offset and length depends on how far into the chunk it is.
        int difference = current - start;
        int bitCount = 4;
        while ((1 << bitCount) < difference) bitCount++;
        int maximumLength = (0xFFFF >> bitCount) + 3;
        int bestLength = 0, bestOffset = 0;
        for (int candidate = current - 1; candidate >= start; candidate--)
        {
          int length = 0;
          while (length < maximumLength && current + length < end && data[candidate + length] == data[current + length]) length++;
          if (length > bestLength) { bestLength = length; bestOffset = current - candidate; }
        }
        if (bestLength >= 3)
        {
          int token = (bestOffset - 1) << (16 - bitCount) | (bestLength - 3);
          body.Add((byte)token);
          body.Add((byte)(token >> 8));
          flags |= (byte)(1 << bit);
          current += bestLength;
        }
        else
        {
          body.Add(data[current++]);
        }
      }
      body[flagAt] = flags;
    }
    return body;
  }

  /// <summary>The "encryption" of CMG, DPB and GC (MS-OVBA 2.4.3.2), as the hex the PROJECT stream holds.</summary>
  public static string Encrypt(byte[] data, byte projectKey, byte seed)
  {
    const byte Version = 2;
    var output = new List<byte>();
    byte versionEnc = (byte)(seed ^ Version);
    byte keyEnc = (byte)(seed ^ projectKey);
    output.Add(seed);
    output.Add(versionEnc);
    output.Add(keyEnc);
    byte unencrypted1 = projectKey, encrypted1 = keyEnc, encrypted2 = versionEnc;
    void Put(byte b)
    {
      byte enc = (byte)(b ^ (byte)(encrypted2 + unencrypted1));
      output.Add(enc);
      encrypted2 = encrypted1;
      encrypted1 = enc;
      unencrypted1 = b;
    }
    for (int i = 0; i < (seed & 6) / 2; i++) Put(0); // ignored bytes: any value
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
    foreach (var b in length) Put(b);
    foreach (var b in data) Put(b);
    return Convert.ToHexString(output.ToArray());
  }

  /// <summary>Reverses <see cref="Encrypt"/> (MS-OVBA 2.4.3.3), giving the key it was made with and the data.</summary>
  public static (byte Key, byte[] Data) Decrypt(string hex)
  {
    var input = Convert.FromHexString(hex);
    byte seed = input[0], versionEnc = input[1], keyEnc = input[2];
    byte key = (byte)(seed ^ keyEnc);
    byte unencrypted1 = key, encrypted1 = keyEnc, encrypted2 = versionEnc;
    int at = 3;
    byte Next()
    {
      byte enc = input[at++];
      byte b = (byte)(enc ^ (byte)(encrypted2 + unencrypted1));
      encrypted2 = encrypted1;
      encrypted1 = enc;
      unencrypted1 = b;
      return b;
    }
    for (int i = 0; i < (seed & 6) / 2; i++) Next();
    Span<byte> length = stackalloc byte[4];
    for (int i = 0; i < 4; i++) length[i] = Next();
    var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
    for (int i = 0; i < data.Length; i++) data[i] = Next();
    return (key, data);
  }

  /// <summary>The project key: the low byte of the sum of the bytes of the project's ID, as written in PROJECT.</summary>
  public static byte ProjectKey(string projectId) => (byte)Encoding.ASCII.GetBytes(projectId).Sum(b => b);

  // ---- The dir stream as records ----

  public sealed class Record(ushort id, byte[] data)
  {
    public ushort Id { get; } = id;
    public byte[] Data { get; set; } = data;
    public override string ToString() => $"0x{Id:X4} ({Data.Length} bytes)";
  }

  const ushort ProjectVersion = 0x0009;

  /// <summary>
  /// Every record of a decompressed dir stream, in order. Each is a 2-byte id, a 4-byte size and its data, apart from
  /// PROJECTVERSION, whose size field says 4 but whose data is 6 bytes. Records the spec nests (a reference's extended
  /// part, a module's Unicode stream name) are themselves in that shape, so they come out as records too.
  /// </summary>
  public static List<Record> ReadDir(byte[] dir)
  {
    var records = new List<Record>();
    int pos = 0;
    while (pos + 6 <= dir.Length)
    {
      ushort id = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(pos));
      int size = id == ProjectVersion ? 6 : BinaryPrimitives.ReadInt32LittleEndian(dir.AsSpan(pos + 2));
      pos += 6;
      records.Add(new Record(id, dir.AsSpan(pos, size).ToArray()));
      pos += size;
      if (id == 0x0010) break; // the dir terminator
    }
    return records;
  }

  public static byte[] WriteDir(IEnumerable<Record> records)
  {
    var output = new List<byte>();
    Span<byte> buffer = stackalloc byte[4];
    foreach (var r in records)
    {
      BinaryPrimitives.WriteUInt16LittleEndian(buffer, r.Id);
      output.AddRange(buffer[..2].ToArray());
      BinaryPrimitives.WriteInt32LittleEndian(buffer, r.Id == ProjectVersion ? 4 : r.Data.Length);
      output.AddRange(buffer.ToArray());
      output.AddRange(r.Data);
    }
    return output.ToArray();
  }

  // ---- PROJECTwm: each module's name in the code page and in UTF-16 ----

  public static List<string> ReadNameMap(byte[] wm)
  {
    var names = new List<string>();
    int pos = 0;
    while (pos < wm.Length && wm[pos] != 0)
    {
      while (wm[pos] != 0) pos++; // the name in the code page
      pos++;
      int start = pos;
      while (pos + 1 < wm.Length && (wm[pos] != 0 || wm[pos + 1] != 0)) pos += 2;
      names.Add(Encoding.Unicode.GetString(wm, start, pos - start));
      pos += 2;
    }
    return names;
  }

  public static byte[] WriteNameMap(IEnumerable<string> names, Encoding encoding)
  {
    var output = new List<byte>();
    foreach (var name in names)
    {
      output.AddRange(encoding.GetBytes(name));
      output.Add(0);
      output.AddRange(Encoding.Unicode.GetBytes(name));
      output.Add(0);
      output.Add(0);
    }
    output.Add(0);
    output.Add(0);
    return output.ToArray();
  }
}
