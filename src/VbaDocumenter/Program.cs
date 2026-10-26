// vbadoc: exports the VBA from an Excel workbook and (in later stages) builds a documentation page.
//
// Exit codes: 0 = success, 1 = bad command line, 2 = a problem with the workbook (message explains it),
// 3 = an unexpected error (a bug; the stack trace is printed).

using VbaDocumenter;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;

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
    foreach (var module in project.Modules)
        Console.WriteLine($"    {module.Kind,-9} {module.Name}");

    Console.WriteLine($"Exporting to {options.OutputFolder}");
    var result = ProjectExporter.Export(project, options.OutputFolder);
    Console.WriteLine($"  {result.Written.Count} written, {result.Unchanged.Count} unchanged, {result.Deleted.Count} deleted");
    foreach (var name in result.Deleted)
        Console.WriteLine($"    deleted {name} (no longer in the workbook)");
    if (result.CreatedGitAttributes)
        Console.WriteLine("  Created .gitattributes");

    if (options.EmbedSource)
        Console.WriteLine("Note: --embed-source has no effect yet; the HTML page is built in a later stage.");
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
