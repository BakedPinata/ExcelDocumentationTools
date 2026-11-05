using ImportSpike;
using PqReader = PowerQueryDocumenter.Core.Extraction.WorkbookReader;
using VbaReader = VbaDocumenter.Core.Extraction.WorkbookReader;

// Step H1 of the feedback plan: can the tools write modules and queries into workbooks that Excel accepts? Each
// experiment writes one workbook into out/, from the modules and queries in inputs/, then reads it back with vbadoc's
// and pqdoc's own readers. Whether Excel accepts it is for Excel to say: see README.md for what to check.
//
// Run from the repository root:  dotnet run --project spikes/h1-import [-- dump <workbook>]

var repo = FindRepository();
if (args.Length == 2 && args[0] == "dump")
{
  Dump.VbaProject(args[1]);
  return;
}

var spike = Path.Combine(repo, "spikes", "h1-import");
var output = Directory.CreateDirectory(Path.Combine(spike, "out")).FullName;
var inputs = Path.Combine(spike, "inputs");
var vbaFixtures = Path.Combine(repo, "vbadoc", "tests", "VbaDocumenter.Tests", "Fixtures", "Workbooks");
var pqFixtures = Path.Combine(repo, "pqdoc", "tests", "PowerQueryDocumenter.Tests", "Fixtures", "Workbooks");

Dump.CheckCompression();
var modules = Directory.GetFiles(inputs, "*.*").Where(f => f.EndsWith(".bas") || f.EndsWith(".cls")).Order().Select(ModuleFiles.Read).ToList();
var queries = Directory.GetFiles(inputs, "*.m").Order().Select(Mashup.ReadQuery).ToList();
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
Console.WriteLine($"\nWrote the workbooks to {output}. README.md says what to check in Excel.");

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

static OpcPackage Fixture(string folder, string name) => OpcPackage.Read(File.ReadAllBytes(Path.Combine(folder, name)));

static string FindRepository()
{
  for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
  {
    if (File.Exists(Path.Combine(dir.FullName, "ExcelDocumentationTools.sln"))) return dir.FullName;
  }
  throw new InvalidOperationException("Run the spike from inside the repository.");
}
