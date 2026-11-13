using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeDocumenter.Shared.Pages;

/// <summary>What <see cref="ViewsFile.Read"/> found in an output folder.</summary>
/// <param name="Json">The views to build into the page, as the page's script reads them, or null for none.</param>
/// <param name="Count">How many views that is.</param>
/// <param name="Warning">Why the file's views weren't built in, or what's odd about them; null when there's nothing
/// to say. Never a reason to stop: the page is made without them.</param>
public sealed record ViewsFileResult(string? Json, int Count, string? Warning)
{
  /// <summary>No views file, which is the usual case.</summary>
  public static ViewsFileResult None { get; } = new(null, 0, null);
}

/// <summary>
/// A saved views file in a tool's output folder, <c>&lt;tool&gt;-views.json</c>, as the page's Views menu exports it
/// (see <c>shell.js</c>). A tool builds the file's views into the page it makes, so readers get them by opening the
/// page and the file is versioned with the source. The file is the person's own: a tool reads it and never writes,
/// renames or deletes it.
/// </summary>
public static class ViewsFile
{
  /// <summary>The newest format this build reads; the page's script writes the same (<c>VIEWS_FORMAT</c>).</summary>
  public const int FormatVersion = 1;

  private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

  /// <summary>The file's name for a tool, such as <c>vbadoc-views.json</c>.</summary>
  public static string FileName(string tool) => tool + "-views.json";

  /// <summary>Reads the tool's views file from a folder, if there is one.</summary>
  /// <param name="folder">The output folder, which need not exist yet.</param>
  /// <param name="tool">The tool's short name, such as <c>vbadoc</c>.</param>
  /// <param name="source">What the page documents, as the page names it (a workbook's file name), to say when the file
  /// holds another's views.</param>
  public static ViewsFileResult Read(string folder, string tool, string source)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(folder);
    ArgumentException.ThrowIfNullOrWhiteSpace(tool);
    ArgumentNullException.ThrowIfNull(source);

    var name = FileName(tool);
    var path = Path.Combine(folder, name);
    if (!File.Exists(path))
    {
      return ViewsFileResult.None;
    }

    JsonNode? root;
    try
    {
      root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }
    catch (JsonException ex)
    {
      return Left(name, $"it isn't valid JSON ({ex.Message})");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return Left(name, $"it couldn't be read ({ex.Message})");
    }

    if (root is not JsonObject file || file["views"] is not JsonArray views || Number(file["formatVersion"]) is not >= 1)
    {
      return Left(name, "it isn't a views file");
    }

    if (Text(file["tool"]) != tool)
    {
      return Left(name, $"it holds {(Text(file["tool"]) is { } other ? other + "'s" : "another tool's")} views, not {tool}'s");
    }

    if (Number(file["formatVersion"]) > FormatVersion)
    {
      return Left(name, $"it was written by a newer {tool} than this one");
    }

    // Only what the page reads is kept, and only views with a name, which is how the page tells them apart.
    var kept = new JsonArray();
    foreach (var view in views)
    {
      if (view is JsonObject && !string.IsNullOrWhiteSpace(Text(view["name"])))
      {
        kept.Add(view.DeepClone());
      }
    }

    var skipped = views.Count - kept.Count;
    var warnings = new List<string>();
    if (skipped > 0)
    {
      warnings.Add($"{skipped} without a name {(skipped == 1 ? "was" : "were")} left out");
    }

    if (Text(file["workbook"]) is { } workbook && workbook != source)
    {
      warnings.Add($"it holds views of {workbook}, not {source}, so they may mention items this page doesn't have");
    }

    if (kept.Count == 0)
    {
      return new ViewsFileResult(null, 0, warnings.Count > 0 ? $"{name}: {string.Join("; ", warnings)}." : null);
    }

    var built = new JsonObject { ["formatVersion"] = FormatVersion, ["tool"] = tool, ["views"] = kept };
    return new ViewsFileResult(
      built.ToJsonString(Indented).ReplaceLineEndings("\n"),
      kept.Count,
      warnings.Count > 0 ? $"{name}: {string.Join("; ", warnings)}." : null);
  }

  private static ViewsFileResult Left(string name, string why) =>
    new(null, 0, $"{name} wasn't built into the page: {why}.");

  private static string? Text(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

  private static double? Number(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;
}
