using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;

namespace VbaDocumenter.Tests.Support;

/// <summary>Scripted answers to prompts.</summary>
/// <param name="isInteractive">Whether the session counts as interactive.</param>
/// <param name="answers">Answers returned to successive overwrite prompts.</param>
public sealed class FakeUserInteraction(bool isInteractive, params OverwriteAnswer[] answers) : IUserInteraction
{
  readonly Queue<OverwriteAnswer> _pending = new(answers);

  public bool IsInteractive { get; } = isInteractive;

  /// <summary>Paths that were asked about, in order.</summary>
  public List<string> Asked { get; } = [];

  /// <summary>Answers to the clash prompt, in order.</summary>
  public Queue<ClashAnswer> ClashAnswers { get; } = [];

  /// <summary>The clashes that were asked about, in order.</summary>
  public List<ImportClash> ClashesAsked { get; } = [];

  /// <summary>Answers to <see cref="Confirm"/>, in order.</summary>
  public Queue<bool> Confirmations { get; } = [];

  /// <summary>The questions that were asked, in order.</summary>
  public List<string> Questions { get; } = [];

  public OverwriteAnswer AskOverwrite(string path)
  {
    Asked.Add(path);
    return _pending.Count > 0 ? _pending.Dequeue() : throw new InvalidOperationException($"Unexpected prompt for {path}");
  }

  public ClashAnswer AskClash(ImportClash clash)
  {
    ClashesAsked.Add(clash);
    return ClashAnswers.Count > 0 ? ClashAnswers.Dequeue() : throw new InvalidOperationException($"Unexpected prompt for {clash.Item}");
  }

  public bool Confirm(string question)
  {
    Questions.Add(question);
    return Confirmations.Count > 0 ? Confirmations.Dequeue() : throw new InvalidOperationException($"Unexpected question: {question}");
  }
}
