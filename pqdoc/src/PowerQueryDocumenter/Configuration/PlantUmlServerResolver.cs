namespace PowerQueryDocumenter.Configuration;

/// <summary>Where the PlantUML server URL came from.</summary>
public enum PlantUmlServerSource
{
  /// <summary>No server configured.</summary>
  None,

  /// <summary>The <c>--plantuml-server</c> option.</summary>
  CommandLine,

  /// <summary>The <c>PLANTUML_SERVER_URL</c> environment variable.</summary>
  EnvironmentVariable,

  /// <summary>A <c>.env</c> file in the current working directory.</summary>
  DotEnvFile,
}

/// <summary>The result of resolving the PlantUML server URL.</summary>
/// <param name="Server">The validated server URL, or null when none is configured or it is invalid.</param>
/// <param name="Source">Where the value came from.</param>
/// <param name="Error">A validation message when the configured value is invalid; otherwise null.</param>
public sealed record PlantUmlServerResolution(Uri? Server, PlantUmlServerSource Source, string? Error);

/// <summary>
/// Resolves the PlantUML server URL (FR-PUML-4) in priority order: the <c>--plantuml-server</c>
/// option, then the <c>PLANTUML_SERVER_URL</c> environment variable, then a <c>.env</c> file in the
/// current working directory. There is deliberately no built-in default server, so query names are
/// never sent to a third-party service unless the user configures one.
/// </summary>
public static class PlantUmlServerResolver
{
  /// <summary>Name of the environment variable (and .env key) holding the server URL.</summary>
  public const string EnvironmentVariableName = "PLANTUML_SERVER_URL";

  /// <summary>Message used when a rendered format is requested but no server is configured.</summary>
  public static string MissingServerMessage { get; } =
    "Rendering pdf, png or svg needs a PlantUML server, but none is configured. Supply its URL in one of three ways: "
    + "(1) the --plantuml-server option, "
    + $"(2) the {EnvironmentVariableName} environment variable, or "
    + $"(3) a {DotEnvFile.FileName} file in the current directory containing {EnvironmentVariableName}=<url>.";

  /// <summary>Resolves the server URL. The first source that has a non-blank value wins, even if that value is invalid.</summary>
  public static async Task<PlantUmlServerResolution> ResolveAsync(
    string? commandLineValue,
    IAppEnvironment environment,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(environment);

    if (!string.IsNullOrWhiteSpace(commandLineValue))
    {
      return Validate(commandLineValue, PlantUmlServerSource.CommandLine, "--plantuml-server");
    }

    var fromEnvironment = environment.GetEnvironmentVariable(EnvironmentVariableName);
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
      return Validate(fromEnvironment, PlantUmlServerSource.EnvironmentVariable, $"the {EnvironmentVariableName} environment variable");
    }

    var dotEnvPath = Path.Combine(environment.CurrentDirectory, DotEnvFile.FileName);
    var dotEnv = await DotEnvFile.LoadAsync(dotEnvPath, cancellationToken).ConfigureAwait(false);
    if (dotEnv.TryGetValue(EnvironmentVariableName, out var fromDotEnv)
      && !string.IsNullOrWhiteSpace(fromDotEnv))
    {
      return Validate(fromDotEnv, PlantUmlServerSource.DotEnvFile, $"{EnvironmentVariableName} in {dotEnvPath}");
    }

    return new PlantUmlServerResolution(null, PlantUmlServerSource.None, null);
  }

  /// <summary>Checks that <paramref name="value"/> is an absolute http or https URL (FR-PUML-6).</summary>
  public static bool TryParseServerUrl(string value, out Uri? server)
  {
    server = null;
    if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
      || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
      || string.IsNullOrEmpty(uri.Host)
      || !string.IsNullOrEmpty(uri.Query)
      || !string.IsNullOrEmpty(uri.Fragment))
    {
      return false;
    }

    server = uri;
    return true;
  }

  private static PlantUmlServerResolution Validate(string value, PlantUmlServerSource source, string origin) =>
    TryParseServerUrl(value, out var server)
      ? new PlantUmlServerResolution(server, source, null)
      : new PlantUmlServerResolution(
        null,
        source,
        $"The PlantUML server URL '{value}' from {origin} is not valid. It must be an absolute http:// or https:// URL "
        + "with no query string, for example https://plantuml.example.com or http://localhost:8080/plantuml.");
}
