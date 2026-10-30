using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PowerQueryDocumenter.Tests.TestSupport;

/// <summary>Captures log events in memory so tests can assert on them.</summary>
public sealed class TestLog : ILogEventSink
{
  private readonly ConcurrentQueue<LogEvent> events = new();

  /// <summary>Every captured event, in order.</summary>
  public IReadOnlyList<LogEvent> Events => [.. events];

  /// <inheritdoc />
  public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);

  /// <summary>Creates a logger that writes to this sink.</summary>
  public ILogger CreateLogger(LogEventLevel minimumLevel = LogEventLevel.Verbose) =>
    new LoggerConfiguration().MinimumLevel.Is(minimumLevel).WriteTo.Sink(this).CreateLogger();

  /// <summary>Rendered messages at a level.</summary>
  public IReadOnlyList<string> Messages(LogEventLevel level) =>
    Events.Where(e => e.Level == level).Select(e => e.RenderMessage(CultureInfo.InvariantCulture)).ToArray();

  /// <summary>Every message, one per line, prefixed by its level. Useful in assertion messages.</summary>
  public string AllText =>
    string.Join(Environment.NewLine, Events.Select(e => $"[{e.Level}] {e.RenderMessage(CultureInfo.InvariantCulture)}"));
}
