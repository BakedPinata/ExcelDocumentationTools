using PowerQueryDocumenter.Core.Output;

namespace PowerQueryDocumenter.Cli;

/// <summary>Prompts on the console. Prompts go to stderr so that stdout stays clean.</summary>
internal sealed class ConsoleUserInteraction : IUserInteraction
{
  /// <inheritdoc />
  public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected && !Console.IsErrorRedirected;

  /// <inheritdoc />
  public OverwriteAnswer AskOverwrite(string path)
  {
    while (true)
    {
      Console.Error.Write($"'{path}' already exists. Overwrite? [y]es / [n]o / [a]ll / [q]uit: ");
      var answer = Console.ReadLine();

      // End of input (for example Ctrl+Z / Ctrl+D) is treated as quit.
      if (answer is null)
      {
        Console.Error.WriteLine();
        return OverwriteAnswer.Quit;
      }

      switch (answer.Trim().ToLowerInvariant())
      {
        case "y" or "yes":
          return OverwriteAnswer.Yes;
        case "n" or "no":
          return OverwriteAnswer.No;
        case "a" or "all":
          return OverwriteAnswer.All;
        case "q" or "quit":
          return OverwriteAnswer.Quit;
        default:
          Console.Error.WriteLine("Please answer y, n, a or q.");
          break;
      }
    }
  }
}
