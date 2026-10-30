using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace VbaDocumenter.Tests.Cli;

/// <summary>
/// Captures log events in memory so tests can assert on them. It isn't in Support/ because the browser tests compile
/// those files too, and they don't use Serilog.
/// </summary>
sealed class TestLog : ILogEventSink
{
  readonly ConcurrentQueue<LogEvent> _events = new();

  public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

  public ILogger CreateLogger(LogEventLevel minimumLevel) =>
      new LoggerConfiguration().MinimumLevel.Is(minimumLevel).WriteTo.Sink(this).CreateLogger();

  /// <summary>Rendered messages at a level.</summary>
  public IReadOnlyList<string> Messages(LogEventLevel level) =>
      _events.Where(e => e.Level == level).Select(e => e.RenderMessage(CultureInfo.InvariantCulture)).ToArray();
}
