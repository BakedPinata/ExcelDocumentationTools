using ExcelDocumenter.Shared.Files;
using VbaDocumenter.Cli.Logging;

namespace VbaDocumenter.Cli;

/// <summary>The bound command line: paths are absolute, and the output folder and page name are always set.</summary>
/// <param name="OutputName">The page's name without ".html": the workbook's name and "-vba", or <c>--output-name</c>.</param>
/// <param name="OnExists">What to do when the page already exists and would change (<c>--on-exists</c>, <c>--force</c>).</param>
sealed record CommandLineOptions(
    string WorkbookPath, string OutputFolder, string OutputName, bool EmbedSource, OnExistsBehaviour OnExists, LogVerbosity Verbosity);
