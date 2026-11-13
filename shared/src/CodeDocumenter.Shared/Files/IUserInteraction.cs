using CodeDocumenter.Shared.Importing;

namespace CodeDocumenter.Shared.Files;

/// <summary>An answer to the "file already exists" prompt.</summary>
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

  /// <summary>Asks what to do with an item the target already has. Overwrite is only offered if it can be replaced.</summary>
  ClashAnswer AskClash(ImportClash clash);

  /// <summary>Asks whether to go ahead with something that can't be undone. Anything but yes is no.</summary>
  /// <param name="question">The question, saying what will happen.</param>
  bool Confirm(string question);
}
