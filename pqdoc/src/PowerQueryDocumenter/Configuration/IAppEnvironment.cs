namespace PowerQueryDocumenter.Configuration;

/// <summary>Process environment the application depends on, abstracted for testing.</summary>
public interface IAppEnvironment
{
  /// <summary>The current working directory.</summary>
  string CurrentDirectory { get; }

  /// <summary>Returns an environment variable, or null when it is not set.</summary>
  string? GetEnvironmentVariable(string name);
}

/// <summary>The real process environment.</summary>
public sealed class SystemEnvironment : IAppEnvironment
{
  /// <inheritdoc />
  public string CurrentDirectory => Environment.CurrentDirectory;

  /// <inheritdoc />
  public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
