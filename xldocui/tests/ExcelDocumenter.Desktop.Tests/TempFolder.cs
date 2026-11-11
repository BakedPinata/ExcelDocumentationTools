namespace ExcelDocumenter.Desktop.Tests;

/// <summary>A folder of its own for a test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
  public TempFolder()
  {
    Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xldocui-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path);
  }

  public string Path { get; }

  public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

  /// <summary>Copies one of the fixtures in, and returns its path.</summary>
  public string Fixture(string name)
  {
    var copy = Combine(name);
    File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name), copy);
    return copy;
  }

  public void Dispose()
  {
    if (Directory.Exists(Path))
    {
      Directory.Delete(Path, recursive: true);
    }
  }
}
