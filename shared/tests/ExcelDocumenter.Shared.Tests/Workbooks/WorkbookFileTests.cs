using System.IO.Compression;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;

namespace ExcelDocumenter.Shared.Tests.Workbooks;

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
}
