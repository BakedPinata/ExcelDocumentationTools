using ExcelDocumenter.Shared.Files;

namespace ExcelDocumenter.Shared.Tests;

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
