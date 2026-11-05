namespace PowerQueryDocumenter.Core.Model;

/// <summary>
/// A step of a query's outermost <c>let</c>, or its <c>in</c> expression (step E4 of the feedback plan), for the details
/// panel.
/// </summary>
/// <param name="Name">The step's name, unescaped, or null for the <c>in</c> expression.</param>
/// <param name="Line">The line its name (or the <c>in</c>) is on, from 1, in the query's text.</param>
/// <param name="Uses">The queries (and functions) it uses, in the order first used.</param>
public sealed record QueryStep(string? Name, int Line, IReadOnlyList<string> Uses);
