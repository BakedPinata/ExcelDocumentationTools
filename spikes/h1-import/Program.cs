using ExcelDocumenter.Shared.Workbooks;
using ImportSpike;
using PqReader = PowerQueryDocumenter.Core.Extraction.WorkbookReader;
using VbaReader = VbaDocumenter.Core.Extraction.WorkbookReader;

// Step H1 of the feedback plan: can the tools write modules and queries into workbooks that Excel accepts? Each
// experiment writes one workbook into out/, from the modules and queries in inputs/, then reads it back with vbadoc's
// and pqdoc's own readers. Whether Excel accepts it is for Excel to say: see README.md for what to check.
//
// Run from the repository root:
//   dotnet run --project spikes/h1-import                            every experiment, into out/
//   dotnet run --project spikes/h1-import -- inspect <workbook>      what a workbook is, labels included
//   dotnet run --project spikes/h1-import -- add <workbook> [--vba-only | --queries-only] [--output <folder>]
//                                                                    a copy of it with the modules and queries added
//   dotnet run --project spikes/h1-import -- copy <source> --modules A,B (--into <target> | --new <file>) [--no-references]
//                                                                    modules, what they use and their references
//   dotnet run --project spikes/h1-import -- dump <workbook>         its VBA project, to compare with Excel's

var repo = FindRepository();
var spike = Path.Combine(repo, "spikes", "h1-import");
var inputs = Path.Combine(spike, "inputs");
var modules = Directory.GetFiles(inputs, "*.*").Where(f => f.EndsWith(".bas") || f.EndsWith(".cls")).Order().Select(ModuleFiles.Read).ToList();
var queries = Directory.GetFiles(inputs, "*.m").Order().Select(Mashup.ReadQuery).ToList();

if (args.Length >= 2)
{
  try
  {
    switch (args[0])
    {
      case "dump":
        Dump.VbaProject(args[1]);
        return 0;
      case "inspect":
        return SpecificWorkbook.Inspect(args[1]) ? 0 : 1;
      case "add":
        return SpecificWorkbook.Add(args[1], args.Contains("--queries-only") ? [] : modules, args.Contains("--vba-only") ? [] : queries, Option("--output")) is null ? 1 : 0;
      case "copy":
        if (Option("--modules") is not { } names || (Option("--into") is null) == (Option("--new") is null))
        {
          Console.WriteLine("copy needs --modules A,B and one of --into <target> or --new <file>.");
          return 1;
        }
        return SpecificWorkbook.Copy(args[1], names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), Option("--into"), Option("--new"),
          withReferences: !args.Contains("--no-references")) is null ? 1 : 0;
    }
  }
  catch (Exception ex) when (ex is ExtractionException or InvalidOperationException or InvalidDataException or NullReferenceException)
  {
    Console.WriteLine($"Couldn't: {ex.GetType().Name}: {ex.Message}");
    return 1;
  }
}

var output = Directory.CreateDirectory(Path.Combine(spike, "out")).FullName;
var vbaFixtures = Path.Combine(repo, "vbadoc", "tests", "VbaDocumenter.Tests", "Fixtures", "Workbooks");
var pqFixtures = Path.Combine(repo, "pqdoc", "tests", "PowerQueryDocumenter.Tests", "Fixtures", "Workbooks");

Dump.CheckCompression();
var module1 = new ModuleToWrite("Module1", ModuleKind.Standard,
  "Attribute VB_Name = \"Module1\"\r\nSub TestMacro()\r\n    MsgBox \"Module1 was replaced by the H1 spike.\", vbInformation, \"H1 spike\"\r\nEnd Sub\r\n");
(string Cell, string? Text, string? Formula)[] checkCells =
[
  ("A1", "SpikeDouble(21), from the imported module, once macros are on and the sheet recalculated (Ctrl+Alt+F9):", null),
  ("B1", null, "SpikeDouble(21)"),
];

Experiment("V1-SimpleMacro-plus-modules.xlsm", Fixture(vbaFixtures, "SimpleMacro.xlsm"), p => Workbooks.AddModules(p, modules));
Experiment("V2-SimpleMacro-Module1-replaced.xlsm", Fixture(vbaFixtures, "SimpleMacro.xlsm"), p => Workbooks.AddModules(p, [module1, .. modules], ["Module1"]));
Experiment("V3-Queries-xlsx-plus-modules.xlsm", Fixture(pqFixtures, "Queries.xlsx"), p => Workbooks.AddModules(p, modules));
Experiment("V4-new-workbook-modules.xlsm", Workbooks.New(macroEnabled: true, checkCells), p => Workbooks.AddModules(p, modules));
Experiment("V5-Queries-xlsb-plus-modules.xlsb", Fixture(pqFixtures, "Queries.xlsb"), p => Workbooks.AddModules(p, modules));
Experiment("P1-Queries-plus-queries.xlsx", Fixture(pqFixtures, "Queries.xlsx"), p => Mashup.AddQueries(p, queries, withConnections: true));
Experiment("P2-Queries-plus-queries-no-connections.xlsx", Fixture(pqFixtures, "Queries.xlsx"), p => Mashup.AddQueries(p, queries, withConnections: false));
Experiment("P3-SimpleMacro-plus-queries.xlsm", Fixture(vbaFixtures, "SimpleMacro.xlsm"), p => Mashup.AddQueries(p, queries, withConnections: true));
Experiment("P4-new-workbook-queries.xlsx", Workbooks.New(macroEnabled: false, []), p => Mashup.AddQueries(p, queries, withConnections: true));
Experiment("P5-Queries-xlsb-plus-queries.xlsb", Fixture(pqFixtures, "Queries.xlsb"), p => Mashup.AddQueries(p, queries, withConnections: false));
Experiment("B1-new-workbook-modules-and-queries.xlsm", Workbooks.New(macroEnabled: true, checkCells), p =>
{
  Workbooks.AddModules(p, modules);
  Mashup.AddQueries(p, queries, withConnections: true);
});

// Library references: a source that needs one, and copying modules out of it, as the source has them.
var encoding = VbaDocumenter.Core.Extraction.CodePages.Get(1252);
var scripting = VbaProjectWriter.Registered("Scripting", @"*\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime", encoding);
var absent = VbaProjectWriter.Registered("SpikeAbsent", @"*\G{0A1B2C3D-1111-2222-3333-444455556666}#1.0#0#C:\Windows\System32\spike-absent.tlb#H1 Spike Library That Is Not Installed", encoding);
var libraryModules = Directory.GetFiles(Path.Combine(inputs, "references"), "*.bas").Order().Select(ModuleFiles.Read).ToList();
Experiment("R0-source-with-reference.xlsm", Workbooks.New(macroEnabled: true, []), p => Workbooks.AddModules(p, libraryModules, references: [scripting]));
var source = File.ReadAllBytes(Path.Combine(output, "R0-source-with-reference.xlsm"));
Experiment("R1-new-workbook-reference-copied.xlsm", Workbooks.New(macroEnabled: true, []), p =>
{
  var (taken, references) = SpecificWorkbook.Take(source, ["SpikeLibrary"], withReferences: true);
  Workbooks.AddModules(p, taken, references: references);
});
Experiment("R2-SimpleMacro-reference-copied.xlsm", Fixture(vbaFixtures, "SimpleMacro.xlsm"), p =>
{
  var (taken, references) = SpecificWorkbook.Take(source, ["SpikeLibrary"], withReferences: true);
  Workbooks.AddModules(p, taken, references: references);
});
Experiment("R3-reference-left-out.xlsm", Workbooks.New(macroEnabled: true, []), p =>
{
  var (taken, references) = SpecificWorkbook.Take(source, ["SpikeLibrary"], withReferences: false);
  Workbooks.AddModules(p, taken, references: references);
});
Experiment("R4-reference-not-installed.xlsm", Workbooks.New(macroEnabled: true, []), p =>
  Workbooks.AddModules(p, libraryModules.Where(m => m.Name == "SpikePlain").ToList(), references: [absent]));

// What inspect and add do with a label, on a stand-in with made-up ids, and with a file that's encrypted.
var labelled = Path.Combine(output, "L1-labelled-stand-in.xlsx");
SpecificWorkbook.WriteLabelledSample(Path.Combine(pqFixtures, "Queries.xlsx"), labelled);
foreach (var old in Directory.GetFiles(output, "L1-labelled-stand-in-spike*")) File.Delete(old);
Console.WriteLine("\nL1-labelled-stand-in.xlsx");
SpecificWorkbook.Inspect(labelled);
Console.WriteLine();
var withLabel = SpecificWorkbook.Add(labelled, modules, queries, null)!;
var sheetModules = VbaReader.Read(File.ReadAllBytes(withLabel)).Modules.Where(m => m.Kind == VbaDocumenter.Core.Extraction.VbaModuleKind.Document).Select(m => m.Name).ToList();
if (sheetModules.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5) throw new InvalidOperationException("Document modules clash: " + string.Join(", ", sheetModules));
Console.WriteLine("Document modules: " + string.Join(", ", sheetModules));
Console.WriteLine();
SpecificWorkbook.Inspect(withLabel);
Console.WriteLine("\nprotected_passtika.xlsb");
SpecificWorkbook.Inspect(Path.Combine(vbaFixtures, "protected_passtika.xlsb"));
SpecificWorkbook.Add(Path.Combine(vbaFixtures, "protected_passtika.xlsb"), modules, queries, output);

Console.WriteLine($"\nWrote the workbooks to {output}. README.md says what to check in Excel.");
return 0;

void Experiment(string name, OpcPackage package, Action<OpcPackage> change)
{
  change(package);
  var bytes = package.Write();
  File.WriteAllBytes(Path.Combine(output, name), bytes);
  Console.WriteLine($"\n{name}");

  // Read back with the tools' own readers: what was written must be there, word for word.
  if (package.Has("xl/vbaProject.bin"))
  {
    var project = VbaReader.Read(bytes);
    Console.WriteLine("  modules: " + string.Join(", ", project.Modules.Select(m => $"{m.Name} ({m.Kind})")));
    foreach (var written in name.StartsWith('V') || name.StartsWith('B') ? modules : [])
    {
      var read = project.Modules.Single(m => m.Name == written.Name);
      if (read.Source != written.Source) throw new InvalidOperationException($"{written.Name} reads back differently.");
    }
  }
  if (name.StartsWith('P') || name.StartsWith('B'))
  {
    var read = PqReader.Read(bytes);
    Console.WriteLine("  queries: " + string.Join(", ", read.Select(q => q.ResultType is null ? q.Name : $"{q.Name} ({q.ResultType})")));
    foreach (var q in queries)
    {
      var back = read.Single(r => r.Name == q.Name);
      if (back.Expression.ReplaceLineEndings("\r\n") != q.Expression) throw new InvalidOperationException($"{q.Name} reads back differently.");
    }
  }
}

string? Option(string name)
{
  int at = Array.IndexOf(args, name);
  return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static OpcPackage Fixture(string folder, string name) => OpcPackage.Read(File.ReadAllBytes(Path.Combine(folder, name)));

static string FindRepository()
{
  for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
  {
    if (File.Exists(Path.Combine(dir.FullName, "ExcelDocumentationTools.sln"))) return dir.FullName;
  }
  throw new InvalidOperationException("Run the spike from inside the repository.");
}
