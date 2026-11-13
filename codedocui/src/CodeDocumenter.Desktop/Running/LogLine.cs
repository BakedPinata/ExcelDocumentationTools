using Serilog.Events;

namespace CodeDocumenter.Desktop.Running;

/// <summary>What a line in the log pane is, which sets how it looks.</summary>
internal enum LogKind
{
  /// <summary>Text a tool wrote outside its log, such as usage after an invalid argument.</summary>
  Plain,

  Debug,

  Information,

  Warning,

  Error,

  /// <summary>The command about to run.</summary>
  Command,

  /// <summary>How a run ended, when it succeeded.</summary>
  Done,

  /// <summary>How a run ended, when it didn't.</summary>
  Failed,
}

/// <summary>One line in the log pane.</summary>
internal sealed record LogLine(string Text, LogKind Kind)
{
  /// <summary>The kind for a level of a tool's log.</summary>
  public static LogKind KindOf(LogEventLevel level) => level switch
  {
    LogEventLevel.Verbose or LogEventLevel.Debug => LogKind.Debug,
    LogEventLevel.Information => LogKind.Information,
    LogEventLevel.Warning => LogKind.Warning,
    _ => LogKind.Error,
  };
}
