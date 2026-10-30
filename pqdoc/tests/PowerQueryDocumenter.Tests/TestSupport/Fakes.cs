using PowerQueryDocumenter.Configuration;
using PowerQueryDocumenter.Output;

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

/// <summary>A clock fixed at one instant, in a UTC+08:00 local time zone.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
  private static readonly TimeZoneInfo Perth =
    TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "UTC+08:00", "UTC+08:00");

  private readonly DateTimeOffset utcNow;

  /// <param name="utcNow">The fixed UTC time. Defaults to 2026-10-02 02:03 UTC (10:03 local).</param>
  public FixedTimeProvider(DateTimeOffset? utcNow = null)
  {
    this.utcNow = utcNow ?? new DateTimeOffset(2026, 10, 2, 2, 3, 0, TimeSpan.Zero);
  }

  /// <inheritdoc />
  public override TimeZoneInfo LocalTimeZone => Perth;

  /// <inheritdoc />
  public override DateTimeOffset GetUtcNow() => utcNow;
}
