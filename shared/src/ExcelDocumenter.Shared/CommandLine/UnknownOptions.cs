using System.CommandLine;
using System.CommandLine.Parsing;

namespace ExcelDocumenter.Shared.CommandLine;

/// <summary>
/// Words on the command line that look like options but aren't any of the command's. System.CommandLine takes such a word
/// for an argument, so <c>vbadoc --nope</c> would look for a workbook called "--nope", and <c>vbadoc --nope Sales.xlsm</c>
/// would complain about Sales.xlsm. The tools report them as unknown options instead, which is a usage error.
/// </summary>
public static class UnknownOptions
{
  /// <summary>A message for each word that looks like an option but isn't one, in order; empty if there are none.</summary>
  /// <remarks>
  /// An option's value may start with a dash (<c>-o -drafts</c>), and anything after <c>--</c> is an argument
  /// (<c>vbadoc -- -draft.xlsm</c>), so neither is counted. Once a word has been taken for an argument, the parse's own
  /// errors may be about the wrong word, so these are reported in place of them.
  /// </remarks>
  public static IReadOnlyList<string> Find(ParseResult parseResult)
  {
    ArgumentNullException.ThrowIfNull(parseResult);

    var values = new HashSet<Token>(ReferenceEqualityComparer.Instance);
    CollectOptionValues(parseResult.RootCommandResult, values);

    var messages = new List<string>();
    foreach (var token in parseResult.Tokens)
    {
      if (token.Type == TokenType.DoubleDash)
      {
        break;
      }

      if (token.Type == TokenType.Argument && LooksLikeOption(token.Value) && !values.Contains(token))
      {
        messages.Add($"Unknown option '{token.Value}'.");
      }
    }

    return messages;
  }

  // A dash on its own isn't an option.
  private static bool LooksLikeOption(string word) => word.Length > 1 && word[0] == '-';

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
