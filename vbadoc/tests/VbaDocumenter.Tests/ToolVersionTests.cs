using VbaDocumenter.Core;

namespace VbaDocumenter.Tests;

public class ToolVersionTests
{
    const string Sha = "b7c0ebe8d952f156a36dd4ed8b545d572397f368";

    [Fact]
    public void Reads_the_version_and_commit()
    {
        var version = ToolVersion.Parse($"0.5.0+{Sha}");

        Assert.Equal(new ToolVersion("0.5.0", Sha, false), version);
        Assert.Equal("vbadoc 0.5.0 (b7c0ebe)", version.ToString());
        Assert.Equal($"Made by vbadoc 0.5.0, commit {Sha}.", version.Description);
    }

    [Fact]
    public void Marks_a_build_with_uncommitted_changes()
    {
        var version = ToolVersion.Parse($"0.5.0+{Sha}-dirty");

        Assert.Equal(new ToolVersion("0.5.0", Sha, true), version);
        Assert.Equal("vbadoc 0.5.0 (b7c0ebe-dirty)", version.ToString());
        Assert.EndsWith("built with uncommitted changes.", version.Description);
    }

    [Fact]
    public void Shows_the_version_alone_when_built_outside_git()
    {
        var version = ToolVersion.Parse("0.5.0");

        Assert.Equal(new ToolVersion("0.5.0", null, false), version);
        Assert.Equal("vbadoc 0.5.0", version.ToString());
    }
}
