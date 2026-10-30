namespace VbaDocumenter.Core.Extraction;

/// <summary>
/// Decompresses data compressed with the MS-OVBA algorithm (MS-OVBA section 2.4.1).
/// The VBA project uses it for the "dir" stream and for each module's source.
/// </summary>
/// <remarks>
/// A compressed container is a signature byte (0x01) followed by chunks. Each chunk decompresses
/// to at most 4096 bytes and starts with a 2-byte header:
///   bits 0-11  chunk size - 3 (the size includes the header itself)
///   bits 12-14 signature, always 0b011
///   bit 15     1 = compressed, 0 = 4096 raw bytes follow
/// A compressed chunk is a sequence of "token sequences": one flag byte, then 8 tokens. A 0 bit in the
/// flag byte means the token is a literal byte; a 1 bit means it is a 2-byte copy token that repeats
/// earlier output from the same chunk.
/// </remarks>
public static class OvbaDecompressor
{
  const int DecompressedChunkSize = 4096;

  public static byte[] Decompress(ReadOnlySpan<byte> data, int offset = 0)
  {
    if (offset < 0 || offset >= data.Length || data[offset] != 0x01)
      throw new InvalidDataException($"No compressed data at offset {offset}: expected signature byte 0x01.");

    var output = new List<byte>(data.Length * 2);
    int pos = offset + 1;

    while (pos < data.Length)
    {
      if (pos + 2 > data.Length)
        throw new InvalidDataException($"Compressed data ends inside a chunk header at offset {pos}.");

      int header = data[pos] | (data[pos + 1] << 8);
      if (((header >> 12) & 0b111) != 0b011)
        throw new InvalidDataException($"Invalid chunk signature at offset {pos}.");

      bool isCompressed = (header & 0x8000) != 0;
      int chunkEnd = Math.Min(pos + (header & 0x0FFF) + 3, data.Length);
      pos += 2;
      int chunkStart = output.Count;

      if (!isCompressed)
      {
        // An uncompressed chunk always holds exactly 4096 bytes; tolerate a short final one.
        int count = Math.Min(DecompressedChunkSize, data.Length - pos);
        for (int i = 0; i < count; i++) output.Add(data[pos + i]);
        pos += count;
        continue;
      }

      while (pos < chunkEnd)
      {
        byte flags = data[pos++];
        for (int bit = 0; bit < 8 && pos < chunkEnd; bit++)
        {
          if ((flags & (1 << bit)) == 0)
          {
            output.Add(data[pos++]);
            continue;
          }

          if (pos + 2 > chunkEnd)
            throw new InvalidDataException($"Copy token at offset {pos} runs past the end of its chunk.");
          int token = data[pos] | (data[pos + 1] << 8);
          pos += 2;

          // How the 16 bits split between offset and length depends on how much of this
          // chunk has been decompressed so far (MS-OVBA 2.4.1.3.19.1 CopyToken Help).
          int decompressedSoFar = output.Count - chunkStart;
          int offsetBits = 4;
          while ((1 << offsetBits) < decompressedSoFar) offsetBits++;
          int lengthMask = 0xFFFF >> offsetBits;

          int length = (token & lengthMask) + 3;
          int copyOffset = (token >> (16 - offsetBits)) + 1;
          int source = output.Count - copyOffset;
          if (source < chunkStart)
            throw new InvalidDataException($"Copy token at offset {pos - 2} points before the start of its chunk.");

          // Copy one byte at a time: source and destination may overlap (e.g. "aaaa..." runs).
          for (int i = 0; i < length; i++) output.Add(output[source + i]);
        }
      }
    }

    return output.ToArray();
  }
}
