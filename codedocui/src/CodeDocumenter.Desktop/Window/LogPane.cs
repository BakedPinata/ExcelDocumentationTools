using System.Collections.Concurrent;
using CodeDocumenter.Desktop.Running;

namespace CodeDocumenter.Desktop.Window;

/// <summary>
/// The log at the foot of the window. A tool writes from its own thread, so lines are queued and added on the window's,
/// in the order they came.
/// </summary>
internal sealed class LogPane : IDisposable
{
  private readonly ConcurrentQueue<LogLine> pending = new();
  private int drainScheduled;

  public LogPane()
  {
    Box = new RichTextBox
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      BackColor = SystemColors.Window,
      BorderStyle = BorderStyle.FixedSingle,
      DetectUrls = false,
      WordWrap = false,
      Font = new Font("Consolas", 9.75f),
      Name = "Log",
      AccessibleName = "Log",
    };
    Bold = new Font(Box.Font, FontStyle.Bold);
  }

  public RichTextBox Box { get; }

  private Font Bold { get; }

  /// <summary>Everything in the pane, for Copy.</summary>
  public string Text => Box.Text;

  /// <summary>Adds a line from any thread.</summary>
  public void Post(LogLine line)
  {
    pending.Enqueue(line);
    if (Interlocked.Exchange(ref drainScheduled, 1) == 0 && Box.IsHandleCreated && !Box.IsDisposed)
    {
      Box.BeginInvoke(Drain);
    }
  }

  /// <summary>Adds every queued line. On the window's thread only.</summary>
  public void Drain()
  {
    Interlocked.Exchange(ref drainScheduled, 0);
    while (pending.TryDequeue(out var line))
    {
      Append(line);
    }
  }

  /// <summary>Adds a line after any queued ones. On the window's thread only.</summary>
  public void Add(LogLine line)
  {
    Drain();
    Append(line);
  }

  public void Clear()
  {
    pending.Clear();
    Box.Clear();
  }

  public void Dispose()
  {
    Bold.Dispose();
    Box.Dispose();
  }

  private void Append(LogLine line)
  {
    Box.SelectionStart = Box.TextLength;
    Box.SelectionLength = 0;
    Box.SelectionColor = line.Kind switch
    {
      LogKind.Error or LogKind.Failed => Color.FromArgb(0xB0, 0x2A, 0x1E),
      LogKind.Warning => Color.FromArgb(0x8A, 0x5A, 0x00),
      LogKind.Debug => SystemColors.GrayText,
      _ => SystemColors.WindowText,
    };
    Box.SelectionFont = line.Kind is LogKind.Command or LogKind.Done or LogKind.Failed ? Bold : Box.Font;
    Box.AppendText(line.Text + "\n");
    Box.SelectionStart = Box.TextLength;
    Box.ScrollToCaret();
  }
}
