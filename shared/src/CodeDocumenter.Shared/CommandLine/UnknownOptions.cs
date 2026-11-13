using System.CommandLine;
using System.CommandLine.Parsing;

namespace CodeDocumenter.Shared.CommandLine;

/// <summary>
/// Words on the command line that look like options but aren't any of the command's. System.CommandLine takes such a word
/// for an argument, so <c>vbadoc --nope</c> would look for a workbook called "--nope", and <c>vbadoc --nope Sales.xlsm</c>
/// would complain about Sales.xlsm. The tools report them as unknown options instead, which is a usage error.
/// </summary>
public static class UnknownOptions
{
  /// <summary>A message for each word that looks like an option but isn't one, in order; empty if there are none.</summary>
  /// <param name="parseResult">The command line, parsed.</param>
  /// <param name="args">The words it was parsed from, as written.</param>
  /// <remarks>
  /// An option's value may start with a dash (<c>-o -drafts</c>), and anything after <c>--</c> is an argument
  /// (<c>vbadoc -- -draft.xlsm</c>), so neither is counted. Once a word has been taken for an argument, the parse's own
  /// errors may be about the wrong word, so these are reported in place of them.
  /// </remarks>
  public static IReadOnlyList<string> Find(ParseResult parseResult, IReadOnlyList<string> args)
  {
    ArgumentNullException.ThrowIfNull(parseResult);
    ArgumentNullException.ThrowIfNull(args);

    var values = new HashSet<Token>(ReferenceEqualityComparer.Instance);
    CollectOptionValues(parseResult.RootCommandResult, values);
    var written = args.TakeWhile(word => word != "--").ToList();

    var messages = new List<string>();
    foreach (var token in parseResult.Tokens)
    {
      if (token.Type == TokenType.DoubleDash)
      {
        break;
      }

      if (token.Type != TokenType.Argument || values.Contains(token))
      {
        continue;
      }

      if (LooksLikeOption(token.Value))
      {
        messages.Add($"Unknown option '{token.Value}'.");
      }
      else if (BundleWithUnknownLetters(token.Value, written) is { } bundle)
      {
        messages.Add($"Unknown option '-{token.Value[0]}' in '{bundle}'.");
      }
    }

    return messages;
  }

  // A dash on its own isn't an option.
  private static bool LooksLikeOption(string word) => word.Length > 1 && word[0] == '-';

  // System.CommandLine reads a bundle of short options such as -vx as -v followed by an argument "x", when x isn't an
  // option. So an argument that wasn't written as a word of its own, but ends such a bundle, is the bundle's unknown
  // letters. Returns the bundle, or null.
  private static string? BundleWithUnknownLetters(string value, List<string> written) =>
    value.Length == 0 || written.Contains(value)
      ? null
      : written.FirstOrDefault(word =>
        word.Length > value.Length + 1 && word[0] == '-' && word[1] != '-' && word.EndsWith(value, StringComparison.Ordinal));

  private static void CollectOptionValues(CommandResult command, HashSet<Token> values)
  {
    foreach (var child in command.Children)
    {
      switch (child)
      {
        case OptionResult option:
          values.UnionWith(option.Tokens);
          break;
        case CommandResult subcommand:
          CollectOptionValues(subcommand, values);
          break;
      }
    }
  }
}
