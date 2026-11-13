using CodeDocumenter.Shared.Files;
using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Shared.Tests;

/// <summary>Scripted answers to prompts.</summary>
public sealed class FakeUserInteraction : IUserInteraction
{
  private readonly Queue<OverwriteAnswer> pendingAnswers;

  /// <summary>Answers to the clash prompt, in order.</summary>
  public Queue<ClashAnswer> ClashAnswers { get; } = [];

  /// <summary>Answers to <see cref="Confirm"/>, in order.</summary>
  public Queue<bool> Confirmations { get; } = [];

  /// <summary>The clashes that were asked about, in order.</summary>
  public List<ImportClash> ClashesAsked { get; } = [];

  /// <summary>The questions that were asked, in order.</summary>
  public List<string> Questions { get; } = [];

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

  /// <inheritdoc />
  public ClashAnswer AskClash(ImportClash clash)
  {
    ClashesAsked.Add(clash);
    return ClashAnswers.Count > 0
      ? ClashAnswers.Dequeue()
      : throw new InvalidOperationException($"Unexpected prompt for {clash.Item}");
  }

  /// <inheritdoc />
  public bool Confirm(string question)
  {
    Questions.Add(question);
    return Confirmations.Count > 0
      ? Confirmations.Dequeue()
      : throw new InvalidOperationException($"Unexpected question: {question}");
  }
}
