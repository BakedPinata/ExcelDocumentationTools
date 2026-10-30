using System.Text;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Extraction;

public class OvbaDecompressorTests
{
    static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));
    static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    // The three examples from MS-OVBA section 3.2, compressed by Microsoft's own implementation.

    [Fact]
    public void Spec_example_no_compression()
    {
        var compressed = Hex("01 19 B0 00 61 62 63 64 65 66 67 68 00 69 6A 6B 6C 6D 6E 6F 70 00 71 72 73 74 75 76 2E");

        Assert.Equal("abcdefghijklmnopqrstuv.", Ascii(OvbaDecompressor.Decompress(compressed)));
    }

    [Fact]
    public void Spec_example_normal_compression()
    {
        var compressed = Hex(
            "01 2F B0 00 23 61 61 61 62 63 64 65 82 66 00 70 61 67 68 69 6A 01 38 08 61 6B 6C 00 20 6D 6E 6F " +
            "70 06 71 02 70 04 00 72 73 74 75 76 10 77 78 79 7A 00 2C");

        Assert.Equal("#aaabcdefaaaaghijaaaaaklaaamnopqaaaaaaaaaaaarstuvwxyzaaa", Ascii(OvbaDecompressor.Decompress(compressed)));
    }

    [Fact]
    public void Spec_example_maximum_compression()
    {
        var compressed = Hex("01 03 B0 02 61 45 00");

        Assert.Equal(new string('a', 73), Ascii(OvbaDecompressor.Decompress(compressed)));
    }

    [Fact]
    public void Starts_at_the_given_offset()
    {
        // Module streams put compiled code before the compressed source; MODULEOFFSET says where the source starts.
        var compressed = Hex("FF FF FF 01 03 B0 02 61 45 00");

        Assert.Equal(73, OvbaDecompressor.Decompress(compressed, offset: 3).Length);
    }

    [Fact]
    public void Reads_uncompressed_chunks()
    {
        // Header 0x3FFF: size 4098, signature 0b011, "compressed" flag clear, so 4096 raw bytes follow.
        var raw = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        byte[] container = [0x01, 0xFF, 0x3F, .. raw];

        Assert.Equal(raw, OvbaDecompressor.Decompress(container));
    }

    [Fact]
    public void Round_trips_data_spanning_several_chunks()
    {
        var text = string.Concat(Enumerable.Range(0, 900).Select(i => $"line {i}\r\n"));
        var bytes = Encoding.ASCII.GetBytes(text);

        Assert.Equal(bytes, OvbaDecompressor.Decompress(SyntheticWorkbook.Compress(bytes)));
    }

    [Fact]
    public void Rejects_a_missing_signature_byte()
    {
        var ex = Assert.Throws<InvalidDataException>(() => OvbaDecompressor.Decompress(Hex("00 03 B0 02 61 45 00")));
        Assert.Contains("signature", ex.Message);
    }

    [Fact]
    public void Rejects_a_copy_token_that_points_before_the_chunk()
    {
        // Flag byte 0x01 says the first token is a copy token, but nothing has been decompressed yet.
        Assert.Throws<InvalidDataException>(() => OvbaDecompressor.Decompress(Hex("01 02 B0 01 00 00")));
    }

    [Fact]
    public void Rejects_a_truncated_chunk_header()
    {
        Assert.Throws<InvalidDataException>(() => OvbaDecompressor.Decompress(Hex("01 03")));
    }
}
