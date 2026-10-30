namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>A unique temporary folder, deleted when disposed.</summary>
public sealed class TempDirectory : IDisposable
{
  /// <summary>Creates the folder.</summary>
  public TempDirectory()
  {
    Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pqdoc-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path);
  }

  /// <summary>Full path of the folder.</summary>
  public string Path { get; }

  /// <summary>Combines path parts under this folder.</summary>
  public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

  /// <summary>Writes a UTF-8 text file (no BOM) under this folder, creating subfolders as needed.</summary>
  public string WriteFile(string relativePath, string content)
  {
    var path = Combine(relativePath);
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  /// <summary>Names of the files directly in a folder under this one (or in this folder), sorted.</summary>
  public IReadOnlyList<string> FileNames(string? relativeFolder = null)
  {
    var folder = relativeFolder is null ? Path : Combine(relativeFolder);
    return Directory.Exists(folder)
      ? Directory.GetFiles(folder).Select(f => System.IO.Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray()
      : [];
  }

  /// <inheritdoc />
  public void Dispose()
  {
    try
    {
      Directory.Delete(Path, recursive: true);
    }
    catch (IOException)
    {
      // Best effort.
    }
    catch (UnauthorizedAccessException)
    {
      // Best effort.
    }
  }
}
