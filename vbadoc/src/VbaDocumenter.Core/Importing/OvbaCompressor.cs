namespace VbaDocumenter.Core.Importing;

/// <summary>
/// Compresses as MS-OVBA 2.4.1.3 describes: chunks of 4096 bytes, each a run of literals and copy tokens that point back
/// into the same chunk. It has to be the real thing: every chunk but the last must decompress to exactly 4096 bytes, and
/// literals alone take more room than a chunk has.
/// </summary>
public static class OvbaCompressor
{
  const int ChunkSize = 4096;
  const byte Signature = 0x01;

  /// <summary>Compresses the data.</summary>
  public static byte[] Compress(byte[] data)
  {
    ArgumentNullException.ThrowIfNull(data);
    var output = new List<byte> { Signature };
    for (int start = 0; start < data.Length; start += ChunkSize)
    {
      int end = Math.Min(start + ChunkSize, data.Length);
      var body = CompressChunk(data, start, end);
      if (body.Count > ChunkSize)
      {
        // What won't compress is stored raw, which is always a full chunk: 2.4.1.3.10 pads a short last one with zeros.
        int raw = ChunkSize - 1 | 0b011 << 12; // size less 3, signature 0b011, flag 0 (raw)
        output.Add((byte)raw);
        output.Add((byte)(raw >> 8));
        output.AddRange(data.AsSpan(start, end - start));
        output.AddRange(new byte[ChunkSize - (end - start)]);
        continue;
      }

      int header = body.Count + 2 - 3 | 0b011 << 12 | 0x8000;
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
        while (1 << bitCount < difference) bitCount++;
        int maximumLength = (0xFFFF >> bitCount) + 3;

        int bestLength = 0, bestOffset = 0;
        for (int candidate = current - 1; candidate >= start; candidate--)
        {
          int length = 0;
          while (length < maximumLength && current + length < end && data[candidate + length] == data[current + length]) length++;
          if (length > bestLength)
          {
            bestLength = length;
            bestOffset = current - candidate;
          }
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
}
