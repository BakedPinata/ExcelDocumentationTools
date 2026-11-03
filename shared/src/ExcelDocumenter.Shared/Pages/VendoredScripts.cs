namespace ExcelDocumenter.Shared.Pages;

/// <summary>A third-party script that is embedded in this assembly and inlined into the pages.</summary>
/// <param name="Name">Its package name.</param>
/// <param name="Version">Its version.</param>
/// <param name="LicenceName">Its licence's name, such as ISC.</param>
/// <param name="LicenceText">Its licence, which travels with it as a comment.</param>
/// <param name="Code">The script.</param>
public sealed record VendoredScript(string Name, string Version, string LicenceName, string LicenceText, string Code);

/// <summary>
/// The vendored scripts. See Resources/Vendor/README.md for where they came from and how to upgrade them.
/// </summary>
public static class VendoredScripts
{
  /// <summary>The vendored d3 version, named in the licence comment in each page.</summary>
  public const string D3Version = "7.9.0";

  /// <summary>d3, which draws every graph and diagram.</summary>
  public static VendoredScript D3 { get; } = Load("d3", D3Version, "ISC");

  private static VendoredScript Load(string name, string version, string licenceName) =>
    new(
      name,
      version,
      licenceName,
      EmbeddedResources.ReadText(typeof(VendoredScripts).Assembly, $"Vendor/{name}.LICENSE.txt"),
      EmbeddedResources.ReadText(typeof(VendoredScripts).Assembly, $"Vendor/{name}.min.js"));
}
