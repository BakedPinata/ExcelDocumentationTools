using ExcelDocumenter.Shared.Files;
using PowerQueryDocumenter.Core.Configuration;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>Scripted answers to overwrite prompts.</summary>
public sealed class FakeUserInteraction : IUserInteraction
{
  private readonly Queue<OverwriteAnswer> pendingAnswers;

  /// <param name="isInteractive">Whether the session counts as interactive.</param>
  /// <param name="answers">Answers returned to successive prompts.</param>
  public FakeUserInteraction(bool isInteractive, params OverwriteAnswer[] answers)
  {
    IsInteractive = isInteractive;
    pendingAnswers = new Queue<OverwriteAnswer>(answers);
  }

  /// <inheritdoc />
  public bool IsInteractive { get; }

  /// <summary>Paths that were asked about, in order.</summary>
  public List<string> Asked { get; } = [];

  /// <inheritdoc />
  public OverwriteAnswer AskOverwrite(string path)
  {
    Asked.Add(path);
    return pendingAnswers.Count > 0
      ? pendingAnswers.Dequeue()
      : throw new InvalidOperationException($"Unexpected prompt for {path}");
  }
}

/// <summary>An environment with a chosen current directory and variables.</summary>
public sealed class FakeEnvironment : IAppEnvironment
{
  private readonly Dictionary<string, string> variables;

  /// <param name="currentDirectory">The current directory to report.</param>
  /// <param name="variables">Environment variables to report.</param>
  public FakeEnvironment(string currentDirectory, Dictionary<string, string>? variables = null)
  {
    CurrentDirectory = currentDirectory;
    this.variables = variables ?? [];
  }

  /// <inheritdoc />
  public string CurrentDirectory { get; }

  /// <inheritdoc />
  public string? GetEnvironmentVariable(string name) => variables.TryGetValue(name, out var value) ? value : null;
}

