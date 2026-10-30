using VbaDocumenter.Logging;

namespace VbaDocumenter;

/// <summary>The bound command line: paths are absolute, and the output folder is always set.</summary>
sealed record CommandLineOptions(string WorkbookPath, string OutputFolder, bool EmbedSource, LogVerbosity Verbosity);
