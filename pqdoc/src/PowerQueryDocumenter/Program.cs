using PowerQueryDocumenter.Cli;
using PowerQueryDocumenter.Core.Export;
using Serilog;

// Composition root (NFR-ARCH-3). Everything else lives in CliRunner so it can be tested.
using var httpClient = PlantUmlClient.CreateHttpClient();
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
  // First Ctrl+C: stop cleanly (temporary files are removed, exit code 130).
  // Second Ctrl+C: let the runtime terminate the process immediately.
  if (!cancellation.IsCancellationRequested)
  {
    e.Cancel = true;
    cancellation.Cancel();
  }
};

try
{
  return await CliRunner.RunAsync(args, DefaultServices.Create(httpClient), cancellation.Token);
}
finally
{
  Log.CloseAndFlush();
}
