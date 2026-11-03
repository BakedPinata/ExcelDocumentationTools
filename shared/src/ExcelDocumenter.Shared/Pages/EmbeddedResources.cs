using System.Reflection;
using System.Text;

namespace ExcelDocumenter.Shared.Pages;

/// <summary>Reads text files that a project embeds under <c>Resources/</c>, by the logical name its .csproj gives them.</summary>
public static class EmbeddedResources
{
  /// <summary>Reads an embedded text file from <paramref name="assembly"/>.</summary>
  /// <exception cref="InvalidOperationException">The assembly doesn't embed it.</exception>
  public static string ReadText(Assembly assembly, string logicalName)
  {
    ArgumentNullException.ThrowIfNull(assembly);
    ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
    using var stream = assembly.GetManifestResourceStream(logicalName)
      ?? throw new InvalidOperationException(
        $"Embedded resource '{logicalName}' is missing from {assembly.GetName().Name}. Check the EmbeddedResource items in its .csproj.");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }
}
