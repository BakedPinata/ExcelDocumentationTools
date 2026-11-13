using System.Text.Json;
using CodeDocumenter.Shared.Workbooks;

namespace CodeDocumenter.Shared.Importing;

/// <summary>
/// What the page's Export tab chose, as a file: the items picked, not what they bring, so running it again after the
/// workbook changes picks up new dependencies. It's JSON with a format version, like a views file, and the page writes
/// it. A tool that's asked to read a newer format, or another tool's file, refuses and says why.
/// </summary>
/// <param name="Items">The items picked.</param>
/// <param name="LeaveOutReferences">The library references not to bring, by name.</param>
/// <param name="Workbook">The workbook it was made from, or null if it doesn't say.</param>
public sealed record ExportManifest(IReadOnlyList<string> Items, IReadOnlyList<string> LeaveOutReferences, string? Workbook)
{
  /// <summary>The newest format this build reads.</summary>
  public const int FormatVersion = 1;

  /// <summary>Reads a manifest for <paramref name="tool"/>.</summary>
  /// <exception cref="ExtractionException">It can't be read, or isn't a manifest this tool can use.</exception>
  public static ExportManifest Read(string path, string tool)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    string text;
    try
    {
      text = File.ReadAllText(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw new ExtractionException($"Can't read {path}: {ex.Message}", ex);
    }

    return Parse(text, Path.GetFileName(path), tool);
  }

  /// <summary>Reads a manifest's text.</summary>
  /// <param name="text">The file's text.</param>
  /// <param name="fileName">Its name, for messages.</param>
  /// <param name="tool">The tool reading it, such as <c>vbadoc</c>.</param>
  /// <exception cref="ExtractionException">It isn't a manifest this tool can use.</exception>
  public static ExportManifest Parse(string text, string fileName, string tool)
  {
    ArgumentNullException.ThrowIfNull(text);
    JsonDocument document;
    try
    {
      document = JsonDocument.Parse(text);
    }
    catch (JsonException ex)
    {
      throw new ExtractionException($"{fileName} isn't an export manifest.", ex);
    }

    using (document)
    {
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object
        || !root.TryGetProperty("formatVersion", out var version) || !version.TryGetInt32(out var format) || format < 1
        || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
      {
        throw new ExtractionException($"{fileName} isn't an export manifest.");
      }

      var from = Text(root, "tool");
      if (from != tool)
      {
        throw new ExtractionException($"{fileName} holds {(from is null ? "another tool's" : from + "'s")} choices, not {tool}'s.");
      }

      if (format > FormatVersion)
      {
        throw new ExtractionException($"{fileName} was written by a newer {tool}. Update {tool} to read it.");
      }

      return new ExportManifest(Strings(items), root.TryGetProperty("leaveOutReferences", out var leaveOut) ? Strings(leaveOut) : [], Text(root, "workbook"));
    }
  }

  private static string? Text(JsonElement root, string name) =>
    root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

  private static string[] Strings(JsonElement array) => array.ValueKind != JsonValueKind.Array
    ? []
    : [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
}
