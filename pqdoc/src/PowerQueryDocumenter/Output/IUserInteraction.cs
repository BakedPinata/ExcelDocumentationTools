namespace PowerQueryDocumenter.Output;

/// <summary>An answer to the "file already exists" prompt (FR-OVR-2).</summary>
public enum OverwriteAnswer
{
  /// <summary>Overwrite this file.</summary>
  Yes,

  /// <summary>Keep this file and skip its output.</summary>
  No,

  /// <summary>Overwrite this file and every remaining existing file without asking again.</summary>
  All,

  /// <summary>Abort without writing anything.</summary>
  Quit,
}

/// <summary>Interaction with the person running the tool, abstracted for testing.</summary>
public interface IUserInteraction
{
  /// <summary>True when the session can prompt a person (input and output are not redirected).</summary>
  bool IsInteractive { get; }

  /// <summary>Asks whether to overwrite an existing file.</summary>
  OverwriteAnswer AskOverwrite(string path);
}

/// <summary>Prompts on the console. Prompts go to stderr so that stdout stays clean.</summary>
public sealed class ConsoleUserInteraction : IUserInteraction
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
