using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;

namespace AddinSpike;

/// <summary>
/// Step J1 of the feedback plan: writes modules into add-ins with vbadoc's own writer, for checking in Excel. The writer
/// won't take an .xlam yet, so the spike hands it a copy named .xlsm, and puts back the add-in's content type where the
/// writer changes it. Throwaway: see README.md.
/// </summary>
public static class Program
{
  const string AddinContentType = "application/vnd.ms-excel.addin.macroEnabled.main+xml";

  public static int Main(string[] args)
  {
    var root = RepositoryRoot();
    var samples = Path.Combine(root, "vbadoc", "tests", "VbaDocumenter.Tests", "Fixtures", "Workbooks");
    switch (args)
    {
      case []:
        Write(samples, Path.Combine(root, "spikes", "j1-addins", "out"));
        return 0;
      case ["inspect", .. var files] when files.Length > 0:
        foreach (var file in files) Inspect(file);
        return 0;
      case ["compare", var a, var b]:
        Compare(a, b);
        return 0;
      case ["find", var text, .. var files] when files.Length > 0:
        return files.Sum(file => Find(text, file)) == 0 ? 0 : 1;
      default:
        Console.Error.WriteLine("Usage: (nothing) | inspect <file>... | compare <a> <b> | find <text> <file>...");
        return 2;
    }
  }

  // ---- Writing ----------------------------------------------------------------------------------------------------

  static void Write(string samples, string output)
  {
    var sources = new[] { "Toolkit.xlam", "LockedToolkit.xlam", "Empty.xlam" }.Select(f => Path.Combine(samples, f)).ToList();
    var before = sources.ToDictionary(f => f, Hash);

    var toolkit = WorkbookReader.Read(Path.Combine(samples, "Toolkit.xlam"));
    var encoding = CodePages.Get(toolkit.CodePage);
    var own = toolkit.Modules.Where(m => m.Kind != VbaModuleKind.Document).ToList();
    var printer = toolkit.Modules.Single(m => m.Name == "CustomStringPrinter");
    var standard = toolkit.Modules.Single(m => m.Name == "StandardModule");
    var references = toolkit.References.Where(r => !r.IsStandard).ToList();

    // SpikePrinter is CustomStringPrinter under another name, so its attributes are as Excel stores a class.
    var spikePrinter = Module("SpikePrinter", VbaModuleKind.Class, encoding,
      printer.Source.Replace("\"CustomStringPrinter\"", "\"SpikePrinter\"", StringComparison.Ordinal)
        .Replace("Hello from the other side", "Hello from SpikePrinter, written by the J1 spike", StringComparison.Ordinal));
    var spikeCheck = Module("SpikeCheck", VbaModuleKind.Standard, encoding, string.Join("\r\n",
      "Attribute VB_Name = \"SpikeCheck\"",
      "Option Explicit",
      "",
      "' Written by the J1 spike. Run SpikeHello from the VBA editor (F5).",
      "Public Sub SpikeHello()",
      "    Dim printer As IStringPrinter",
      "    Set printer = New SpikePrinter",
      "    MsgBox \"J1 spike: \" & ThisWorkbook.Name & vbCrLf & _",
      "           \"IsAddin: \" & ThisWorkbook.IsAddin & vbCrLf & _",
      "           printer.TheString & vbCrLf & _",
      "           \"Dictionary items: \" & printer.BuildADictionary().Count",
      "End Sub",
      ""));
    var replaced = Module("StandardModule", VbaModuleKind.Standard, encoding,
      standard.Source.Replace("Debug.Print \"Hello World!\"", "MsgBox \"StandardModule was replaced by the J1 spike\"", StringComparison.Ordinal));
    if (replaced.Source == standard.Source) throw new InvalidOperationException("StandardModule isn't as the spike expects.");

    Directory.CreateDirectory(output);
    var intoToolkit = new ModuleChange([spikeCheck, spikePrinter, replaced], ["StandardModule"], [], toolkit.CodePage);
    var withEverything = new ModuleChange([.. own, spikeCheck, spikePrinter], [], references, toolkit.CodePage);

    Save(output, "A1-Toolkit.xlam", Existing(sources[0], intoToolkit));
    Save(output, "A2-LockedToolkit.xlam", Existing(sources[1], intoToolkit));
    Save(output, "A3-Empty.xlam", AsAddin(Existing(sources[2], withEverything)));
    Save(output, "A4-New.xlam", AsAddin(TargetWorkbook.New().Write(withEverything)));

    // What `vbadoc import Toolkit.xlam --items CustomStringPrinter,StandardModule --new A5-FromAddin.xlsm` writes today.
    Save(output, "A5-FromAddin.xlsm", TargetWorkbook.New().Write(new ModuleChange(own, [], references, toolkit.CodePage)));

    Console.WriteLine();
    foreach (var (file, hash) in before)
      Console.WriteLine($"{Path.GetFileName(file)} {(Hash(file) == hash ? "unchanged" : "CHANGED")}");
  }

  static VbaModule Module(string name, VbaModuleKind kind, Encoding encoding, string source)
  {
    var bytes = encoding.GetBytes(source);
    return new VbaModule { Name = name, Kind = kind, StreamName = name, SourceBytes = bytes, Source = source };
  }

  // The writer only takes .xlsx, .xlsm and .xlsb targets, so it's given a copy named .xlsm. It changes the content type
  // only when it adds a project.
  static byte[] Existing(string addin, ModuleChange change)
  {
    var folder = Directory.CreateTempSubdirectory("j1-spike-");
    try
    {
      var copy = Path.Combine(folder.FullName, Path.GetFileNameWithoutExtension(addin) + ".xlsm");
      File.Copy(addin, copy);
      var target = TargetWorkbook.Read(copy);
      Console.WriteLine($"{Path.GetFileName(addin)}: {(target.Project is null ? "no project" : "a project")}, locked: {target.IsLocked}, signed: {target.IsSigned}");
      return target.Write(change);
    }
    finally
    {
      folder.Delete(recursive: true);
    }
  }

  static byte[] AsAddin(byte[] workbook)
  {
    var package = OpcPackage.Read(workbook);
    package.SetOverride(package.WorkbookPart, AddinContentType);
    return package.Write();
  }

  static void Save(string folder, string name, byte[] bytes)
  {
    var path = Path.Combine(folder, name);
    File.WriteAllBytes(path, bytes);
    var project = WorkbookReader.Read(path);
    Console.WriteLine($"Wrote {name}: {ContentTypeOf(bytes)}");
    Console.WriteLine($"  modules: {string.Join(", ", project.Modules.Select(m => $"{m.Name} ({m.Kind})"))}");
    Console.WriteLine($"  references: {string.Join(", ", project.References.Select(r => r.Name))}");
    var protection = Protection(ProjectStorageBytes(bytes));
    Console.WriteLine($"  {protection}");
  }

  // ---- Looking ----------------------------------------------------------------------------------------------------

  static void Inspect(string file)
  {
    var bytes = File.ReadAllBytes(file);
    Console.WriteLine($"== {Path.GetFileName(file)}: {WorkbookFile.ContainerOf(bytes)}");
    if (WorkbookFile.ContainerOf(bytes) == WorkbookContainer.Zip)
    {
      var package = OpcPackage.Read(bytes);
      Console.WriteLine($"  workbook part: {package.WorkbookPart}, {ContentTypeOf(bytes)}");
      Console.WriteLine($"  parts: {string.Join(", ", package.Names)}");
    }

    var project = ProjectStorageBytes(bytes);
    if (project is null)
    {
      Console.WriteLine("  no VBA project");
      return;
    }

    Console.WriteLine($"  {Protection(project)}");
    foreach (var (path, data) in Streams(project)) Console.WriteLine($"  {path,-40} {data.Length,7} {Convert.ToHexString(SHA256.HashData(data))[..12]}");
    foreach (var line in ProjectLines(project)) Console.WriteLine($"  | {line}");
  }

  static void Compare(string a, string b)
  {
    var (pa, pb) = (ProjectStorageBytes(File.ReadAllBytes(a)), ProjectStorageBytes(File.ReadAllBytes(b)));
    if (pa is null || pb is null)
    {
      Console.WriteLine("One of them has no VBA project.");
      return;
    }

    var (sa, sb) = (Streams(pa).ToDictionary(s => s.Path, s => s.Data), Streams(pb).ToDictionary(s => s.Path, s => s.Data));
    foreach (var path in sa.Keys.Union(sb.Keys).Order())
    {
      var state = !sa.ContainsKey(path) ? "only in the second"
        : !sb.ContainsKey(path) ? "only in the first"
        : sa[path].AsSpan().SequenceEqual(sb[path]) ? "same"
        : $"differs ({sa[path].Length} vs {sb[path].Length} bytes)";
      Console.WriteLine($"  {path,-40} {state}");
    }

    var (la, lb) = (ProjectLines(pa), ProjectLines(pb));
    foreach (var line in la.Except(lb)) Console.WriteLine($"  PROJECT - {line}");
    foreach (var line in lb.Except(la)) Console.WriteLine($"  PROJECT + {line}");
  }

  // Every place a text appears, ignoring case, as 8-bit or UTF-16 text: in each zip part, decompressed, and in each stream
  // of a compound file, whether it's the file or a part (vbaProject.bin). For checking the samples hold nothing personal.
  static int Find(string text, string file)
  {
    var found = 0;
    void Search(string where, byte[] data)
    {
      foreach (var encoding in new[] { Encoding.Latin1, Encoding.Unicode })
      {
        if (encoding.GetString(data).Contains(text, StringComparison.OrdinalIgnoreCase)
          || (encoding == Encoding.Unicode && data.Length > 1 && encoding.GetString(data, 1, data.Length - 1).Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
          Console.WriteLine($"  {Path.GetFileName(file)}: {where} ({encoding.WebName})");
          found++;
        }
      }

      if (WorkbookFile.ContainerOf(data) != WorkbookContainer.CompoundFile) return;
      using var root = RootStorage.Open(new MemoryStream(data));
      void Walk(Storage storage, string prefix)
      {
        foreach (var entry in storage.EnumerateEntries())
        {
          if (entry.Type == EntryType.Storage) Walk(storage.OpenStorage(entry.Name), $"{prefix}{entry.Name}/");
          else Search($"{where} > {prefix}{entry.Name}", Read(storage, entry.Name));
        }
      }

      Walk(root, string.Empty);
    }

    var bytes = File.ReadAllBytes(file);
    if (WorkbookFile.ContainerOf(bytes) == WorkbookContainer.Zip)
    {
      var package = OpcPackage.Read(bytes);
      foreach (var part in package.Names) Search(part, package.Get(part));
    }
    else
    {
      Search("the file", bytes);
    }

    return found;
  }

  // The bytes of a compound file whose root holds the "VBA" storage and the "PROJECT" stream: an .xlsm's or .xlam's
  // vbaProject.bin, or, for an .xls or .xla, the file itself (whose project is in _VBA_PROJECT_CUR).
  static byte[]? ProjectStorageBytes(byte[] file)
  {
    if (WorkbookFile.ContainerOf(file) == WorkbookContainer.CompoundFile) return file;
    var package = OpcPackage.Read(file);
    var part = package.Names.FirstOrDefault(n => n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));
    return part is null ? null : package.Get(part);
  }

  static Storage ProjectStorage(RootStorage root) =>
    root.TryOpenStorage("_VBA_PROJECT_CUR", out var legacy) && legacy is not null ? legacy : root;

  static List<(string Path, byte[] Data)> Streams(byte[] compoundFile)
  {
    using var root = RootStorage.Open(new MemoryStream(compoundFile));
    var streams = new List<(string, byte[])>();
    void Walk(Storage storage, string prefix)
    {
      foreach (var entry in storage.EnumerateEntries().OrderBy(e => e.Name, StringComparer.Ordinal))
      {
        var name = string.Concat(entry.Name.Select(c => c < ' ' ? $"\\x{(int)c:X2}" : c.ToString()));
        if (entry.Type == EntryType.Storage) Walk(storage.OpenStorage(entry.Name), prefix + name + "/");
        else streams.Add((prefix + name, Read(storage, entry.Name)));
      }
    }

    Walk(ProjectStorage(root), string.Empty);
    return streams;
  }

  static List<string> ProjectLines(byte[] compoundFile)
  {
    using var root = RootStorage.Open(new MemoryStream(compoundFile));
    var storage = ProjectStorage(root);
    return storage.ContainsEntry("PROJECT")
      ? [.. Encoding.Latin1.GetString(Read(storage, "PROJECT")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)]
      : [];
  }

  // CMG is the protection state, DPB the password and GC whether the project can be seen ([MS-OVBA] 2.3.1.15–17).
  static string Protection(byte[]? compoundFile)
  {
    if (compoundFile is null) return "no VBA project";
    var lines = ProjectLines(compoundFile).Where(l => l.StartsWith("CMG=", StringComparison.Ordinal) || l.StartsWith("DPB=", StringComparison.Ordinal) || l.StartsWith("GC=", StringComparison.Ordinal))
      .ToDictionary(l => l.Split('=')[0], l => l.Split('"').ElementAtOrDefault(1) ?? string.Empty);
    byte[] Value(string key) => lines.TryGetValue(key, out var hex) && hex.Length > 0 ? ProjectEncryption.Decrypt(hex).Data : [];
    var (cmg, dpb, gc) = (Value("CMG"), Value("DPB"), Value("GC"));
    return $"protection state: {Convert.ToHexString(cmg)}, password: {(dpb.Length > 1 ? $"yes ({dpb.Length} bytes)" : "none")}, "
      + $"visible: {(gc.Length == 1 ? (gc[0] == 0xFF ? "yes" : $"no ({gc[0]:X2})") : $"? ({gc.Length} bytes)")}";
  }

  static string ContentTypeOf(byte[] zip)
  {
    var package = OpcPackage.Read(zip);
    var part = "/" + package.WorkbookPart;
    var types = package.Xml(OpcPackage.ContentTypesPart).Root!;
    return (string?)types.Elements(OpcPackage.ContentTypes + "Override").FirstOrDefault(e => (string?)e.Attribute("PartName") == part)?.Attribute("ContentType")
      ?? "(no override)";
  }

  static byte[] Read(Storage storage, string name)
  {
    using var stream = storage.OpenStream(name);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }

  static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

  static string RepositoryRoot()
  {
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
      if (File.Exists(Path.Combine(folder.FullName, "ExcelDocumentationTools.sln"))) return folder.FullName;
    throw new InvalidOperationException("Run the spike from inside the repository.");
  }
}
