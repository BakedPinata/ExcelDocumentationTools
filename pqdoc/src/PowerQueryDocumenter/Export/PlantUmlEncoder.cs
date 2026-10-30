using System.IO.Compression;
using System.Text;

namespace PowerQueryDocumenter.Export;

/// <summary>
/// PlantUML's text encoding for diagram URLs (FR-PUML-5): the UTF-8 diagram text is compressed with
/// raw DEFLATE (no zlib header) and then encoded with PlantUML's own base64 variant, which uses the
/// alphabet <c>0-9 A-Z a-z - _</c> and pads a final partial group with zero bits instead of '='.
/// See https://plantuml.com/text-encoding.
/// </summary>
public static class PlantUmlEncoder
{
  private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

  /// <summary>Compresses and encodes diagram text for use in a <c>/{format}/{encoded}</c> URL.</summary>
  public static string Encode(string diagram)
  {
    ArgumentNullException.ThrowIfNull(diagram);

    using var compressed = new MemoryStream();
    using (var deflate = new DeflateStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
    {
      var bytes = Encoding.UTF8.GetBytes(diagram);
      deflate.Write(bytes, 0, bytes.Length);
    }

    return Encode64(compressed.ToArray());
  }

  /// <summary>Reverses <see cref="Encode"/>.</summary>
  /// <exception cref="FormatException">The text contains a character outside the PlantUML alphabet.</exception>
  public static string Decode(string encoded)
  {
    ArgumentNullException.ThrowIfNull(encoded);

    using var compressed = new MemoryStream(Decode64(encoded));
    using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
    using var reader = new StreamReader(deflate, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  /// <summary>Encodes bytes with PlantUML's base64 variant: every 3 bytes become 4 characters.</summary>
  public static string Encode64(ReadOnlySpan<byte> data)
  {
    var text = new StringBuilder((data.Length + 2) / 3 * 4);
    for (var i = 0; i < data.Length; i += 3)
    {
      var b1 = data[i];
      var b2 = i + 1 < data.Length ? data[i + 1] : (byte)0;
      var b3 = i + 2 < data.Length ? data[i + 2] : (byte)0;

      text.Append(Alphabet[b1 >> 2]);
      text.Append(Alphabet[((b1 & 0x3) << 4) | (b2 >> 4)]);
      text.Append(Alphabet[((b2 & 0xF) << 2) | (b3 >> 6)]);
      text.Append(Alphabet[b3 & 0x3F]);
    }

    return text.ToString();
  }

  /// <summary>Decodes PlantUML base64. Trailing zero padding produced by <see cref="Encode64"/> is kept.</summary>
  /// <exception cref="FormatException">The text contains a character outside the PlantUML alphabet.</exception>
  public static byte[] Decode64(string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    var bytes = new List<byte>(text.Length * 3 / 4);

    for (var i = 0; i < text.Length; i += 4)
    {
      var c1 = Value(text, i);
      var c2 = Value(text, i + 1);
      var c3 = Value(text, i + 2);
      var c4 = Value(text, i + 3);

      bytes.Add((byte)((c1 << 2) | (c2 >> 4)));
      bytes.Add((byte)(((c2 & 0xF) << 4) | (c3 >> 2)));
      bytes.Add((byte)(((c3 & 0x3) << 6) | c4));
    }

    return [.. bytes];
  }

  private static int Value(string text, int index)
  {
    if (index >= text.Length)
    {
      return 0;
    }

    var value = Alphabet.IndexOf(text[index], StringComparison.Ordinal);
    return value >= 0
      ? value
      : throw new FormatException($"'{text[index]}' at position {index} is not a PlantUML base64 character.");
  }
}
