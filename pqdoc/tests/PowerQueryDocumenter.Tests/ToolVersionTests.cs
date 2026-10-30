using PowerQueryDocumenter.Core;

namespace PowerQueryDocumenter.Tests;

public sealed class ToolVersionTests
{
  private const string Sha = "b7c0ebe8d952f156a36dd4ed8b545d572397f368";

  [Fact]
  public void Reads_the_version_and_commit()
  {
    var version = ToolVersion.Parse($"1.0.0+{Sha}");

    Assert.Equal(new ToolVersion("1.0.0", Sha, false), version);
    Assert.Equal("pqdoc 1.0.0 (b7c0ebe)", version.ToString());
    Assert.Equal($"Made by pqdoc 1.0.0, commit {Sha}.", version.Description);
  }

  [Fact]
  public void Marks_a_build_with_uncommitted_changes()
  {
    var version = ToolVersion.Parse($"1.0.0+{Sha}-dirty");

    Assert.Equal(new ToolVersion("1.0.0", Sha, true), version);
    Assert.Equal("pqdoc 1.0.0 (b7c0ebe-dirty)", version.ToString());
    Assert.EndsWith("built with uncommitted changes.", version.Description);
  }

  [Fact]
  public void Shows_the_version_alone_when_built_outside_git()
  {
    var version = ToolVersion.Parse("1.0.0");

    Assert.Equal(new ToolVersion("1.0.0", null, false), version);
    Assert.Equal("pqdoc 1.0.0", version.ToString());
  }
}
