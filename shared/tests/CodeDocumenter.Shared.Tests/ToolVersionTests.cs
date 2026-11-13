namespace CodeDocumenter.Shared.Tests;

public sealed class ToolVersionTests
{
  private const string Sha = "b7c0ebe8d952f156a36dd4ed8b545d572397f368";

  [Fact]
  public void Reads_the_version_and_commit()
  {
    var version = ToolVersion.Parse("pqdoc", $"1.0.0+{Sha}");

    Assert.Equal(new ToolVersion("pqdoc", "1.0.0", Sha, false), version);
    Assert.Equal("pqdoc 1.0.0 (b7c0ebe)", version.ToString());
    Assert.Equal($"Made by pqdoc 1.0.0, commit {Sha}.", version.Description);
  }

  [Fact]
  public void Marks_a_build_with_uncommitted_changes()
  {
    var version = ToolVersion.Parse("vbadoc", $"0.5.0+{Sha}-dirty");

    Assert.Equal(new ToolVersion("vbadoc", "0.5.0", Sha, true), version);
    Assert.Equal("vbadoc 0.5.0 (b7c0ebe-dirty)", version.ToString());
    Assert.EndsWith("built with uncommitted changes.", version.Description);
  }

  [Fact]
  public void Shows_the_version_alone_when_built_outside_git()
  {
    var version = ToolVersion.Parse("pqdoc", "1.0.0");

    Assert.Equal(new ToolVersion("pqdoc", "1.0.0", null, false), version);
    Assert.Equal("pqdoc 1.0.0", version.ToString());
    Assert.Equal("Made by pqdoc 1.0.0, built outside git so the commit isn't known.", version.Description);
  }

  [Fact]
  public void Takes_the_product_and_version_from_the_assembly_it_is_given()
  {
    // This test assembly is built like the tools' assemblies, so it has a product name and an informational version.
    var assembly = typeof(ToolVersionTests).Assembly;

    var version = ToolVersion.Of(assembly);

    Assert.Equal("CodeDocumenter.Shared.Tests", version.Product);
    Assert.StartsWith(version.Version, assembly.GetName().Version!.ToString(), StringComparison.Ordinal);
  }
}
