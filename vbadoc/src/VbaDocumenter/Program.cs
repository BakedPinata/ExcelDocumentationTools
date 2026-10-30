// vbadoc: exports the VBA from an Excel workbook and (in later stages) builds a documentation page.
//
// Exit codes: 0 = success, 1 = bad command line, 2 = a problem with the workbook (message explains it),
// 3 = an unexpected error (a bug; the stack trace is printed).

using VbaDocumenter;
using VbaDocumenter.Core;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

if (args is ["--version"])
{
  Console.WriteLine(ToolVersion.Current.Description);
  return 0;
}

var options = CommandLineOptions.Parse(args, out var error);
if (options == null)
{
  Console.Error.WriteLine(error);
  Console.Error.WriteLine();
  Console.Error.WriteLine(CommandLineOptions.Usage);
  return 1;
}

try
{
  Console.WriteLine($"Reading {options.WorkbookPath}");
  var project = WorkbookReader.Read(options.WorkbookPath);
  Console.WriteLine($"  Project '{project.Name}', code page {project.CodePage}, {project.Modules.Count} module(s)");
  var parsed = ProjectParser.Parse(project);
  ConsoleReport.WriteModules(parsed, options.Verbose);

  Console.WriteLine("Analysing dependencies");
  var dependencies = DependencyAnalyzer.Analyze(parsed);
  ConsoleReport.WriteDependencies(dependencies, options.Verbose);

  Console.WriteLine($"Exporting to {options.OutputFolder}");
  var result = ProjectExporter.Export(project, options.OutputFolder);
  Console.WriteLine($"  {result.Written.Count} written, {result.Unchanged.Count} unchanged, {result.Deleted.Count} deleted");
  foreach (var name in result.Deleted)
    Console.WriteLine($"    deleted {name} (no longer in the workbook)");
  if (result.CreatedGitAttributes)
    Console.WriteLine("  Created .gitattributes");

  var html = HtmlPageWriter.Build(dependencies, project, Path.GetFileName(options.WorkbookPath), options.EmbedSource);
  bool pageWritten = HtmlPageWriter.Write(options.OutputFolder, html);
  Console.WriteLine($"  {HtmlPageWriter.FileName} {(pageWritten ? "written" : "unchanged")}" +
                    (options.EmbedSource ? " (source embedded)" : " (open it and use Load folder to see source)"));
  return 0;
}
catch (ExtractionException ex)
{
  WriteError(ex.Message);
  return 2;
}
catch (Exception ex)
{
  WriteError($"Unexpected error. Please report this with the details below.\n{ex}");
  return 3;
}

static void WriteError(string message)
{
  Console.ForegroundColor = ConsoleColor.Red;
  Console.Error.WriteLine($"Error: {message}");
  Console.ResetColor();
}
