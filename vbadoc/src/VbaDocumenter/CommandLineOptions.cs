using VbaDocumenter.Logging;

namespace VbaDocumenter;

/// <summary>The bound command line: paths are absolute, and the output folder and page name are always set.</summary>
/// <param name="OutputName">The page's name without ".html": the workbook's name and "-vba", or <c>--output-name</c>.</param>
sealed record CommandLineOptions(string WorkbookPath, string OutputFolder, string OutputName, bool EmbedSource, LogVerbosity Verbosity);
