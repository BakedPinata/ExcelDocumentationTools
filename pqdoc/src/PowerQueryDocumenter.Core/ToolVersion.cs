using System.Reflection;

namespace PowerQueryDocumenter.Core;

/// <summary>
/// Which build of pqdoc this is. The version comes from Directory.Build.props; the build adds the last commit that
/// touched pqdoc and, if there are uncommitted changes, "-dirty" (see Directory.Build.targets at the repository
/// root). Generated pages show it so a change in them can be traced to a new version of the tool rather than to the
/// queries.
/// </summary>
public sealed record ToolVersion(string Version, string? Commit, bool Dirty)
{
  /// <summary>Where pqdoc lives; generated pages link to it from the version.</summary>
  public const string RepositoryUrl = "https://github.com/BakedPinata/ExcelDocumentationTools";

  /// <summary>The version of this build.</summary>
  public static ToolVersion Current { get; } =
    Parse(typeof(ToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

  /// <summary>Reads an informational version such as "1.0.0", "1.0.0+&lt;sha&gt;" or "1.0.0+&lt;sha&gt;-dirty".</summary>
  public static ToolVersion Parse(string informationalVersion)
  {
    ArgumentNullException.ThrowIfNull(informationalVersion);

    var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
    if (plus < 0)
    {
      return new ToolVersion(informationalVersion, null, false);
    }

    var version = informationalVersion[..plus];
    var commit = informationalVersion[(plus + 1)..];
    var dirty = commit.EndsWith("-dirty", StringComparison.Ordinal);
    if (dirty)
    {
      commit = commit[..^"-dirty".Length];
    }

    return new ToolVersion(version, commit.Length > 0 ? commit : null, dirty);
  }

  /// <summary>The first seven characters of the commit, or an empty string outside git.</summary>
  public string ShortCommit => Commit is null ? string.Empty : Commit[..Math.Min(7, Commit.Length)];

  /// <summary>"pqdoc 1.0.0 (abc1234)", "pqdoc 1.0.0 (abc1234-dirty)" or, built outside git, "pqdoc 1.0.0".</summary>
  public override string ToString() =>
    Commit is null ? $"pqdoc {Version}" : $"pqdoc {Version} ({ShortCommit}{(Dirty ? "-dirty" : string.Empty)})";

  /// <summary>A sentence for a tooltip, with the full commit.</summary>
  public string Description =>
    Commit is null
      ? $"Made by pqdoc {Version}, built outside git so the commit isn't known."
      : $"Made by pqdoc {Version}, commit {Commit}{(Dirty ? ", built with uncommitted changes" : string.Empty)}.";
}
