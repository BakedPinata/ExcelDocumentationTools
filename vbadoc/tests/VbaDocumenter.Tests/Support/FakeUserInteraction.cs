using ExcelDocumenter.Shared.Files;

namespace VbaDocumenter.Tests.Support;

/// <summary>Scripted answers to the overwrite prompt.</summary>
/// <param name="isInteractive">Whether the session counts as interactive.</param>
/// <param name="answers">Answers returned to successive prompts.</param>
public sealed class FakeUserInteraction(bool isInteractive, params OverwriteAnswer[] answers) : IUserInteraction
{
  readonly Queue<OverwriteAnswer> _pending = new(answers);

  public bool IsInteractive { get; } = isInteractive;

  /// <summary>Paths that were asked about, in order.</summary>
  public List<string> Asked { get; } = [];

  public OverwriteAnswer AskOverwrite(string path)
  {
    Asked.Add(path);
    return _pending.Count > 0 ? _pending.Dequeue() : throw new InvalidOperationException($"Unexpected prompt for {path}");
  }
}
