using System.Reflection;

namespace VbaDocumenter.Core;

/// <summary>
/// Which build of vbadoc this is. The version comes from Directory.Build.props; the build adds the git commit and,
/// for a working copy with uncommitted changes, "-dirty" (see Directory.Build.targets). Generated pages show it so
/// a change in them can be traced to a new version of the tool rather than to the workbook.
/// </summary>
public sealed record ToolVersion(string Version, string? Commit, bool Dirty)
{
  /// <summary>Where vbadoc lives; generated pages link to it from the version.</summary>
  public const string RepositoryUrl = "https://github.com/BakedPinata/ExcelDocumentationTools";

  public static ToolVersion Current { get; } =
      Parse(typeof(ToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

  /// <summary>Reads an informational version such as "0.5.0", "0.5.0+&lt;sha&gt;" or "0.5.0+&lt;sha&gt;-dirty".</summary>
  public static ToolVersion Parse(string informationalVersion)
  {
    var plus = informationalVersion.IndexOf('+');
    if (plus < 0) return new ToolVersion(informationalVersion, null, false);

    var version = informationalVersion[..plus];
    var commit = informationalVersion[(plus + 1)..];
    var dirty = commit.EndsWith("-dirty", StringComparison.Ordinal);
    if (dirty) commit = commit[..^"-dirty".Length];
    return new ToolVersion(version, commit.Length > 0 ? commit : null, dirty);
  }

  public string ShortCommit => Commit is null ? "" : Commit[..Math.Min(7, Commit.Length)];

  /// <summary>"vbadoc 0.5.0 (abc1234)", "vbadoc 0.5.0 (abc1234-dirty)" or, built outside git, "vbadoc 0.5.0".</summary>
  public override string ToString() =>
      Commit is null ? $"vbadoc {Version}" : $"vbadoc {Version} ({ShortCommit}{(Dirty ? "-dirty" : "")})";

  /// <summary>A sentence for a tooltip, with the full commit.</summary>
  public string Description =>
      Commit is null
          ? $"Made by vbadoc {Version}, built outside git so the commit isn't known."
          : $"Made by vbadoc {Version}, commit {Commit}{(Dirty ? ", built with uncommitted changes" : "")}.";
}
