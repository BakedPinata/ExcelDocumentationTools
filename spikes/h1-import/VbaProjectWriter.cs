using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace ImportSpike;

/// <summary>
/// A library reference from a project's dir stream, as its records stand, so any kind can be copied to another project.
/// It's known by the GUID in its libid, or by its name if it has none (a reference to another VBA project).
/// </summary>
sealed record ProjectReference(string Name, string Key, string Kind, IReadOnlyList<Ovba.Record> Records);

/// <summary>
/// Writes modules into a VBA project (vbaProject.bin) as source only, with no compiled code, so that Excel compiles
/// them when it opens the workbook. The way to force that is MS-OVBA's own rule for writers: _VBA_PROJECT holds only
/// its 7-byte header, with version 0xFFFF, so Excel can't use any compiled code it finds, and reads every module's
/// source instead. Each new module's stream is its compressed source, at offset 0.
/// </summary>
static partial class VbaProjectWriter
{
  static readonly byte[] ResetPerformanceCache = [0xCC, 0x61, 0xFF, 0xFF, 0x00, 0x00, 0x00];

  // dir records (MS-OVBA 2.3.4.2)
  const ushort CodePageId = 0x0003, ModulesCount = 0x000F, Terminator = 0x0010;
  const ushort ModuleName = 0x0019, ModuleNameUnicode = 0x0047, ModuleStreamName = 0x001A, ModuleEnd = 0x002B;

  /// <summary>The library references of a project, in order.</summary>
  public static List<ProjectReference> ReferencesOf(byte[] vbaProjectBin)
  {
    using var root = RootStorage.Open(new MemoryStream(vbaProjectBin));
    var dir = Ovba.ReadDir(OvbaDecompressor.Decompress(Dump.Read(root.OpenStorage("VBA"), "dir")));
    return ReferencesIn(dir, CodePages.Get(BinaryPrimitives.ReadUInt16LittleEndian(dir.First(r => r.Id == CodePageId).Data)));
  }

  // They run from the first reference record to PROJECTMODULES. Each ends with the record that says what it is: a control
  // reference is several records, and can hold a name of its own, so a name record doesn't always start a new one.
  static List<ProjectReference> ReferencesIn(List<Ovba.Record> dir, Encoding encoding)
  {
    var references = new List<ProjectReference>();
    int end = dir.FindIndex(r => r.Id == ModulesCount);
    int start = dir.FindIndex(r => r.Id is 0x0016 or 0x0033 or 0x002F or 0x000D or 0x000E);
    if (start < 0 || start > end) return references;
    var records = new List<Ovba.Record>();
    for (int i = start; i < end; i++)
    {
      records.Add(dir[i]);
      if (dir[i].Id is not (0x000D or 0x000E or 0x0030)) continue;
      var name = records.FirstOrDefault(r => r.Id == 0x0016) is { } n ? encoding.GetString(n.Data) : "(no name)";
      var guid = Guid().Match(string.Concat(records.Select(r => Encoding.Latin1.GetString(r.Data))));
      references.Add(new(name, guid.Success ? guid.Value.ToUpperInvariant() : name,
        dir[i].Id switch { 0x000D => "registered", 0x000E => "another VBA project", _ => "control" }, records));
      records = [];
    }
    return references;
  }

  /// <summary>A reference to a registered type library, as Excel writes one.</summary>
  public static ProjectReference Registered(string name, string libid, Encoding encoding)
  {
    var id = encoding.GetBytes(libid);
    List<Ovba.Record> records =
    [
      new(0x0016, encoding.GetBytes(name)),                 // REFERENCENAME
      new(0x003E, Encoding.Unicode.GetBytes(name)),
      new(0x000D, [.. BitConverter.GetBytes(id.Length), .. id, 0, 0, 0, 0, 0, 0]), // REFERENCEREGISTERED
    ];
    return ReferencesIn([.. records, new(ModulesCount, [])], encoding)[0];
  }

  // Adds the references the project doesn't have yet, after those it has, and says which were new.
  static List<ProjectReference> AddReferences(List<Ovba.Record> dir, IEnumerable<ProjectReference> references, Encoding encoding)
  {
    var have = ReferencesIn(dir, encoding).Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var added = references.Where(r => have.Add(r.Key)).ToList();
    foreach (var r in added) dir.InsertRange(dir.FindIndex(x => x.Id == ModulesCount), r.Records);
    return added;
  }

  /// <summary>
  /// Adds modules to an existing project, first taking out the modules named in <paramref name="replace"/>, and any
  /// <paramref name="references"/> it doesn't have. Everything else (other modules, references, protection) is kept.
  /// </summary>
  public static byte[] Edit(byte[] vbaProjectBin, IReadOnlyList<ModuleToWrite> add, IReadOnlyCollection<string> replace, IReadOnlyList<ProjectReference>? references = null)
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

      AddReferences(dir, references ?? [], encoding);
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
  /// new project (OLE Automation and the Office library) and any others asked for, no protection, and code page 1252.
  /// </summary>
  public static byte[] Create(IReadOnlyList<ModuleToWrite> modules, IReadOnlyList<ProjectReference>? references = null)
  {
    const int CodePage = 1252;
    var encoding = CodePages.Get(CodePage);
    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      var vba = root.CreateStorage("VBA");
      Write(vba, "_VBA_PROJECT", ResetPerformanceCache);
      var dir = NewDir(modules, encoding, CodePage);
      AddReferences(dir, references ?? [], encoding);
      Write(vba, "dir", Ovba.Compress(Ovba.WriteDir(dir)));
      foreach (var m in modules) Write(vba, m.Name, Ovba.Compress(encoding.GetBytes(m.Source)));

      var id = "{" + System.Guid.NewGuid().ToString().ToUpperInvariant() + "}";
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
    dir.AddRange(Registered("stdole", @"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation", encoding).Records);
    dir.AddRange(Registered("Office", @"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#C:\Program Files\Common Files\Microsoft Shared\OFFICE14\MSO.DLL#Microsoft Office 14.0 Object Library", encoding).Records);
    dir.Add(new(ModulesCount, BitConverter.GetBytes((ushort)modules.Count)));
    dir.Add(new(0x0013, [0xFF, 0xFF]));                    // PROJECTCOOKIE
    foreach (var m in modules) dir.AddRange(ModuleRecords(m, encoding));
    dir.Add(new(Terminator, []));
    return dir;
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

  [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
  private static partial Regex Guid();
}
