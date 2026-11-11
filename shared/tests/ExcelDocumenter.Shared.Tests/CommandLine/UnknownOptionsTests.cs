using System.CommandLine;
using ExcelDocumenter.Shared.CommandLine;

namespace ExcelDocumenter.Shared.Tests.CommandLine;

public sealed class UnknownOptionsTests
{
  /// <summary>A command shaped like the tools': an argument, an option with a value, a flag and a subcommand.</summary>
  private static IReadOnlyList<string> Find(params string[] args)
  {
    var command = new RootCommand
    {
      new Argument<string>("workbook"),
      new Option<string?>("--output", "-o"),
      new Option<bool>("--verbose", "-v") { Recursive = true },
      new Option<bool>("--quiet", "-q"),
      new Command("import") { new Argument<string>("source"), new Option<string?>("--items", "-i") },
    };
    return UnknownOptions.Find(command.Parse(args), args);
  }

  [Theory]
  [InlineData("--nope")]                          // taken for the workbook
  [InlineData("--nope", "Sales.xlsm")]            // taken for the workbook, leaving the workbook unmatched
  [InlineData("Sales.xlsm", "--nope")]            // unmatched
  [InlineData("Sales.xlsm", "-v", "--nope")]
  [InlineData("import", "--nope", "-i", "A")]
  [InlineData("import", "Sales.xlsm", "--nope")]
  public void A_word_that_looks_like_an_option_but_is_not_one_is_found_wherever_it_is(params string[] args)
  {
    Assert.Equal(new[] { "Unknown option '--nope'." }, Find(args));
  }

  [Fact]
  public void Each_one_is_found_in_order_as_written()
  {
    Assert.Equal(
      new[] { "Unknown option '--nope=3'.", "Unknown option '-x'." },
      Find("--nope=3", "-x", "Sales.xlsm"));
  }

  [Theory]
  [InlineData("-vx", "-x", "-vx")]                        // read as -v and a workbook called x
  [InlineData("-vx Sales.xlsm", "-x", "-vx")]             // read as -v and x, leaving the workbook unmatched
  [InlineData("Sales.xlsm -vxq", "-x", "-vxq")]
  [InlineData("import Sales.xlsm -vx -i A", "-x", "-vx")]
  public void An_unknown_letter_in_a_bundle_of_short_options_is_found(string args, string letter, string bundle)
  {
    Assert.Equal(new[] { $"Unknown option '{letter}' in '{bundle}'." }, Find(args.Split(' ')));
  }

  [Theory]
  [InlineData("Sales.xlsm")]
  [InlineData("Sales.xlsm", "-v", "-q", "-o", "docs")]
  [InlineData("-vq", "Sales.xlsm")]                       // a bundle of known options
  [InlineData("-vodocs", "Sales.xlsm")]                   // ending with an option's value
  [InlineData("x", "-v")]                                 // an argument after a flag, written as a word of its own
  [InlineData("Sales.xlsm", "-o", "-drafts")]             // an option's value may start with a dash
  [InlineData("Sales.xlsm", "--output=-drafts")]
  [InlineData("-o", "--drafts", "Sales.xlsm")]
  [InlineData("--", "-draft.xlsm")]                       // after --, everything is an argument
  [InlineData("-v", "--", "--nope")]
  [InlineData("-")]                                       // a dash on its own isn't an option
  [InlineData("import", "-v", "Sales.xlsm", "-i", "A")]
  [InlineData()]
  public void Known_options_option_values_and_arguments_are_not_counted(params string[] args)
  {
    Assert.Empty(Find(args));
  }
}
