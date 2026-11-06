using System.CommandLine;
using ExcelDocumenter.Shared.CommandLine;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Shared.Tests.CommandLine;

public sealed class ImportTargetOptionsTests
{
  private readonly ImportOptions import = new("A workbook.", "modules");
  private readonly ImportTargetOptions target;

  public ImportTargetOptionsTests()
  {
    target = new ImportTargetOptions(import, "modules", "It has to be an .xlsm.");
  }

  private ParseResult Parse(params string[] args) =>
    new RootCommand { import.Command }.Parse(["import", "Source.xlsm", "--items", "A", .. args]);

  [Fact]
  public void Nothing_asked_for_means_no_target()
  {
    var result = Parse();

    Assert.Empty(result.Errors);
    Assert.Null(target.Bind(result));
  }

  [Fact]
  public void New_is_a_full_path_and_never_in_place()
  {
    var bound = target.Bind(Parse("--new", "Toolkit.xlsm"));

    Assert.Equal(Path.GetFullPath("Toolkit.xlsm"), bound!.New);
    Assert.Null(bound.Into);
    Assert.False(bound.InPlace);
  }

  [Fact]
  public void Into_asks_about_clashes_unless_told_otherwise()
  {
    Assert.Equal(OnClashBehaviour.Prompt, target.Bind(Parse("--into", "T.xlsm"))!.OnClash);
    Assert.Equal(OnClashBehaviour.Overwrite, target.Bind(Parse("--into", "T.xlsm", "--on-clash", "overwrite"))!.OnClash);
    Assert.Equal(OnClashBehaviour.Skip, target.Bind(Parse("--into", "T.xlsm", "--on-clash", "SKIP"))!.OnClash);
  }

  [Fact]
  public void In_place_with_force_is_how_an_unattended_run_says_yes()
  {
    var result = Parse("--into", "T.xlsm", "--in-place", "--force");

    Assert.Empty(result.Errors);
    Assert.True(target.Bind(result)!.InPlace);
  }

  [Theory]
  [InlineData("--new", "A.xlsm", "--into", "B.xlsm")]
  [InlineData("--new", "A.xlsm", "--bundle", "Shared")]
  [InlineData("--into", "A.xlsm", "--bundle", "Shared")]
  public void Only_one_place_can_be_given(params string[] args)
  {
    var error = Assert.Single(Parse(args).Errors);

    Assert.Equal("Say where they go with one of --new, --into or --bundle (-b).", error.Message);
  }

  [Theory]
  [InlineData("--in-place")]
  [InlineData("--in-place", "--new", "A.xlsm")]
  public void In_place_needs_a_target(params string[] args)
  {
    Assert.Contains(Parse(args).Errors, e => e.Message.StartsWith("--in-place changes the target", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("--on-clash", "skip")]
  [InlineData("--on-clash", "skip", "--new", "A.xlsm")]
  public void On_clash_needs_a_target_that_could_clash(params string[] args)
  {
    Assert.Contains(Parse(args).Errors, e => e.Message.StartsWith("--on-clash is about", StringComparison.Ordinal));
  }

  [Fact]
  public void An_unknown_on_clash_says_what_it_can_be()
  {
    Assert.Contains(Parse("--into", "T.xlsm", "--on-clash", "merge").Errors, e => e.Message == "Invalid value 'merge' for --on-clash. Valid values are: overwrite, skip.");
  }

  [Theory]
  [InlineData("overwrite")]
  [InlineData("skip")]
  public void On_exists_means_nothing_in_place_so_it_cant_stand_in_for_the_answer(string behaviour)
  {
    Assert.Contains(Parse("--into", "T.xlsm", "--in-place", "--on-exists", behaviour).Errors, e => e.Message.StartsWith("--in-place writes no new file", StringComparison.Ordinal));
  }
}
