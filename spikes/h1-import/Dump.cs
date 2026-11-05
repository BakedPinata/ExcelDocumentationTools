using System.IO.Compression;
using System.Text;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace ImportSpike;

/// <summary>Prints what's in a workbook's VBA project, to compare what the spike writes with what Excel writes.</summary>
static class Dump
{
  public static void VbaProject(string workbookPath)
  {
    Console.WriteLine($"== {Path.GetFileName(workbookPath)}");
    using var zip = ZipFile.OpenRead(workbookPath);
    var entry = zip.GetEntry("xl/vbaProject.bin");
    if (entry is null) { Console.WriteLine("  no xl/vbaProject.bin"); return; }
    var bin = new MemoryStream();
    using (var s = entry.Open()) s.CopyTo(bin);
    bin.Position = 0;
    using var root = RootStorage.Open(bin);
    Console.WriteLine($"  root CLSID {root.CLSID}");
    Entries(root, "  ");

    var vba = root.OpenStorage("VBA");
    var dir = Ovba.ReadDir(OvbaDecompressor.Decompress(Read(vba, "dir")));
    Console.WriteLine("  dir: " + string.Join(" ", dir.Select(r => $"{r.Id:X2}:{r.Data.Length}")));
    foreach (var r in dir.Where(r => r.Id == 0x000D))
    {
      int length = BitConverter.ToInt32(r.Data, 0);
      Console.WriteLine($"  reference: {Encoding.Latin1.GetString(r.Data, 4, length)} (then {Convert.ToHexString(r.Data.AsSpan(4 + length))})");
    }
    foreach (var r in dir.Where(r => r.Id is 0x0001 or 0x0002 or 0x0014 or 0x0009 or 0x0008))
      Console.WriteLine($"  record 0x{r.Id:X4}: {Convert.ToHexString(r.Data)}");
    var projectVba = Read(vba, "_VBA_PROJECT");
    Console.WriteLine($"  _VBA_PROJECT: {projectVba.Length} bytes, starts {Convert.ToHexString(projectVba.AsSpan(0, Math.Min(8, projectVba.Length)))}");

    var project = Encoding.Latin1.GetString(Read(root, "PROJECT"));
    Console.WriteLine("  PROJECT:");
    foreach (var line in project.Split("\r\n")) Console.WriteLine("    | " + line);
    var id = project.Split("\r\n").First(l => l.StartsWith("ID=")).Split('"')[1];
    foreach (var key in new[] { "CMG", "DPB", "GC" })
    {
      var line = project.Split("\r\n").FirstOrDefault(l => l.StartsWith(key + "="));
      if (line is null) continue;
      var (k, data) = Ovba.Decrypt(line.Split('"')[1]);
      Console.WriteLine($"  {key}: key {k:X2} (from the ID: {Ovba.ProjectKey(id):X2}, without braces {Ovba.ProjectKey(id.Trim('{', '}')):X2}), data {Convert.ToHexString(data)}");
    }
    if (root.ContainsEntry("PROJECTwm")) Console.WriteLine("  PROJECTwm: " + string.Join(", ", Ovba.ReadNameMap(Read(root, "PROJECTwm"))));

    foreach (var e in vba.EnumerateEntries().Where(e => e.Type == EntryType.Stream && dir.Any(r => r.Id == 0x001A && Encoding.Latin1.GetString(r.Data) == e.Name)))
    {
      var offset = dir.SkipWhile(r => !(r.Id == 0x001A && Encoding.Latin1.GetString(r.Data) == e.Name)).First(r => r.Id == 0x0031).Data;
      var source = Encoding.Latin1.GetString(OvbaDecompressor.Decompress(Read(vba, e.Name), BitConverter.ToInt32(offset)));
      Console.WriteLine($"  module {e.Name}, offset {BitConverter.ToInt32(offset)}:");
      foreach (var line in source.Split("\r\n").Take(12)) Console.WriteLine("    | " + line);
    }
  }

  static void Entries(Storage storage, string indent)
  {
    foreach (var e in storage.EnumerateEntries())
    {
      Console.WriteLine($"{indent}{e.Type} {e.Name} {(e.Type == EntryType.Stream ? e.Length + " bytes" : "CLSID " + e.CLSID)}");
      if (e.Type == EntryType.Storage) Entries(storage.OpenStorage(e.Name), indent + "  ");
    }
  }

  public static byte[] Read(Storage storage, string name)
  {
    using var stream = storage.OpenStream(name);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }

  /// <summary>Compresses varied text and checks vbadoc's decompressor gives it back.</summary>
  public static void CheckCompression()
  {
    var random = new Random(1);
    foreach (var size in new[] { 0, 1, 7, 100, 4095, 4096, 4097, 9000, 20000 })
    {
      var text = new StringBuilder();
      while (text.Length < size) text.Append(random.Next(4) == 0 ? $"Dim x{random.Next(1000)} As Long\r\n" : "    Call Something(1, 2)\r\n");
      var bytes = Encoding.ASCII.GetBytes(text.ToString()[..size]);
      var back = OvbaDecompressor.Decompress(Ovba.Compress(bytes));
      if (!back.AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException($"Compression round trip failed at {size} bytes.");
    }
    var noise = new byte[10000];
    random.NextBytes(noise);
    if (!OvbaDecompressor.Decompress(Ovba.Compress(noise[..8192])).AsSpan().SequenceEqual(noise[..8192]))
      throw new InvalidOperationException("Compression round trip failed on incompressible data.");
    Console.WriteLine("Compression round trips: ok");
  }
}
