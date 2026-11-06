using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Shared.CommandLine;

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

  /// <inheritdoc />
  public ClashAnswer AskClash(ImportClash clash)
  {
    ArgumentNullException.ThrowIfNull(clash);
    Console.Error.WriteLine(clash.Problem);
    if (clash.IfOverwritten is not null)
    {
      Console.Error.WriteLine($"  Overwrite: {clash.IfOverwritten}");
    }

    Console.Error.WriteLine($"  Skip: {clash.IfSkipped}");
    while (true)
    {
      Console.Error.Write(clash.CanOverwrite
        ? "[s]kip / [o]verwrite / [sa] skip all / [oa] overwrite all / [q]uit: "
        : "[s]kip / [sa] skip all / [q]uit: ");
      var answer = Console.ReadLine();

      // End of input (for example Ctrl+Z / Ctrl+D) is treated as quit.
      if (answer is null)
      {
        Console.Error.WriteLine();
        return ClashAnswer.Quit;
      }

      if (ReadClashAnswer(answer, clash.CanOverwrite) is { } read)
      {
        return read;
      }

      Console.Error.WriteLine(clash.CanOverwrite ? "Please answer s, o, sa, oa or q." : "Please answer s, sa or q.");
    }
  }

  /// <inheritdoc />
  public bool Confirm(string question)
  {
    while (true)
    {
      Console.Error.Write($"{question} [y]es / [n]o: ");
      var answer = Console.ReadLine();

      // End of input is treated as no: this is for what can't be undone.
      if (answer is null)
      {
        Console.Error.WriteLine();
        return false;
      }

      switch (answer.Trim().ToLowerInvariant())
      {
        case "y" or "yes":
          return true;
        case "n" or "no":
          return false;
        default:
          Console.Error.WriteLine("Please answer y or n.");
          break;
      }
    }
  }

  /// <summary>What a typed answer to the clash prompt means, or null if it means nothing.</summary>
  public static ClashAnswer? ReadClashAnswer(string answer, bool canOverwrite)
  {
    ArgumentNullException.ThrowIfNull(answer);
    return string.Join(' ', answer.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant() switch
    {
      "s" or "skip" => ClashAnswer.Skip,
      "sa" or "skip all" => ClashAnswer.SkipAll,
      "o" or "overwrite" when canOverwrite => ClashAnswer.Overwrite,
      "oa" or "overwrite all" when canOverwrite => ClashAnswer.OverwriteAll,
      "q" or "quit" => ClashAnswer.Quit,
      _ => null,
    };
  }
}
