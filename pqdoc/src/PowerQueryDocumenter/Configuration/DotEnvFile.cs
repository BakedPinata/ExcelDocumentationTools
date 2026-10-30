namespace PowerQueryDocumenter.Configuration;

/// <summary>
/// A minimal reader for <c>.env</c> files (NFR-ARCH-6: hand-written instead of a package).
/// Supports <c>KEY=value</c> lines, blank lines, <c>#</c> comment lines, an optional
/// <c>export </c> prefix, and values wrapped in single or double quotes.
/// </summary>
public static class DotEnvFile
{
  /// <summary>The file name looked for in the current working directory.</summary>
  public const string FileName = ".env";

  /// <summary>Parses .env content. Later keys override earlier ones. Keys are case-sensitive.</summary>
  public static IReadOnlyDictionary<string, string> Parse(string content)
  {
    ArgumentNullException.ThrowIfNull(content);
    var values = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var rawLine in content.Split('\n'))
    {
      var line = rawLine.Trim();
      if (line.Length == 0 || line.StartsWith('#'))
      {
        continue;
      }

      if (line.StartsWith("export ", StringComparison.Ordinal))
      {
        line = line["export ".Length..].TrimStart();
      }

      var separator = line.IndexOf('=', StringComparison.Ordinal);
      if (separator <= 0)
      {
        continue;
      }

      var key = line[..separator].Trim();
      var value = line[(separator + 1)..].Trim();
      values[key] = Unquote(value);
    }

    return values;
  }

  /// <summary>Loads and parses a .env file. A missing file yields an empty set of values.</summary>
  public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(string path, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(path);
    return File.Exists(path)
      ? Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
      : new Dictionary<string, string>();
  }

  private static string Unquote(string value)
  {
    if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
    {
      return value[1..^1];
    }

    // Unquoted values may carry a trailing comment: KEY=value # comment
    var comment = value.IndexOf(" #", StringComparison.Ordinal);
    return comment >= 0 ? value[..comment].TrimEnd() : value;
  }
}
