using System.CommandLine;
using System.CommandLine.Parsing;
using CodeDocumenter.Desktop.Documenting;
using CodeDocumenter.Desktop.Importing;
using CodeDocumenter.Desktop.Running;
using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace CodeDocumenter.Desktop.Tests.Importing;

public sealed class ImportCommandsTests
{
  private const string Source = @"C:\Work\Sales report.xlsm";
  private const string Bundle = @"C:\Work\Shared";
  private const string Target = @"C:\Work\Target.xlsm";

  private static ImportSettings Settings(string tool = "Vbadoc") => new()
  {
    Source = Source,
    Tool = Enum.Parse<Tool>(tool),
    Items = ["Utilities", "Logger"],
    Bundle = Bundle,
  };

  /// <summary>Every option set away from its default, writing to the destination given.</summary>
  private static ImportSettings Everything(string tool, ImportDestination writeTo, ChooseFrom from = ChooseFrom.List) => new()
  {
    Source = Source,
    Tool = Enum.Parse<Tool>(tool),
    ChooseFrom = from,
    Manifest = @"C:\Downloads\Sales report-vba-export.json",
    Items = ["Utilities"],
    WriteTo = writeTo,
    Bundle = Bundle,
    NewWorkbook = @"C:\Work\New.xlsm",
    Target = Target,
    OnClash = OnClashBehaviour.Skip,
    OnExists = OnExistsBehaviour.Overwrite,
    LogDetail = LogDetail.Verbose,
    NoReferences = true,
    DropSignature = true,
  };

  private static Command ImportOf(Tool tool) => tool == Tool.Vbadoc ? new VbaCli.CliDefinition().Import.Command : new PqCli.CliDefinition().Import.Command;

  private static ParseResult Parse(ToolCommand command) => command.Tool == Tool.Vbadoc
    ? new VbaCli.CliDefinition().Parse(command.Arguments)
    : new PqCli.CliDefinition().Parse(command.Arguments);

  private static IEnumerable<string> Given(ToolCommand command) =>
    Parse(command).CommandResult.Children.OfType<OptionResult>().Where(o => !o.Implicit).Select(o => o.Option.Name);

  public static TheoryData<string, string, string> Shapes()
  {
    var shapes = new TheoryData<string, string, string>();
    foreach (var tool in new[] { "Vbadoc", "Pqdoc" })
    {
      foreach (var writeTo in Enum.GetValues<ImportDestination>())
      {
        shapes.Add(tool, writeTo.ToString(), nameof(ChooseFrom.List));
        shapes.Add(tool, writeTo.ToString(), nameof(ChooseFrom.Manifest));
      }
    }

    return shapes;
  }

  [Fact]
  public void The_command_is_the_choice_and_where_it_goes_with_nothing_else_by_default()
  {
    Assert.Equal(
      "vbadoc import \"C:\\Work\\Sales report.xlsm\" --items \"Utilities,Logger\" --bundle C:\\Work\\Shared",
      ImportCommands.Run(Settings())!.Text);
  }

  [Theory]
  [MemberData(nameof(Shapes))]
  public void What_the_tab_builds_parses_with_no_errors_in_the_tools_own_import(string tool, string writeTo, string from)
  {
    var settings = Everything(tool, Enum.Parse<ImportDestination>(writeTo), Enum.Parse<ChooseFrom>(from));

    Assert.Empty(Parse(ImportCommands.Run(settings)!).Errors);
    Assert.Empty(Parse(ImportCommands.Preview(settings)!).Errors);
    Assert.Empty(ImportCommands.Problems(settings));
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Every_option_of_a_tools_import_is_set_by_the_tab_or_left_out_on_purpose(string name)
  {
    var tool = Enum.Parse<Tool>(name);
    var root = tool == Tool.Vbadoc ? new VbaCli.CliDefinition().RootCommand : new PqCli.CliDefinition().RootCommand;
    var options = ImportOf(tool).Options.Concat(root.Options.Where(o => o.Recursive)).Select(o => o.Name).Distinct();

    Assert.Equal(options.Order(), ImportCommands.Covered[tool].Concat(ImportCommands.LeftOut.Keys).Order());
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Every_option_the_tab_covers_can_be_set_from_it(string name)
  {
    var tool = Enum.Parse<Tool>(name);
    var quiet = Everything(name, ImportDestination.Bundle);
    quiet.LogDetail = LogDetail.Quiet;
    ImportSettings[] settings =
    [
      Everything(name, ImportDestination.Bundle), Everything(name, ImportDestination.NewWorkbook), Everything(name, ImportDestination.InPlace),
      Everything(name, ImportDestination.Copy, ChooseFrom.Manifest), quiet,
    ];

    var given = settings.SelectMany(s => Given(ImportCommands.Run(s)!)).Distinct();

    Assert.Equal(ImportCommands.Covered[tool].Order(), given.Order());
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Preview_is_the_same_choice_with_nowhere_to_write(string tool)
  {
    var preview = ImportCommands.Preview(Everything(tool, ImportDestination.InPlace))!;

    Assert.Empty(Given(preview).Intersect(["--bundle", "--new", "--into", "--in-place", "--on-clash", "--drop-signature", "--on-exists"]));
    Assert.Contains("--items", Given(preview));
    Assert.Contains("--verbose", Given(preview));
  }

  [Fact]
  public void Changing_the_target_itself_gives_no_on_exists_since_it_asks_instead()
  {
    var command = ImportCommands.Run(Everything("Vbadoc", ImportDestination.InPlace))!;

    Assert.Contains("--in-place", command.Arguments);
    Assert.DoesNotContain("--on-exists", command.Arguments);
  }

  [Fact]
  public void Whats_about_a_target_is_only_given_with_one()
  {
    var command = ImportCommands.Run(Everything("Vbadoc", ImportDestination.Bundle))!;

    Assert.DoesNotContain("--on-clash", command.Arguments);
    Assert.DoesNotContain("--drop-signature", command.Arguments);
    Assert.Contains("--no-references", command.Arguments);
  }

  [Fact]
  public void A_manifest_takes_the_place_of_the_list()
  {
    var command = ImportCommands.Run(Everything("Pqdoc", ImportDestination.Bundle, ChooseFrom.Manifest))!;

    Assert.Contains("--manifest", command.Arguments);
    Assert.DoesNotContain("--items", command.Arguments);
  }

  [Theory]
  [InlineData(@"C:\Work\Sales.xlsx", "Vbadoc", "Pqdoc")]   // vbadoc doesn't read it
  [InlineData(@"C:\Work\Sales.xls", "Pqdoc", "Vbadoc")]    // nor pqdoc this
  [InlineData(@"C:\Work\queries", "Vbadoc", "Pqdoc")]      // a folder is pqdoc's
  [InlineData(@"C:\Work\Sales.xlsm", "Pqdoc", "Pqdoc")]    // both read it, so it's the one chosen
  [InlineData(@"C:\Work\Sales.xlsm", "Vbadoc", "Vbadoc")]
  public void The_tool_is_the_one_chosen_if_it_reads_the_source_or_else_the_one_that_does(string source, string chosen, string used)
  {
    var settings = new ImportSettings { Source = source, Tool = Enum.Parse<Tool>(chosen), Items = ["A"] };

    Assert.Equal(used, ImportCommands.ToolFor(settings).ToString());
    Assert.Equal(used, ImportCommands.Preview(settings)!.Tool.ToString());
  }

  [Fact]
  public void Nothing_can_run_until_a_source_is_chosen()
  {
    var settings = new ImportSettings { Items = ["A"], Bundle = Bundle };

    Assert.Null(ImportCommands.Run(settings));
    Assert.Equal(new[] { "Choose a workbook to take from, or for queries a folder of .m files." }, ImportCommands.Problems(settings));
  }

  [Fact]
  public void A_source_neither_tool_reads_says_so()
  {
    var settings = Settings();
    settings.Source = @"C:\Work\Notes.txt";

    Assert.Equal(new[] { "Neither tool reads .txt files." }, ImportCommands.Problems(settings));
  }

  [Fact]
  public void Something_has_to_be_ticked_or_a_manifest_chosen()
  {
    var settings = Settings();
    settings.Items = [];
    Assert.Equal(new[] { "Tick what to take." }, ImportCommands.Problems(settings, preview: true));

    settings.ChooseFrom = ChooseFrom.Manifest;
    Assert.Equal(new[] { "Choose a manifest, as the page's Export tab downloads." }, ImportCommands.Problems(settings, preview: true));
  }

  [Fact]
  public void A_name_with_a_comma_needs_a_manifest_since_items_are_split_at_commas()
  {
    var settings = Settings("Pqdoc");
    settings.Items = ["Sales, by region"];

    var problem = Assert.Single(ImportCommands.Problems(settings, preview: true));

    Assert.StartsWith("Sales, by region has a comma in its name", problem, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("Bundle", "Choose a folder for the bundle.")]
  [InlineData("NewWorkbook", "Choose a name for the new workbook.")]
  [InlineData("Copy", "Choose the target workbook.")]
  [InlineData("InPlace", "Choose the target workbook.")]
  public void Run_needs_somewhere_to_write_but_Preview_does_not(string writeTo, string problem)
  {
    var settings = new ImportSettings { Source = Source, Items = ["Utilities"], WriteTo = Enum.Parse<ImportDestination>(writeTo) };

    Assert.Equal(new[] { problem }, ImportCommands.Problems(settings));
    Assert.Empty(ImportCommands.Problems(settings, preview: true));
  }

  [Fact]
  public void Every_option_left_out_says_why()
  {
    Assert.All(ImportCommands.LeftOut.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
  }
}
