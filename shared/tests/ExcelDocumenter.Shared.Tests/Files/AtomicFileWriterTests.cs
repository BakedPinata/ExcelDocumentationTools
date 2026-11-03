using ExcelDocumenter.Shared.Files;

namespace ExcelDocumenter.Shared.Tests.Files;

public sealed class AtomicFileWriterTests : IDisposable
{
  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private static Func<Stream, CancellationToken, Task> Writes(string text) =>
    (stream, token) => stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text), token).AsTask();

  [Fact]
  public async Task Writes_a_new_file()
  {
    var path = temp.Combine("out.txt");

    await AtomicFileWriter.WriteAsync(path, Writes("hello"), TestContext.Current.CancellationToken);

    Assert.Equal("hello", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    Assert.Equal(new[] { "out.txt" }, temp.FileNames());
  }

  [Fact]
  public async Task Replaces_an_existing_file()
  {
    var path = temp.WriteFile("out.txt", "a much longer original content");

    await AtomicFileWriter.WriteAsync(path, Writes("new"), TestContext.Current.CancellationToken);

    Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task A_failure_leaves_the_existing_file_untouched_and_no_temporary_file()
  {
    var path = temp.WriteFile("out.txt", "original");

    async Task Fails(Stream stream, CancellationToken token)
    {
      await stream.WriteAsync("partial"u8.ToArray(), token);
      throw new IOException("Disk exploded");
    }

    await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Fails, TestContext.Current.CancellationToken));

    Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    Assert.Equal(new[] { "out.txt" }, temp.FileNames());
  }

  [Fact]
  public async Task Cancellation_removes_the_temporary_file_and_writes_nothing()
  {
    var path = temp.Combine("out.txt");
    using var cancellation = new CancellationTokenSource();

    async Task CancelsMidway(Stream stream, CancellationToken token)
    {
      await stream.WriteAsync("partial"u8.ToArray(), token);
      await cancellation.CancelAsync();
      token.ThrowIfCancellationRequested();
    }

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFileWriter.WriteAsync(path, CancelsMidway, cancellation.Token));

    Assert.Empty(temp.FileNames());
  }

  [Fact]
  public async Task A_missing_folder_is_reported()
  {
    await Assert.ThrowsAsync<DirectoryNotFoundException>(
      () => AtomicFileWriter.WriteAsync(temp.Combine("missing", "out.txt"), Writes("x"), TestContext.Current.CancellationToken));
  }

  [Fact]
  public void Write_puts_the_bytes_in_place_of_any_existing_file()
  {
    var path = temp.WriteFile("out.txt", "a much longer original content");

    AtomicFileWriter.Write(path, "new"u8);

    Assert.Equal("new", File.ReadAllText(path));
    Assert.Equal(new[] { "out.txt" }, temp.FileNames());
  }

  [Fact]
  public void Write_leaves_the_existing_file_and_no_temporary_file_when_it_fails()
  {
    // On Windows a read-only destination can't be replaced, so the move at the end fails after the temporary file is
    // written. Elsewhere a rename replaces it anyway.
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a read-only file.");
    var path = temp.WriteFile("out.txt", "original");

    File.SetAttributes(path, FileAttributes.ReadOnly);
    try
    {
      Assert.ThrowsAny<Exception>(() => AtomicFileWriter.Write(path, "new"u8));
    }
    finally
    {
      File.SetAttributes(path, FileAttributes.Normal);
    }

    Assert.Equal("original", File.ReadAllText(path));
    Assert.Equal(new[] { "out.txt" }, temp.FileNames());
  }

  [Fact]
  public void Write_reports_a_missing_folder()
  {
    Assert.Throws<DirectoryNotFoundException>(() => AtomicFileWriter.Write(temp.Combine("missing", "out.txt"), "x"u8));
  }
}
