namespace CodeDocumenter.Shared.Importing;

/// <summary>
/// Something a source has that <c>import</c> could be asked for, so another program can offer the choice the page's
/// Export tab offers. Which items can be taken is the tool's Core's to say.
/// </summary>
/// <param name="Name">The name, as the source spells it, and as <c>--items</c> takes it.</param>
/// <param name="Kind">What it is, in a word or two, such as "class" or "function".</param>
/// <param name="WhyNot">Why it can't be taken, as a sentence, or null when it can.</param>
public sealed record ImportableItem(string Name, string Kind, string? WhyNot)
{
  /// <summary>Whether it can be taken.</summary>
  public bool CanTake => WhyNot is null;
}
