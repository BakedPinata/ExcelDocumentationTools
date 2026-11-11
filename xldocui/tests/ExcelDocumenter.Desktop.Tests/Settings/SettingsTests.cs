using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Importing;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Desktop.Settings;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;
using PowerQueryDocumenter.Core.Configuration;

namespace ExcelDocumenter.Desktop.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
  private readonly TempFolder folder = new();

  private string File => folder.Combine("xldocui", "settings.json");

  public void Dispose() => folder.Dispose();

  private static DocumentSettings Document(string input, string vbadocOutput = "") => new()
  {
    Input = input,
    Vbadoc = new VbadocSettings { Output = vbadocOutput },
  };

  [Fact]
  public void Settings_are_read_back_as_they_were_saved()
  {
    var settings = new AppSettings
    {
      OpenPages = false,
      Document = new DocumentSettings
      {
        Input = @"C:\Work\Sales.xlsm",
        EmbedSource = true,
        OnExists = OnExistsBehaviour.Skip,
        LogDetail = LogDetail.Quiet,
        Vbadoc = new VbadocSettings { Run = false, Output = @"C:\Docs", OutputName = "Sales" },
        Pqdoc = new PqdocSettings { Formats = [OutputFormat.Csv, OutputFormat.Svg], PlantUmlServer = "https://plantuml.example.com" },
      },
    };
    settings.Remember(settings.Document);

    SettingsStore.Save(File, settings);
    var read = SettingsStore.Load(File);

    Assert.False(read.OpenPages);
    Assert.Equal(@"C:\Work\Sales.xlsm", read.Document.Input);
    Assert.True(read.Document.EmbedSource);
    Assert.Equal(OnExistsBehaviour.Skip, read.Document.OnExists);
    Assert.Equal(LogDetail.Quiet, read.Document.LogDetail);
    Assert.False(read.Document.Vbadoc.Run);
    Assert.Equal(@"C:\Docs", read.Document.Vbadoc.Output);
    Assert.Equal(new[] { OutputFormat.Csv, OutputFormat.Svg }, read.Document.Pqdoc.Formats);
    Assert.Equal("https://plantuml.example.com", read.Document.Pqdoc.PlantUmlServer);
    Assert.Equal(@"C:\Docs", Assert.Single(read.Recent).VbadocOutput);
  }

  [Fact]
  public void Without_a_file_the_defaults_are_used()
  {
    var read = SettingsStore.Load(File);

    Assert.True(read.OpenPages);
    Assert.Equal("", read.Document.Input);
    Assert.Equal(new[] { OutputFormat.Html }, read.Document.Pqdoc.Formats);
    Assert.Empty(read.Recent);
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("{\"document\": {\"onExists\": \"sometimes\"}}")]
  public void A_file_that_cannot_be_read_starts_again_with_the_defaults(string text)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(File)!);
    System.IO.File.WriteAllText(File, text);

    Assert.Equal(OnExistsBehaviour.Prompt, SettingsStore.Load(File).Document.OnExists);
  }

  [Fact]
  public void What_a_hand_edited_file_leaves_out_is_filled_in()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(File)!);
    System.IO.File.WriteAllText(File, "{\"document\": {\"vbadoc\": null, \"pqdoc\": {\"formats\": null}}, \"recent\": [null, {\"input\": \"\"}]}");

    var read = SettingsStore.Load(File);

    Assert.True(read.Document.Vbadoc.Run);
    Assert.Empty(read.Document.Pqdoc.Formats);
    Assert.Empty(read.Recent);
  }

  [Fact]
  public void Recent_inputs_are_the_most_recent_first_once_each_and_only_ten()
  {
    var settings = new AppSettings();
    for (var i = 1; i <= 12; i++)
    {
      settings.Remember(Document($@"C:\Work\Book{i}.xlsm"));
    }

    settings.Remember(Document(@"c:\work\BOOK5.XLSM"));

    Assert.Equal(AppSettings.RecentCount, settings.Recent.Count);
    Assert.Equal(@"c:\work\BOOK5.XLSM", settings.Recent[0].Input);
    Assert.Equal(@"C:\Work\Book12.xlsm", settings.Recent[1].Input);
    Assert.Single(settings.Recent, r => r.Input.Equals(@"C:\Work\Book5.xlsm", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void An_input_is_remembered_tidied_and_a_blank_one_not_at_all()
  {
    var settings = new AppSettings();
    settings.Remember(Document("  "));
    settings.Remember(Document("\"C:\\Work\\Sales.xlsm\""));

    Assert.Equal(@"C:\Work\Sales.xlsm", Assert.Single(settings.Recent).Input);
  }

  [Fact]
  public void Choosing_a_recent_input_brings_back_its_outputs_and_a_new_one_starts_from_the_defaults()
  {
    var settings = new AppSettings();
    settings.Remember(Document(@"C:\Work\Sales.xlsm", vbadocOutput: @"C:\Docs\Sales"));
    settings.Document = Document(@"C:\Work\Other.xlsm", vbadocOutput: @"C:\Docs\Other");

    settings.Choose(@"C:\WORK\SALES.XLSM");
    Assert.Equal(@"C:\WORK\SALES.XLSM", settings.Document.Input);
    Assert.Equal(@"C:\Docs\Sales", settings.Document.Vbadoc.Output);

    settings.Choose(@"C:\Work\New.xlsm");
    Assert.Equal("", settings.Document.Vbadoc.Output);
  }

  [Fact]
  public void The_import_tab_is_read_back_as_it_was_saved()
  {
    var settings = new AppSettings
    {
      Import = new ImportSettings
      {
        Source = @"C:\Work\Sales.xlsm",
        Tool = Tool.Pqdoc,
        ChooseFrom = ChooseFrom.Manifest,
        Manifest = @"C:\Downloads\choice.json",
        Items = ["Chain_A", "fnClean"],
        WriteTo = ImportDestination.InPlace,
        Target = @"C:\Work\Target.xlsx",
        OnClash = OnClashBehaviour.Skip,
        NoReferences = true,
      },
    };

    SettingsStore.Save(File, settings);
    var read = SettingsStore.Load(File).Import;

    Assert.Equal(@"C:\Work\Sales.xlsm", read.Source);
    Assert.Equal(Tool.Pqdoc, read.Tool);
    Assert.Equal(ChooseFrom.Manifest, read.ChooseFrom);
    Assert.Equal(new[] { "Chain_A", "fnClean" }, read.Items);
    Assert.Equal(ImportDestination.InPlace, read.WriteTo);
    Assert.Equal(@"C:\Work\Target.xlsx", read.Target);
    Assert.Equal(OnClashBehaviour.Skip, read.OnClash);
    Assert.True(read.NoReferences);
  }

  [Fact]
  public void An_imports_source_goes_to_the_top_of_the_recent_list_keeping_its_outputs()
  {
    var settings = new AppSettings();
    settings.Remember(Document(@"C:\Work\Sales.xlsm", vbadocOutput: @"C:\Docs\Sales"));
    settings.Remember(Document(@"C:\Work\Other.xlsm"));

    settings.RememberSource(@"c:\work\sales.xlsm");
    settings.RememberSource("\"C:\\Work\\New.xlsx\"");

    Assert.Equal(new[] { @"C:\Work\New.xlsx", @"C:\Work\Sales.xlsm", @"C:\Work\Other.xlsm" }, settings.Recent.Select(r => r.Input));
    Assert.Equal(@"C:\Docs\Sales", settings.Recent[1].VbadocOutput);
  }

  [Fact]
  public void Choosing_an_input_keeps_the_options_that_belong_to_the_tools()
  {
    var settings = new AppSettings { Document = Document(@"C:\Work\Sales.xlsm") };
    settings.Document.EmbedSource = true;
    settings.Document.Pqdoc.Formats = [OutputFormat.Csv];

    settings.Choose(@"C:\Work\New.xlsm");

    Assert.True(settings.Document.EmbedSource);
    Assert.Equal(new[] { OutputFormat.Csv }, settings.Document.Pqdoc.Formats);
  }
}
