namespace VbaDocumenter;

/// <summary>The parsed command line. Kept separate from Program so the rules are easy to see in one place.</summary>
sealed record CommandLineOptions(string WorkbookPath, string OutputFolder, bool EmbedSource, bool Verbose)
{
  public const string Usage =
      """
        Usage: vbadoc <workbook> [--out <folder>] [--embed-source] [--verbose]
               vbadoc --version

          <workbook>       .xlsm, .xlsb, .xlam, .xls or .xla file
          --out <folder>   where to write the source files and page
                           (default: a "VBAProject" folder next to the workbook)
          --embed-source   put the module source inside the page, so it works without "Load folder"
          --verbose        list every member found in each module, with its line range
        """;

  /// <summary>Returns the options, or null with an error message if the arguments are invalid.</summary>
  public static CommandLineOptions? Parse(string[] args, out string? error)
  {
    string? workbook = null;
    string? output = null;
    bool embedSource = false;
    bool verbose = false;
    error = null;

    for (int i = 0; i < args.Length; i++)
    {
      switch (args[i])
      {
        case "--out":
          if (i + 1 >= args.Length) { error = "--out needs a folder."; return null; }
          output = args[++i];
          break;
        case "--embed-source":
          embedSource = true;
          break;
        case "--verbose":
          verbose = true;
          break;
        case var arg when arg.StartsWith("--"):
          error = $"Unknown option '{arg}'.";
          return null;
        default:
          if (workbook != null) { error = "Only one workbook can be given."; return null; }
          workbook = args[i];
          break;
      }
    }

    if (workbook == null) { error = "No workbook given."; return null; }

    var workbookPath = Path.GetFullPath(workbook);
    var outputFolder = Path.GetFullPath(output ?? Path.Combine(Path.GetDirectoryName(workbookPath)!, "VBAProject"));
    return new CommandLineOptions(workbookPath, outputFolder, embedSource, verbose);
  }
}
