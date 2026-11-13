using CodeDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Core.Output;

/// <summary>An output file the run intends to produce, as the shared <see cref="OverwriteResolver"/> sees it.</summary>
/// <param name="Format">The export format.</param>
/// <param name="Path">Full path of the destination file.</param>
public sealed record PlannedOutput(OutputFormat Format, string Path) : IPlannedOutput
{
  /// <summary>The format's name on the command line, such as <c>html</c>, for messages.</summary>
  public string Name => Format.CliName();
}
