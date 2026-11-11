namespace ExcelDocumenter.Desktop.Asking;

/// <summary>A question for the person running the app, with its answers in the order they're offered.</summary>
/// <param name="Heading">What it's about, in a few words.</param>
/// <param name="Text">What each answer means, or the question itself.</param>
/// <param name="Answers">The answers' labels.</param>
/// <param name="DefaultAnswer">The answer Enter gives, as an index into <paramref name="Answers"/>.</param>
internal sealed record Question(string Heading, string Text, IReadOnlyList<string> Answers, int DefaultAnswer);

/// <summary>Asks questions. The window does it in a dialog; tests answer as they're told to.</summary>
internal interface IAsk
{
  /// <summary>The answer chosen, as an index into the question's answers, or null if it was closed without one.</summary>
  /// <remarks>Called from a tool's thread, not the window's.</remarks>
  int? Ask(Question question);
}
