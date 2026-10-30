using System.Reflection;

namespace VbaDocumenter.Core;

/// <summary>A third-party script that is embedded in this assembly and inlined into the HTML page.</summary>
public sealed record VendoredScript(string Name, string Version, string LicenceName, string LicenceText, string Code);

/// <summary>
/// Loads the vendored d3 and Mermaid scripts from embedded resources.
/// See Resources/Vendor/README.md for where they came from and how to upgrade them.
/// </summary>
public static class VendoredScripts
{
  public const string D3Version = "7.9.0";
  public const string MermaidVersion = "11.17.2";

  public static VendoredScript D3 { get; } = Load("d3", D3Version, "ISC");
  public static VendoredScript Mermaid { get; } = Load("mermaid", MermaidVersion, "MIT");

  static VendoredScript Load(string name, string version, string licenceName) =>
      new(name, version, licenceName,
          EmbeddedResources.ReadText($"Vendor/{name}.LICENSE.txt"),
          EmbeddedResources.ReadText($"Vendor/{name}.min.js"));
}

/// <summary>Reads text files that the .csproj embeds under Resources/.</summary>
public static class EmbeddedResources
{
  public static string ReadText(string logicalName)
  {
    var assembly = typeof(EmbeddedResources).Assembly;
    using var stream = assembly.GetManifestResourceStream(logicalName)
        ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' is missing. Check the EmbeddedResource items in VbaDocumenter.Core.csproj.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
  }
}
