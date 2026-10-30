namespace PowerQueryDocumenter.Core.Output;

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
