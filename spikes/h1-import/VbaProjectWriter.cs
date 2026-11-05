using System.Buffers.Binary;
using System.Text;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace ImportSpike;

/// <summary>
/// Writes modules into a VBA project (vbaProject.bin) as source only, with no compiled code, so that Excel compiles
/// them when it opens the workbook. The way to force that is MS-OVBA's own rule for writers: _VBA_PROJECT holds only
/// its 7-byte header, with version 0xFFFF, so Excel can't use any compiled code it finds, and reads every module's
/// source instead. Each new module's stream is its compressed source, at offset 0.
/// </summary>
static class VbaProjectWriter
{
  static readonly byte[] ResetPerformanceCache = [0xCC, 0x61, 0xFF, 0xFF, 0x00, 0x00, 0x00];

  // dir records (MS-OVBA 2.3.4.2)
  const ushort CodePageId = 0x0003, ModulesCount = 0x000F, Terminator = 0x0010;
  const ushort ModuleName = 0x0019, ModuleNameUnicode = 0x0047, ModuleStreamName = 0x001A, ModuleEnd = 0x002B;

  /// <summary>
  /// Adds modules to an existing project, first taking out the modules named in <paramref name="replace"/>. Everything
  /// else (other modules, references, protection) is kept as it is.
  /// </summary>
  public static byte[] Edit(byte[] vbaProjectBin, IReadOnlyList<ModuleToWrite> add, IReadOnlyCollection<string> replace)
  {
    var buffer = new MemoryStream();
    buffer.Write(vbaProjectBin);
    buffer.Position = 0;
    using (var root = RootStorage.Open(buffer, StorageModeFlags.LeaveOpen))
    {
      var vba = root.OpenStorage("VBA");
      var dir = Ovba.ReadDir(OvbaDecompressor.Decompress(Dump.Read(vba, "dir")));
      var encoding = CodePages.Get(BinaryPrimitives.ReadUInt16LittleEndian(dir.First(r => r.Id == CodePageId).Data));
      var project = encoding.GetString(Dump.Read(root, "PROJECT"));
      var nameMap = root.ContainsEntry("PROJECTwm") ? Ovba.ReadNameMap(Dump.Read(root, "PROJECTwm")) : [];

      foreach (var name in replace)
      {
        var (start, end) = FindModule(dir, name, encoding) ?? throw new InvalidOperationException($"No module {name} to replace.");
        var streamName = encoding.GetString(dir.Skip(start).First(r => r.Id == ModuleStreamName).Data);
        vba.Delete(streamName);
        dir.RemoveRange(start, end - start + 1);
        project = RemoveModuleLines(project, name);
        nameMap.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
      }

      foreach (var m in add)
      {
        if (FindModule(dir, m.Name, encoding) is not null) throw new InvalidOperationException($"The project already has a module {m.Name}.");
        dir.InsertRange(dir.FindIndex(r => r.Id == Terminator), ModuleRecords(m, encoding));
        Write(vba, m.Name, Ovba.Compress(encoding.GetBytes(m.Source)));
        project = AddModuleLine(project, m);
        nameMap.Add(m.Name);
      }

      int count = dir.Count(r => r.Id == ModuleName);
      dir.First(r => r.Id == ModulesCount).Data = BitConverter.GetBytes((ushort)count);
      Write(vba, "dir", Ovba.Compress(Ovba.WriteDir(dir)));
      Write(vba, "_VBA_PROJECT", ResetPerformanceCache);
      // The __SRP_ streams are more compiled-code cache, which nothing reads once _VBA_PROJECT is reset.
      foreach (var srp in vba.EnumerateEntries().Where(e => e.Name.StartsWith("__SRP_", StringComparison.Ordinal)).Select(e => e.Name).ToList())
      {
        vba.Delete(srp);
      }
      Write(root, "PROJECT", encoding.GetBytes(project));
      Write(root, "PROJECTwm", Ovba.WriteNameMap(nameMap, encoding));
    }
    return buffer.ToArray();
  }

  /// <summary>
  /// A new project holding <paramref name="modules"/>, document modules included, with the references Excel gives a
  /// new project (OLE Automation and the Office library), no protection, and code page 1252.
  /// </summary>
  public static byte[] Create(IReadOnlyList<ModuleToWrite> modules)
  {
    const int CodePage = 1252;
    var encoding = CodePages.Get(CodePage);
    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      var vba = root.CreateStorage("VBA");
      Write(vba, "_VBA_PROJECT", ResetPerformanceCache);
      Write(vba, "dir", Ovba.Compress(Ovba.WriteDir(NewDir(modules, encoding, CodePage))));
      foreach (var m in modules) Write(vba, m.Name, Ovba.Compress(encoding.GetBytes(m.Source)));

      var id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
      byte key = Ovba.ProjectKey(id);
      var seeds = new byte[3];
      Random.Shared.NextBytes(seeds);
      var text = new StringBuilder($"ID=\"{id}\"\r\n");
      foreach (var m in modules) text.Append(ModuleLine(m)).Append("\r\n");
      text.Append("Name=\"VBAProject\"\r\nHelpContextID=\"0\"\r\nVersionCompatible32=\"393222000\"\r\n");
      text.Append($"CMG=\"{Ovba.Encrypt([0, 0, 0, 0], key, seeds[0])}\"\r\n");  // not protected
      text.Append($"DPB=\"{Ovba.Encrypt([0], key, seeds[1])}\"\r\n");           // no password
      text.Append($"GC=\"{Ovba.Encrypt([0xFF], key, seeds[2])}\"\r\n");         // visible
      text.Append("\r\n[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n\r\n[Workspace]\r\n");
      foreach (var m in modules) text.Append($"{m.Name}=0, 0, 0, 0, C\r\n");
      Write(root, "PROJECT", encoding.GetBytes(text.ToString()));
      Write(root, "PROJECTwm", Ovba.WriteNameMap(modules.Select(m => m.Name), encoding));
    }
    return buffer.ToArray();
  }

  static List<Ovba.Record> NewDir(IReadOnlyList<ModuleToWrite> modules, Encoding encoding, int codePage)
  {
    var dir = new List<Ovba.Record>
    {
      new(0x0001, [3, 0, 0, 0]),                          // PROJECTSYSKIND: 64-bit Windows
      new(0x0002, [0x09, 0x04, 0, 0]),                    // PROJECTLCID: 1033
      new(0x0014, [0x09, 0x04, 0, 0]),                    // PROJECTLCIDINVOKE
      new(0x0003, BitConverter.GetBytes((ushort)codePage)), // PROJECTCODEPAGE
      new(0x0004, encoding.GetBytes("VBAProject")),       // PROJECTNAME
      new(0x0005, []), new(0x0040, []),                   // PROJECTDOCSTRING
      new(0x0006, []), new(0x003D, []),                   // PROJECTHELPFILEPATH
      new(0x0007, [0, 0, 0, 0]),                          // PROJECTHELPCONTEXT
      new(0x0008, [0, 0, 0, 0]),                          // PROJECTLIBFLAGS
      new(0x0009, [0x95, 0x12, 0xD1, 0x58, 0x05, 0x00]),  // PROJECTVERSION, as SimpleMacro.xlsm has it
      new(0x000C, []), new(0x003C, []),                   // PROJECTCONSTANTS
    };
    // The references Excel gives a new project, as SimpleMacro.xlsm has them; Excel finds each by its GUID.
    AddReference(dir, "stdole", @"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation", encoding);
    AddReference(dir, "Office", @"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#C:\Program Files\Common Files\Microsoft Shared\OFFICE14\MSO.DLL#Microsoft Office 14.0 Object Library", encoding);
    dir.Add(new(ModulesCount, BitConverter.GetBytes((ushort)modules.Count)));
    dir.Add(new(0x0013, [0xFF, 0xFF]));                    // PROJECTCOOKIE
    foreach (var m in modules) dir.AddRange(ModuleRecords(m, encoding));
    dir.Add(new(Terminator, []));
    return dir;
  }

  static void AddReference(List<Ovba.Record> dir, string name, string libid, Encoding encoding)
  {
    dir.Add(new(0x0016, encoding.GetBytes(name)));       // REFERENCENAME
    dir.Add(new(0x003E, Encoding.Unicode.GetBytes(name)));
    var id = encoding.GetBytes(libid);
    dir.Add(new(0x000D, [.. BitConverter.GetBytes(id.Length), .. id, 0, 0, 0, 0, 0, 0])); // REFERENCEREGISTERED
  }

  // A module's records, in the order Excel writes them (MS-OVBA 2.3.4.2.3.2), with its source at offset 0.
  static IEnumerable<Ovba.Record> ModuleRecords(ModuleToWrite m, Encoding encoding) =>
  [
    new(ModuleName, encoding.GetBytes(m.Name)),
    new(ModuleNameUnicode, Encoding.Unicode.GetBytes(m.Name)),
    new(ModuleStreamName, encoding.GetBytes(m.Name)),
    new(0x0032, Encoding.Unicode.GetBytes(m.Name)),
    new(0x001C, []), new(0x0048, []),                     // MODULEDOCSTRING
    new(0x0031, [0, 0, 0, 0]),                            // MODULEOFFSET: no compiled code before the source
    new(0x001E, [0, 0, 0, 0]),                            // MODULEHELPCONTEXT
    new(0x002C, [0xFF, 0xFF]),                            // MODULECOOKIE
    new(m.Kind == ModuleKind.Standard ? (ushort)0x0021 : (ushort)0x0022, []), // MODULETYPE: procedural, or not
    new(ModuleEnd, []),
  ];

  // The first and last record of a module, by name.
  static (int Start, int End)? FindModule(List<Ovba.Record> dir, string name, Encoding encoding)
  {
    for (int i = 0; i < dir.Count; i++)
    {
      if (dir[i].Id != ModuleName) continue;
      int end = dir.FindIndex(i, r => r.Id == ModuleEnd);
      var unicode = dir.Skip(i).Take(end - i).FirstOrDefault(r => r.Id == ModuleNameUnicode);
      var found = unicode is not null ? Encoding.Unicode.GetString(unicode.Data) : encoding.GetString(dir[i].Data);
      if (found.Equals(name, StringComparison.OrdinalIgnoreCase)) return (i, end);
    }
    return null;
  }

  static string ModuleLine(ModuleToWrite m) => m.Kind switch
  {
    ModuleKind.Standard => $"Module={m.Name}",
    ModuleKind.Class => $"Class={m.Name}",
    _ => $"Document={m.Name}/&H00000000",
  };

  // A module's line goes after the others, before the project's own properties.
  static string AddModuleLine(string project, ModuleToWrite m)
  {
    var lines = project.Split("\r\n").ToList();
    int at = lines.FindIndex(l => l.StartsWith("HelpFile=", StringComparison.Ordinal) || l.StartsWith("ExeName32=", StringComparison.Ordinal)
      || l.StartsWith("Name=", StringComparison.Ordinal));
    lines.Insert(at < 0 ? 1 : at, ModuleLine(m));
    return string.Join("\r\n", lines);
  }

  static string RemoveModuleLines(string project, string name)
  {
    bool Names(string line, string prefix) => line.StartsWith(prefix, StringComparison.Ordinal)
      && line[prefix.Length..].Split('/')[0].Equals(name, StringComparison.OrdinalIgnoreCase);
    return string.Join("\r\n", project.Split("\r\n").Where(l =>
      !Names(l, "Module=") && !Names(l, "Class=") && !Names(l, "Document=") && !(l.Split('=')[0].Equals(name, StringComparison.OrdinalIgnoreCase))));
  }

  static void Write(Storage storage, string name, byte[] data)
  {
    if (storage.ContainsEntry(name)) storage.Delete(name);
    using var stream = storage.CreateStream(name);
    stream.Write(data);
  }
}
