using System.Reflection;

namespace CodeDocumenter.Shared;

/// <summary>
/// Which build of a tool this is. The version comes from the tool's Directory.Build.props; the build adds the last
/// commit that touched the tool and, if there are uncommitted changes, "-dirty" (see Directory.Build.targets at the
/// repository root). Generated pages show it, so a change in them can be traced to a new version of the tool rather
/// than to the workbook.
/// </summary>
/// <param name="Product">The tool's short name, such as vbadoc.</param>
/// <param name="Version">The version, such as 1.0.0.</param>
/// <param name="Commit">The full commit, or null when built outside git.</param>
/// <param name="Dirty">Whether the build had uncommitted changes.</param>
public sealed record ToolVersion(string Product, string Version, string? Commit, bool Dirty)
{
  /// <summary>Where the tools live; generated pages link to it from the version.</summary>
  public const string RepositoryUrl = "https://github.com/BakedPinata/ExcelDocumentationTools";

  /// <summary>
  /// The version of the tool an assembly belongs to, from its product name and informational version. Pass one of
  /// the tool's own assemblies, not this one.
  /// </summary>
  public static ToolVersion Of(Assembly assembly)
  {
    ArgumentNullException.ThrowIfNull(assembly);
    var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? assembly.GetName().Name ?? string.Empty;
    var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    return Parse(product, version);
  }

  /// <summary>Reads an informational version such as "1.0.0", "1.0.0+&lt;sha&gt;" or "1.0.0+&lt;sha&gt;-dirty".</summary>
  public static ToolVersion Parse(string product, string informationalVersion)
  {
    ArgumentNullException.ThrowIfNull(product);
    ArgumentNullException.ThrowIfNull(informationalVersion);

    var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
    if (plus < 0)
    {
      return new ToolVersion(product, informationalVersion, null, false);
    }

    var version = informationalVersion[..plus];
    var commit = informationalVersion[(plus + 1)..];
    var dirty = commit.EndsWith("-dirty", StringComparison.Ordinal);
    if (dirty)
    {
      commit = commit[..^"-dirty".Length];
    }

    return new ToolVersion(product, version, commit.Length > 0 ? commit : null, dirty);
  }

  /// <summary>The first seven characters of the commit, or an empty string outside git.</summary>
  public string ShortCommit => Commit is null ? string.Empty : Commit[..Math.Min(7, Commit.Length)];

  /// <summary>"pqdoc 1.0.0 (abc1234)", "pqdoc 1.0.0 (abc1234-dirty)" or, built outside git, "pqdoc 1.0.0".</summary>
  public override string ToString() =>
    Commit is null ? $"{Product} {Version}" : $"{Product} {Version} ({ShortCommit}{(Dirty ? "-dirty" : string.Empty)})";

  /// <summary>A sentence for a tooltip, with the full commit.</summary>
  public string Description =>
    Commit is null
      ? $"Made by {Product} {Version}, built outside git so the commit isn't known."
      : $"Made by {Product} {Version}, commit {Commit}{(Dirty ? ", built with uncommitted changes" : string.Empty)}.";
}
