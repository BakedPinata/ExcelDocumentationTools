// vbadoc: exports the VBA from an Excel workbook and builds a page that documents it.

using Serilog;
using VbaDocumenter.Cli;

// Everything else lives in CliRunner so it can be tested.
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
  // First Ctrl+C: stop cleanly (exit code 130). Second Ctrl+C: let the runtime terminate the process immediately.
  if (!cancellation.IsCancellationRequested)
  {
    e.Cancel = true;
    cancellation.Cancel();
  }
};

try
{
  return CliRunner.Run(args, AppServices.CreateDefault(), cancellation.Token);
}
finally
{
  Log.CloseAndFlush();
}
