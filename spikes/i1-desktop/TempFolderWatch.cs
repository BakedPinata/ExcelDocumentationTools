using System.Collections.Concurrent;

namespace DesktopSpike;

/// <summary>
/// Notes every file created in the temporary folder while the program runs. Other programs' files turn up too, which
/// is why the report gives the whole path for someone to read, rather than a verdict.
/// </summary>
internal sealed class TempFolderWatch : IDisposable
{
  private readonly ConcurrentQueue<string> _created = new();
  private readonly FileSystemWatcher _watcher;

  public TempFolderWatch()
  {
    _watcher = new FileSystemWatcher(Path.GetTempPath())
    {
      IncludeSubdirectories = true,
      NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
    };
    _watcher.Created += (_, change) => _created.Enqueue(change.FullPath);
    _watcher.EnableRaisingEvents = true;
  }

  public IReadOnlyList<string> Created => [.. _created];

  public void Dispose() => _watcher.Dispose();
}
