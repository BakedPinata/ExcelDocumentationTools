using System.Text;
using CodeDocumenter.Shared.Files;

namespace CodeDocumenter.Shared.Tests.Files;

public sealed class SourceFolderWriterTests : IDisposable
{
  private const string GitAttributes = "*.m -text diff\n";

  private static readonly string[] Owned = [".m"];

  private readonly TempDirectory temp = new();

  public void Dispose() => temp.Dispose();

  private string Folder => temp.Combine("out");

  private static Dictionary<string, byte[]> Files(params string[] namesAndTexts)
  {
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    for (var i = 0; i < namesAndTexts.Length; i += 2)
    {
      files[namesAndTexts[i]] = Encoding.UTF8.GetBytes(namesAndTexts[i + 1]);
    }

    return files;
  }

  private SourceFolderResult Write(Dictionary<string, byte[]> files) => SourceFolderWriter.Write(Folder, files, Owned, GitAttributes);

  [Fact]
  public void Creates_the_folder_the_files_and_a_gitattributes()
  {
    var result = Write(Files("A.m", "1", "B.m", "2"));

    Assert.Equal(new[] { "A.m", "B.m" }, result.Written);
    Assert.Empty(result.Unchanged);
    Assert.Empty(result.Deleted);
    Assert.True(result.CreatedGitAttributes);
    Assert.Equal(new[] { ".gitattributes", "A.m", "B.m" }, temp.FileNames("out"));
    Assert.Equal("2", File.ReadAllText(Path.Combine(Folder, "B.m")));
    Assert.Equal(GitAttributes, File.ReadAllText(Path.Combine(Folder, ".gitattributes")));
  }

  [Fact]
  public void Unchanged_files_keep_their_timestamps()
  {
    Write(Files("A.m", "1", "B.m", "2"));
    var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(Path.Combine(Folder, "A.m"), stamp);

    var result = Write(Files("A.m", "1", "B.m", "changed"));

    Assert.Equal(new[] { "B.m" }, result.Written);
    Assert.Equal(new[] { "A.m" }, result.Unchanged);
    Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(Folder, "A.m")));
    Assert.Equal("changed", File.ReadAllText(Path.Combine(Folder, "B.m")));
    Assert.False(result.CreatedGitAttributes);
  }

  [Fact]
  public void Only_the_tools_own_top_level_files_are_deleted()
  {
    Write(Files("A.m", "1", "Gone.m", "2"));
    temp.WriteFile(Path.Combine("out", "notes.txt"), "mine");
    temp.WriteFile(Path.Combine("out", "sub", "Deep.m"), "mine");

    var result = Write(Files("A.m", "1"));

    Assert.Equal(new[] { "Gone.m" }, result.Deleted);
    Assert.Equal(new[] { ".gitattributes", "A.m", "notes.txt" }, temp.FileNames("out"));
    Assert.True(File.Exists(Path.Combine(Folder, "sub", "Deep.m")));
  }

  [Fact]
  public void A_rename_by_case_takes_the_new_spelling()
  {
    Write(Files("orders.m", "1"));

    var result = Write(Files("Orders.m", "1"));

    Assert.Equal(new[] { "orders.m" }, result.Deleted);
    Assert.Equal(new[] { "Orders.m" }, result.Written);
    Assert.Equal(new[] { ".gitattributes", "Orders.m" }, temp.FileNames("out"));
  }

  [Fact]
  public void An_existing_gitattributes_is_never_overwritten()
  {
    temp.WriteFile(Path.Combine("out", ".gitattributes"), "mine\n");

    var result = Write(Files("A.m", "1"));

    Assert.False(result.CreatedGitAttributes);
    Assert.Equal("mine\n", File.ReadAllText(Path.Combine(Folder, ".gitattributes")));
  }

  [Fact]
  public void Names_that_differ_only_by_case_are_refused_before_anything_changes()
  {
    var files = Files("A.m", "1", "a.m", "2");

    Assert.Throws<ArgumentException>(() => Write(files));

    Assert.False(Directory.Exists(Folder));
  }
}
