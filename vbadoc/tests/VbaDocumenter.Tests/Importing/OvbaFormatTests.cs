using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

/// <summary>The parts of MS-OVBA only writing needs: compression, the dir stream as records, and PROJECT's encrypted values.</summary>
public sealed class OvbaFormatTests
{
  static readonly string[] Workbooks = ["SimpleMacro.xlsm", "45431.xlsm", "57181.xlsm", "suffix-generator.xlsm"];

  static byte[] RoundTrip(byte[] data) => OvbaDecompressor.Decompress(OvbaCompressor.Compress(data));

  static byte[] Text(int length)
  {
    var line = Encoding.ASCII.GetBytes("Public Sub Log(ByVal message As String)\r\n    Debug.Print message\r\nEnd Sub\r\n");
    return Enumerable.Range(0, length).Select(i => line[i % line.Length]).ToArray();
  }

  static byte[] Noise(int length)
  {
    var data = new byte[length];
    new Random(42).NextBytes(data);
    return data;
  }

  // Each chunk's size, from the headers.
  static List<int> ChunkSizes(byte[] compressed)
  {
    var sizes = new List<int>();
    for (int at = 1; at < compressed.Length;)
    {
      int size = (BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(at)) & 0x0FFF) + 3;
      sizes.Add(size);
      at += size;
    }

    return sizes;
  }

  public static TheoryData<string> WorkbookNames => new(Workbooks);

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(3)]
  [InlineData(4095)]
  [InlineData(4096)]
  [InlineData(4097)]
  [InlineData(8192)]
  [InlineData(50000)]
  public void Text_of_any_length_comes_back(int length)
  {
    Assert.Equal(Text(length), RoundTrip(Text(length)));
  }

  [Theory]
  [MemberData(nameof(WorkbookNames))]
  public void Every_real_module_comes_back_and_any_of_a_size_gets_smaller(string workbook)
  {
    foreach (var module in WorkbookReader.Read(Fixtures.Workbook(workbook)).Modules)
    {
      var compressed = OvbaCompressor.Compress(module.SourceBytes);

      Assert.Equal(module.SourceBytes, OvbaDecompressor.Decompress(compressed));
      Assert.True(module.SourceBytes.Length < 200 || compressed.Length < module.SourceBytes.Length, $"{module.Name} grew");
    }
  }

  [Fact]
  public void Data_is_split_into_a_chunk_per_4096_bytes_as_the_format_requires()
  {
    Assert.Equal(3, ChunkSizes(OvbaCompressor.Compress(Text(9000))).Count);
  }

  [Fact]
  public void A_full_chunk_that_wont_compress_is_stored_raw()
  {
    var data = Noise(8192);

    var compressed = OvbaCompressor.Compress(data);

    Assert.Equal([4098, 4098], ChunkSizes(compressed));
    Assert.Equal(data, OvbaDecompressor.Decompress(compressed));
  }

  [Fact]
  public void A_short_last_chunk_that_wont_compress_is_padded_as_the_format_says()
  {
    var data = Noise(4000);

    var read = RoundTrip(data);

    Assert.Equal(data, read[..4000]);
    Assert.Equal(new byte[96], read[4000..]);
  }

  [Theory]
  [MemberData(nameof(WorkbookNames))]
  public void A_real_dir_stream_comes_back_byte_for_byte_from_its_records(string workbook)
  {
    var dir = DirOf(File.ReadAllBytes(Fixtures.Workbook(workbook)));

    Assert.Equal(dir, DirRecords.Write(DirRecords.Read(dir)));
  }

  [Fact]
  public void A_record_running_past_the_end_is_an_error()
  {
    Assert.Throws<InvalidDataException>(() => DirRecords.Read([0x01, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x01]));
  }

  [Theory]
  [InlineData(0x00, 0x00)]
  [InlineData(0x02, 0x7F)]
  [InlineData(0x04, 0x11)]
  [InlineData(0x06, 0xFF)]
  [InlineData(0xFE, 0xA5)]
  public void What_is_encrypted_decrypts_to_the_same_with_the_same_key(byte seed, byte key)
  {
    foreach (var data in new byte[][] { [0, 0, 0, 0], [0], [0xFF], [1, 2, 3, 4, 5, 6, 7, 8, 9] })
    {
      var (readKey, read) = ProjectEncryption.Decrypt(ProjectEncryption.Encrypt(data, key, seed));

      Assert.Equal(key, readKey);
      Assert.Equal(data, read);
    }
  }

  [Fact]
  public void Excels_own_values_use_the_key_worked_out_from_the_projects_id_with_its_braces()
  {
    var lines = Encoding.Latin1.GetString(Stream(BinOf(File.ReadAllBytes(Fixtures.Workbook("SimpleMacro.xlsm"))), "PROJECT")).Split("\r\n");
    string Value(string key) => lines.First(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Split('"')[1];

    var (cmgKey, cmg) = ProjectEncryption.Decrypt(Value("CMG"));

    Assert.Equal(ProjectEncryption.ProjectKey(Value("ID")), cmgKey);
    Assert.Equal(new byte[4], cmg);
    Assert.Equal([0xFF], ProjectEncryption.Decrypt(Value("GC")).Data);
  }

  static byte[] BinOf(byte[] workbook)
  {
    using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
    using var entry = zip.GetEntry("xl/vbaProject.bin")!.Open();
    var buffer = new MemoryStream();
    entry.CopyTo(buffer);
    return buffer.ToArray();
  }

  static byte[] DirOf(byte[] workbook)
  {
    using var root = RootStorage.Open(new MemoryStream(BinOf(workbook)));
    return OvbaDecompressor.Decompress(Read(root.OpenStorage("VBA"), "dir"));
  }

  static byte[] Stream(byte[] bin, string name)
  {
    using var root = RootStorage.Open(new MemoryStream(bin));
    return Read(root, name);
  }

  static byte[] Read(Storage storage, string name)
  {
    using var stream = storage.OpenStream(name);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }
}
