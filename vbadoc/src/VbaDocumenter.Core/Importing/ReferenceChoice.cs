using Microsoft.Win32;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Importing;

/// <summary>
/// Which of the source's library references go into a target. A reference to something that isn't there stops the whole
/// project compiling, with "Can't find project or library", even code that needs no library. Leaving out one that's
/// needed stops only what needs it. So one that can't be found is left out and listed, never copied.
/// </summary>
/// <param name="Add">The references to add.</param>
/// <param name="NotFound">Those left out, as what they point at isn't on this machine.</param>
/// <param name="NameTaken">Those left out, as the target has a different library of that name.</param>
/// <param name="NotChecked">Those among <paramref name="Add"/> that couldn't be looked up here.</param>
public sealed record ReferenceChoice(
  IReadOnlyList<VbaReference> Add,
  IReadOnlyList<VbaReference> NotFound,
  IReadOnlyList<VbaReference> NameTaken,
  IReadOnlyList<VbaReference> NotChecked)
{
  /// <summary>Sorts the references wanted against what the target has. One it has, known by GUID, is left alone.</summary>
  /// <param name="wanted">The source's references, less those left out by choice.</param>
  /// <param name="target">The target's project, or null if it has none.</param>
  /// <param name="isInstalled">Whether what a reference points at is on this machine, or null if it can't be told.</param>
  public static ReferenceChoice Make(IEnumerable<VbaReference> wanted, VbaProject? target, Func<VbaReference, bool?> isInstalled)
  {
    ArgumentNullException.ThrowIfNull(wanted);
    ArgumentNullException.ThrowIfNull(isInstalled);
    var theirs = target?.References ?? [];
    var add = new List<VbaReference>();
    var notFound = new List<VbaReference>();
    var nameTaken = new List<VbaReference>();
    var notChecked = new List<VbaReference>();
    foreach (var reference in wanted)
    {
      if (theirs.Any(r => r.Guid is not null && r.Guid.Equals(reference.Guid, StringComparison.OrdinalIgnoreCase) || r.Libid.Equals(reference.Libid, StringComparison.OrdinalIgnoreCase)))
      {
        continue;
      }

      if (theirs.Any(r => r.Name.Equals(reference.Name, StringComparison.OrdinalIgnoreCase)))
      {
        nameTaken.Add(reference);
        continue;
      }

      var installed = isInstalled(reference);
      if (installed == false)
      {
        notFound.Add(reference);
        continue;
      }

      add.Add(reference);
      if (installed is null) notChecked.Add(reference);
    }

    return new ReferenceChoice(add, notFound, nameTaken, notChecked);
  }

  /// <summary>
  /// Whether what a reference points at is on this machine: a library by its GUID alone, as a project can name an older
  /// version than the machine has, and another VBA project by its file. Null when it can't be told, off Windows.
  /// </summary>
  public static bool? IsInstalled(VbaReference reference)
  {
    ArgumentNullException.ThrowIfNull(reference);
    if (reference.Kind == VbaReferenceKind.Project) return reference.ProjectPath is { } path && File.Exists(path);
    if (reference.Guid is null) return false;
    if (!OperatingSystem.IsWindows()) return null;
    using var library = Registry.ClassesRoot.OpenSubKey($@"TypeLib\{reference.Guid}");
    return library is not null;
  }
}
