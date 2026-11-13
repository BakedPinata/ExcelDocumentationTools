using System.CommandLine;
using System.CommandLine.Parsing;
using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using CodeDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;
using PqCli = PowerQueryDocumenter.Cli;
using VbaCli = VbaDocumenter.Cli;

namespace ExcelDocumenter.Desktop.Tests.Documenting;

public sealed class DocumentCommandsTests
{
  private const string Workbook = @"C:\Work\Sales report.xlsm";

  private static DocumentSettings Settings(string input = Workbook) => new() { Input = input };

  /// <summary>Every option the tab has, set away from its default.</summary>
  private static DocumentSettings Everything() => new()
  {
    Input = Workbook,
    EmbedSource = true,
    OnExists = OnExistsBehaviour.Skip,
    LogDetail = LogDetail.Verbose,
    Vbadoc = new VbadocSettings { Output = @"C:\Docs\VBA", OutputName = "Sales" },
    Pqdoc = new PqdocSettings
    {
      Output = @"C:\Docs\Queries",
      OutputName = "Sales-queries",
      Formats = [OutputFormat.Svg, OutputFormat.Html, OutputFormat.Csv],
      PlantUmlServer = "https://plantuml.example.com",
    },
  };

  private static ParseResult Parse(ToolCommand command) => command.Tool switch
  {
    Tool.Vbadoc => new VbaCli.CliDefinition().Parse(command.Arguments),
    _ => new PqCli.CliDefinition().Parse(command.Arguments),
  };

  private static RootCommand RootOf(Tool tool) => tool switch
  {
    Tool.Vbadoc => new VbaCli.CliDefinition().RootCommand,
    _ => new PqCli.CliDefinition().RootCommand,
  };

  [Fact]
  public void By_default_each_tool_is_given_only_the_workbook()
  {
    var commands = DocumentCommands.For(Settings());

    Assert.Equal(
      new[] { "vbadoc \"C:\\Work\\Sales report.xlsm\"", "pqdoc \"C:\\Work\\Sales report.xlsm\"" },
      commands.Select(c => c.Text));
  }

  [Fact]
  public void Every_option_is_written_only_when_it_differs_from_the_default()
  {
    var commands = DocumentCommands.For(Everything());

    Assert.Equal(
      new[] { Workbook, "--output", @"C:\Docs\VBA", "--output-name", "Sales", "--embed-source", "--on-exists", "skip", "--verbose" },
      commands[0].Arguments);
    Assert.Equal(
      new[]
      {
        Workbook, "--output", @"C:\Docs\Queries", "--output-name", "Sales-queries", "--format", "html,csv,svg",
        "--plantuml-server", "https://plantuml.example.com", "--embed-source", "--on-exists", "skip", "--verbose",
      },
      commands[1].Arguments);
  }

  public static TheoryData<string> Shapes => new() { "default", "everything", "quiet", "csv only", "overwrite", "fail" };

  private static DocumentSettings Shape(string shape)
  {
    var settings = shape == "everything" ? Everything() : Settings();
    switch (shape)
    {
      case "quiet":
        settings.LogDetail = LogDetail.Quiet;
        break;
      case "csv only":
        settings.Pqdoc.Formats = [OutputFormat.Csv];
        break;
      case "overwrite":
        settings.OnExists = OnExistsBehaviour.Overwrite;
        break;
      case "fail":
        settings.OnExists = OnExistsBehaviour.Fail;
        break;
    }

    return settings;
  }

  [Theory]
  [MemberData(nameof(Shapes))]
  public void What_the_tab_builds_parses_with_no_errors_in_each_tools_own_command_line(string shape)
  {
    var commands = DocumentCommands.For(Shape(shape));

    Assert.Equal(2, commands.Count);
    Assert.All(commands, command => Assert.Empty(Parse(command).Errors));
    Assert.Empty(DocumentCommands.Problems(Shape(shape)));
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Every_option_of_a_tools_main_command_is_set_by_the_tab_or_left_out_on_purpose(string name)
  {
    var tool = Enum.Parse<Tool>(name);
    var options = RootOf(tool).Options.Select(o => o.Name).ToHashSet();

    Assert.Equal(options.Order(), DocumentCommands.Covered[tool].Concat(DocumentCommands.LeftOut.Keys).Order());
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void Every_option_the_tab_covers_can_be_set_from_it(string name)
  {
    var tool = Enum.Parse<Tool>(name);
    // --verbose and --quiet can't both be given, so it takes two.
    var quiet = Everything();
    quiet.LogDetail = LogDetail.Quiet;

    var given = new[] { Everything(), quiet }
      .Select(settings => DocumentCommands.For(settings).Single(c => c.Tool == tool))
      .SelectMany(command => Parse(command).CommandResult.Children.OfType<OptionResult>().Where(o => !o.Implicit))
      .Select(o => o.Option.Name)
      .Distinct();

    Assert.Equal(DocumentCommands.Covered[tool].Order(), given.Order());
  }

  [Theory]
  [InlineData("Vbadoc")]
  [InlineData("Pqdoc")]
  public void The_input_is_the_only_argument(string name)
  {
    var tool = Enum.Parse<Tool>(name);
    var command = DocumentCommands.For(Settings()).Single(c => c.Tool == tool);

    Assert.Single(RootOf(tool).Arguments);
    Assert.Equal(Workbook, Parse(command).GetValue<string>(RootOf(tool).Arguments[0].Name));
  }

  [Theory]
  [InlineData(OnExistsBehaviour.Prompt)]
  [InlineData(OnExistsBehaviour.Overwrite)]
  [InlineData(OnExistsBehaviour.Skip)]
  [InlineData(OnExistsBehaviour.Fail)]
  public void If_a_file_exists_means_the_same_to_both_tools(OnExistsBehaviour behaviour)
  {
    var settings = Settings();
    settings.OnExists = behaviour;
    var commands = DocumentCommands.For(settings);

    var vbadoc = new VbaCli.CliDefinition();
    Assert.Equal(behaviour, vbadoc.Overwrite.Bind(vbadoc.Parse(commands[0].Arguments)));
    var pqdoc = new PqCli.CliDefinition();
    Assert.Equal(behaviour, pqdoc.Overwrite.Bind(pqdoc.Parse(commands[1].Arguments)));
  }

  [Theory]
  [InlineData("Normal", false, false)]
  [InlineData("Verbose", true, false)]
  [InlineData("Quiet", false, true)]
  public void Log_sets_verbose_or_quiet(string detail, bool verbose, bool quiet)
  {
    var settings = Settings();
    settings.LogDetail = Enum.Parse<LogDetail>(detail);
    var command = DocumentCommands.Vbadoc(settings);

    var cli = new VbaCli.CliDefinition();
    var parsed = cli.Parse(command.Arguments);
    Assert.Equal(verbose, parsed.GetValue(cli.Verbose));
    Assert.Equal(quiet, parsed.GetValue(cli.Quiet));
  }

  [Fact]
  public void Formats_are_given_in_pqdocs_order_and_the_page_alone_is_left_to_the_default()
  {
    var settings = Settings();
    Assert.DoesNotContain("--format", DocumentCommands.Pqdoc(settings).Arguments);

    settings.Pqdoc.Formats = [OutputFormat.Puml, OutputFormat.Csv];
    var cli = new PqCli.CliDefinition();
    var parsed = cli.Parse(DocumentCommands.Pqdoc(settings).Arguments);

    Assert.Equal(new[] { OutputFormat.Csv, OutputFormat.Puml }, parsed.GetValue(cli.Formats));
  }

  [Fact]
  public void The_PlantUML_server_is_only_given_when_a_format_needs_one()
  {
    var settings = Settings();
    settings.Pqdoc.PlantUmlServer = "https://plantuml.example.com";

    Assert.DoesNotContain("--plantuml-server", DocumentCommands.Pqdoc(settings).Arguments);

    settings.Pqdoc.Formats = [OutputFormat.Html, OutputFormat.Png];
    Assert.Contains("--plantuml-server", DocumentCommands.Pqdoc(settings).Arguments);
  }

  [Theory]
  [InlineData("  C:\\Work\\Sales.xlsm  ", "C:\\Work\\Sales.xlsm")]
  [InlineData("\"C:\\Work\\Sales.xlsm\"", "C:\\Work\\Sales.xlsm")]  // Explorer's Copy as path
  [InlineData(" \" C:\\Work\\Sales.xlsm \" ", "C:\\Work\\Sales.xlsm")]
  [InlineData("\"", "\"")]
  [InlineData("", "")]
  public void Text_is_tidied_into_an_argument(string typed, string argument)
  {
    Assert.Equal(argument, DocumentCommands.Tidy(typed));
  }

  [Fact]
  public void Output_fields_are_tidied_and_left_out_when_blank()
  {
    var settings = Settings("\"C:\\Work\\Sales.xlsm\"");
    settings.Vbadoc.Output = "  ";
    settings.Vbadoc.OutputName = " \"Sales\" ";

    Assert.Equal(new[] { "C:\\Work\\Sales.xlsm", "--output-name", "Sales" }, DocumentCommands.Vbadoc(settings).Arguments);
  }

  [Theory]
  [InlineData(@"C:\Work\Sales.xlsx", new[] { "Pqdoc" })]
  [InlineData(@"C:\Work\Sales.xls", new[] { "Vbadoc" })]
  [InlineData(@"C:\Work\Sales.xlam", new[] { "Vbadoc" })]
  [InlineData(@"C:\Work\Sales.XLSB", new[] { "Vbadoc", "Pqdoc" })]
  [InlineData(@"C:\Work\queries", new[] { "Pqdoc" })]
  public void Only_the_tools_that_read_the_input_run(string input, string[] tools)
  {
    Assert.Equal(tools, DocumentCommands.For(Settings(input)).Select(c => c.Tool.ToString()));
  }

  [Fact]
  public void A_folder_is_for_pqdoc_whatever_its_name()
  {
    using var folder = new TempFolder();
    var named = Directory.CreateDirectory(Path.Combine(folder.Path, "queries.xlsm")).FullName;

    Assert.Equal(new[] { "Pqdoc" }, DocumentCommands.For(Settings(named)).Select(c => c.Tool.ToString()));
  }

  [Fact]
  public void A_tool_that_is_not_ticked_does_not_run()
  {
    var settings = Settings();
    settings.Vbadoc.Run = false;

    Assert.Equal(new[] { "Pqdoc" }, DocumentCommands.For(settings).Select(c => c.Tool.ToString()));
  }

  [Fact]
  public void Nothing_runs_until_an_input_is_chosen()
  {
    Assert.Empty(DocumentCommands.For(Settings("  ")));
    Assert.Equal(new[] { "Choose a workbook, or a folder of .m files for Power Query." }, DocumentCommands.Problems(Settings("")));
  }

  [Fact]
  public void An_input_neither_tool_reads_says_so()
  {
    Assert.Equal(new[] { "Neither tool reads .txt files." }, DocumentCommands.Problems(Settings(@"C:\Work\Notes.TXT")));
  }

  [Fact]
  public void A_tool_has_to_be_ticked()
  {
    var settings = Settings(@"C:\Work\Sales.xlsx");
    settings.Pqdoc.Run = false;
    Assert.Equal(new[] { "Tick Power Query." }, DocumentCommands.Problems(settings));

    settings = Settings();
    settings.Vbadoc.Run = false;
    settings.Pqdoc.Run = false;
    Assert.Equal(new[] { "Tick VBA, Power Query or both." }, DocumentCommands.Problems(settings));
  }

  [Fact]
  public void Power_Query_needs_a_format()
  {
    var settings = Settings();
    settings.Pqdoc.Formats = [];

    Assert.Equal(new[] { "Tick at least one format for Power Query." }, DocumentCommands.Problems(settings));
    Assert.Equal(new[] { "Vbadoc" }, DocumentCommands.For(settings).Select(c => c.Tool.ToString()));
  }

  [Fact]
  public void What_a_tool_says_is_wrong_is_shown_in_its_own_words_before_running()
  {
    var settings = Settings();
    settings.Vbadoc.OutputName = "Sales #3";

    var problem = Assert.Single(DocumentCommands.Problems(settings));

    Assert.StartsWith("vbadoc: --output-name 'Sales #3' contains a character", problem, StringComparison.Ordinal);
  }

  [Fact]
  public void Every_option_left_out_says_why()
  {
    Assert.All(DocumentCommands.LeftOut.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
  }
}
