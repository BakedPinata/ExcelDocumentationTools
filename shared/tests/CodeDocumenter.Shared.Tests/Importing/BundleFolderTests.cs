using System.Text;
using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Shared.Tests.Importing;

/// <summary>A bundle only goes where it can't cost anyone their own files.</summary>
public sealed class BundleFolderTests : IDisposable
{
  private static readonly string Readme = BundleFolder.Marker("vbadoc") + "\n# A bundle\n";
  private static readonly string[] Extensions = [".bas"];

  private readonly TempDirectory temp = new();

  private string Folder => Path.Combine(this.temp.Path, "Shared");

  public void Dispose() => this.temp.Dispose();

  private void WriteBundle(params string[] names) =>
    BundleFolder.Write(this.Folder, names.ToDictionary(n => n, n => Encoding.ASCII.GetBytes(n)), Readme, Extensions, "*.bas -text\n");

  [Fact]
  public void A_missing_or_empty_folder_can_hold_a_bundle()
  {
    Assert.True(BundleFolder.CanHold(this.Folder, "vbadoc"));
    Directory.CreateDirectory(this.Folder);
    Assert.True(BundleFolder.CanHold(this.Folder, "vbadoc"));
  }

  [Fact]
  public void An_earlier_bundle_can_be_replaced_and_files_no_longer_in_it_go()
  {
    WriteBundle("Logger.bas", "Paths.bas");
    Assert.True(BundleFolder.CanHold(this.Folder, "vbadoc"));

    WriteBundle("Logger.bas");

    Assert.True(File.Exists(Path.Combine(this.Folder, "Logger.bas")));
    Assert.False(File.Exists(Path.Combine(this.Folder, "Paths.bas")));
    Assert.StartsWith(BundleFolder.Marker("vbadoc"), File.ReadAllText(Path.Combine(this.Folder, BundleFolder.ReadmeName)));
  }

  [Fact]
  public void A_folder_of_other_files_cant_hold_one_so_its_files_are_safe()
  {
    Directory.CreateDirectory(this.Folder);
    File.WriteAllText(Path.Combine(this.Folder, "Mine.bas"), "mine");
    Assert.False(BundleFolder.CanHold(this.Folder, "vbadoc"));

    File.WriteAllText(Path.Combine(this.Folder, BundleFolder.ReadmeName), "# My project\n");
    Assert.False(BundleFolder.CanHold(this.Folder, "vbadoc"));
  }

  [Fact]
  public void Another_tools_bundle_is_someone_elses_folder()
  {
    WriteBundle("Logger.bas");
    Assert.False(BundleFolder.CanHold(this.Folder, "pqdoc"));
  }

  [Fact]
  public void Writing_the_same_bundle_again_would_change_nothing()
  {
    WriteBundle("Logger.bas");
    var files = new Dictionary<string, byte[]> { ["Logger.bas"] = Encoding.ASCII.GetBytes("Logger.bas") };

    Assert.False(BundleFolder.WouldChange(this.Folder, files, Readme));
    Assert.True(BundleFolder.WouldChange(this.Folder, files, Readme + "more\n"));
    Assert.True(BundleFolder.WouldChange(this.Folder, new Dictionary<string, byte[]> { ["Logger.bas"] = [1] }, Readme));
  }
}
