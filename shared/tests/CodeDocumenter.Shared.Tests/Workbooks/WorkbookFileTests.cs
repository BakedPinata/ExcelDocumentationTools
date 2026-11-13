using System.IO.Compression;
using CodeDocumenter.Shared.Workbooks;
using OpenMcdf;

namespace CodeDocumenter.Shared.Tests.Workbooks;

public sealed class WorkbookFileTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private static byte[] CompoundFile(params string[] streams)
  {
    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      foreach (var name in streams)
      {
        using var stream = root.CreateStream(name);
        stream.Write(new byte[64]);
      }
    }

    return buffer.ToArray();
  }

  private static byte[] Zip()
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      zip.CreateEntry("[Content_Types].xml");
    }

    return buffer.ToArray();
  }

  [Fact]
  public void A_zip_based_workbook_is_recognised()
  {
    Assert.Equal(WorkbookContainer.Zip, WorkbookFile.ContainerOf(Zip()));
  }

  [Fact]
  public void A_compound_file_is_recognised()
  {
    Assert.Equal(WorkbookContainer.CompoundFile, WorkbookFile.ContainerOf(CompoundFile("Workbook")));
  }

  [Theory]
  [InlineData(new byte[0])]
  [InlineData(new byte[] { 0x50, 0x4B, 0x03 })]
  [InlineData(new byte[] { 0x6E, 0x6F, 0x74, 0x20, 0x61, 0x20, 0x77, 0x6F, 0x72, 0x6B })]
  public void Anything_else_is_unknown(byte[] bytes)
  {
    Assert.Equal(WorkbookContainer.Unknown, WorkbookFile.ContainerOf(bytes));
  }

  [Fact]
  public void An_encrypted_workbook_is_told_apart_from_an_older_xls()
  {
    using var encrypted = RootStorage.Open(new MemoryStream(CompoundFile(WorkbookFile.EncryptedPackageStream, "EncryptionInfo")));
    using var xls = RootStorage.Open(new MemoryStream(CompoundFile("Workbook")));

    Assert.True(WorkbookFile.IsEncrypted(encrypted));
    Assert.False(WorkbookFile.IsEncrypted(xls));
  }

  [Fact]
  public void The_whole_file_is_read_even_while_another_program_has_it_open()
  {
    var path = temp.Combine("Book.xlsx");
    var bytes = Zip();
    File.WriteAllBytes(path, bytes);

    // Excel keeps an open workbook open for writing, and lets others read it.
    using var excel = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    Assert.Equal(bytes, WorkbookFile.ReadAllBytes(path));
  }

  [Fact]
  public void A_file_that_cannot_be_read_says_which_and_why()
  {
    var path = temp.Combine("Missing.xlsx");

    var exception = Assert.Throws<ExtractionException>(() => WorkbookFile.ReadAllBytes(path));

    Assert.StartsWith($"Can't read {path}: ", exception.Message, StringComparison.Ordinal);
    Assert.IsType<FileNotFoundException>(exception.InnerException);
  }

  // ---- When the workbook was last saved ----

  private static byte[] ZipWith(string name, string content)
  {
    var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
      using var writer = new StreamWriter(zip.CreateEntry(name).Open());
      writer.Write(content);
    }

    return buffer.ToArray();
  }

  private const string CoreProperties =
    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties " +
    "xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
    "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-01-02T03:04:05Z</dcterms:created>" +
    "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{0}</dcterms:modified></cp:coreProperties>";

  /// <summary>A compound file whose summary information holds a last-saved time, as an .xls file's does.</summary>
  private static byte[] CompoundFileSavedAt(DateTimeOffset saved, ushort type = 0x40)
  {
    var set = new MemoryStream();
    using (var writer = new BinaryWriter(set, System.Text.Encoding.UTF8, leaveOpen: true))
    {
      // The set: its size, two properties (the code page, then the last-saved time), and their values.
      writer.Write(48);
      writer.Write(2);
      writer.Write(1);
      writer.Write(24);
      writer.Write(13);
      writer.Write(32);
      writer.Write((uint)2); // VT_I2 code page
      writer.Write(1252);
      writer.Write((uint)type);
      writer.Write(saved.UtcDateTime.ToFileTimeUtc());
    }

    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
      writer.Write((ushort)0xFFFE);
      writer.Write((ushort)0);
      writer.Write(0x00020006);
      writer.Write(new byte[16]);
      writer.Write(1);
      writer.Write(Guid.Parse("F29F85E0-4FF9-1068-AB91-08002B27B3D9").ToByteArray());
      writer.Write(48);
      writer.Write(set.ToArray());
    }

    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      using var summary = root.CreateStream((char)5 + "SummaryInformation");
      summary.Write(stream.ToArray());
    }

    return buffer.ToArray();
  }

  [Fact]
  public void A_zip_based_workbook_says_when_it_was_saved_in_its_core_properties()
  {
    var bytes = ZipWith("docProps/core.xml", string.Format(System.Globalization.CultureInfo.InvariantCulture, CoreProperties, "2026-11-04T01:57:36Z"));

    Assert.Equal(new DateTimeOffset(2026, 11, 4, 1, 57, 36, TimeSpan.Zero), WorkbookFile.LastSaved(bytes));
  }

  [Fact]
  public void An_older_xls_file_says_when_it_was_saved_in_its_summary_information()
  {
    var saved = new DateTimeOffset(2019, 5, 6, 7, 8, 9, TimeSpan.Zero);

    Assert.Equal(saved, WorkbookFile.LastSaved(CompoundFileSavedAt(saved)));
  }

  [Fact]
  public void Without_the_property_or_with_one_that_cannot_be_read_there_is_no_saved_time()
  {
    Assert.Null(WorkbookFile.LastSaved(Zip()));
    Assert.Null(WorkbookFile.LastSaved(ZipWith("docProps/core.xml", "<not xml")));
    Assert.Null(WorkbookFile.LastSaved(ZipWith("docProps/core.xml", string.Format(System.Globalization.CultureInfo.InvariantCulture, CoreProperties, "yesterday"))));
    Assert.Null(WorkbookFile.LastSaved(CompoundFile("Workbook")));
    Assert.Null(WorkbookFile.LastSaved(CompoundFileSavedAt(DateTimeOffset.UnixEpoch, type: 0x1F)));
    Assert.Null(WorkbookFile.LastSaved([1, 2, 3]));
  }
}
