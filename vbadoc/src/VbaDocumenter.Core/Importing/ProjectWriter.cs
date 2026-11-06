using System.Buffers.Binary;
using System.Text;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Importing;

/// <summary>A module to write into a project.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Source">Its source as the project stores it, in the project's code page.</param>
public sealed record ModuleToWrite(string Name, VbaModuleKind Kind, byte[] Source);

/// <summary>What to change in a project.</summary>
/// <param name="Add">The modules to add.</param>
/// <param name="Replace">The modules to take out first.</param>
/// <param name="References">The library references to add, which it doesn't have.</param>
public sealed record ProjectChange(IReadOnlyList<ModuleToWrite> Add, IReadOnlyCollection<string> Replace, IReadOnlyList<VbaReference> References);

/// <summary>
/// Writes modules into a VBA project (vbaProject.bin) as source only, with no compiled code, so that Excel compiles them
/// when it opens the workbook. The way to force that is MS-OVBA's own rule for writers: _VBA_PROJECT holds only its 7-byte
/// header, with version 0xFFFF, so Excel can't use any compiled code it finds, and reads every module's source instead.
/// A new module is a stream of compressed source at offset 0, its dir records, a line in PROJECT and an entry in PROJECTwm.
/// </summary>
public static class ProjectWriter
{
  static readonly byte[] ResetPerformanceCache = [0xCC, 0x61, 0xFF, 0xFF, 0x00, 0x00, 0x00];

  // dir records (MS-OVBA 2.3.4.2)
  const ushort CodePageId = 0x0003, ModulesCount = 0x000F, Terminator = 0x0010;
  const ushort ReferenceName = 0x0016, ReferenceNameUnicode = 0x003E, ReferenceRegistered = 0x000D;
  const ushort ModuleName = 0x0019, ModuleNameUnicode = 0x0047, ModuleStreamName = 0x001A, ModuleStreamNameUnicode = 0x0032, ModuleEnd = 0x002B;
  const ushort ModuleTypeProcedural = 0x0021, ModuleTypeOther = 0x0022;

  const string ProjectStream = "PROJECT", NameMapStream = "PROJECTwm", DirStream = "dir", PerformanceCache = "_VBA_PROJECT";

  /// <summary>
  /// Changes an existing project. Everything else in it stays: other modules, references, protection, and the storages
  /// behind its forms.
  /// </summary>
  public static byte[] Edit(byte[] vbaProjectBin, ProjectChange change)
  {
    ArgumentNullException.ThrowIfNull(vbaProjectBin);
    ArgumentNullException.ThrowIfNull(change);
    var buffer = new MemoryStream();
    buffer.Write(vbaProjectBin);
    buffer.Position = 0;
    using (var root = RootStorage.Open(buffer, StorageModeFlags.LeaveOpen))
    {
      var vba = root.OpenStorage("VBA");
      var dir = DirRecords.Read(OvbaDecompressor.Decompress(Read(vba, DirStream)));
      var encoding = CodePages.Get(BinaryPrimitives.ReadUInt16LittleEndian(dir.First(r => r.Id == CodePageId).Data));
      var project = encoding.GetString(Read(root, ProjectStream));
      var nameMap = root.ContainsEntry(NameMapStream) ? ReadNameMap(Read(root, NameMapStream)) : null;

      foreach (var name in change.Replace)
      {
        var (start, end) = FindModule(dir, name, encoding) ?? throw new InvalidDataException($"The project has no module {name} to replace.");
        vba.Delete(StreamNameOf(dir.GetRange(start, end - start + 1), encoding));
        dir.RemoveRange(start, end - start + 1);
        project = RemoveModuleLines(project, name);
        nameMap?.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
      }

      foreach (var module in change.Add)
      {
        if (FindModule(dir, module.Name, encoding) is not null) throw new InvalidDataException($"The project already has a module {module.Name}.");
        var stream = FreeStreamName(vba, module.Name);
        Write(vba, stream, OvbaCompressor.Compress(module.Source));
        dir.InsertRange(dir.FindIndex(r => r.Id == Terminator), ModuleRecords(module, stream, encoding));
        project = AddModuleLine(project, module);
        nameMap?.Add(module.Name);
      }

      int at = dir.FindIndex(r => r.Id == ModulesCount);
      dir.InsertRange(at, change.References.SelectMany(r => DirRecords.Read(r.Records)));
      dir.First(r => r.Id == ModulesCount).Data = BitConverter.GetBytes((ushort)dir.Count(r => r.Id == ModuleName));

      Write(vba, DirStream, OvbaCompressor.Compress(DirRecords.Write(dir)));
      Write(vba, PerformanceCache, ResetPerformanceCache);

      // The __SRP_ streams are more compiled-code cache, which nothing reads once _VBA_PROJECT is reset.
      foreach (var cache in vba.EnumerateEntries().Select(e => e.Name).Where(n => n.StartsWith("__SRP_", StringComparison.Ordinal)).ToList())
        vba.Delete(cache);

      Write(root, ProjectStream, encoding.GetBytes(project));
      if (nameMap is not null) Write(root, NameMapStream, WriteNameMap(nameMap, encoding));
    }

    return buffer.ToArray();
  }

  /// <summary>
  /// A new project holding the modules, document modules included, with the references Excel gives a new project (OLE
  /// Automation and the Office library) and any others, and no protection.
  /// </summary>
  /// <param name="modules">The modules.</param>
  /// <param name="references">The library references to add to Excel's two.</param>
  /// <param name="codePage">The code page the modules' source is in.</param>
  public static byte[] Create(IReadOnlyList<ModuleToWrite> modules, IReadOnlyList<VbaReference> references, int codePage)
  {
    ArgumentNullException.ThrowIfNull(modules);
    ArgumentNullException.ThrowIfNull(references);
    var encoding = CodePages.Get(codePage);
    var buffer = new MemoryStream();
    using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
    {
      var vba = root.CreateStorage("VBA");
      Write(vba, PerformanceCache, ResetPerformanceCache);

      var moduleRecords = new List<DirRecord>();
      foreach (var module in modules)
      {
        var stream = FreeStreamName(vba, module.Name);
        Write(vba, stream, OvbaCompressor.Compress(module.Source));
        moduleRecords.AddRange(ModuleRecords(module, stream, encoding));
      }

      var dir = NewDir(modules.Count, references, encoding, codePage);
      dir.InsertRange(dir.Count - 1, moduleRecords);
      Write(vba, DirStream, OvbaCompressor.Compress(DirRecords.Write(dir)));

      Write(root, ProjectStream, encoding.GetBytes(NewProjectText(modules)));
      Write(root, NameMapStream, WriteNameMap(modules.Select(m => m.Name), encoding));
    }

    return buffer.ToArray();
  }

  static string NewProjectText(IReadOnlyList<ModuleToWrite> modules)
  {
    var id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
    byte key = ProjectEncryption.ProjectKey(id);
    var seeds = new byte[3];
    Random.Shared.NextBytes(seeds);

    var text = new StringBuilder($"ID=\"{id}\"\r\n");
    foreach (var module in modules) text.Append(ModuleLine(module)).Append("\r\n");
    text.Append("Name=\"VBAProject\"\r\nHelpContextID=\"0\"\r\nVersionCompatible32=\"393222000\"\r\n");
    text.Append($"CMG=\"{ProjectEncryption.Encrypt([0, 0, 0, 0], key, seeds[0])}\"\r\n");  // not protected
    text.Append($"DPB=\"{ProjectEncryption.Encrypt([0], key, seeds[1])}\"\r\n");           // no password
    text.Append($"GC=\"{ProjectEncryption.Encrypt([0xFF], key, seeds[2])}\"\r\n");         // visible
    text.Append("\r\n[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n\r\n[Workspace]\r\n");
    foreach (var module in modules) text.Append($"{module.Name}=0, 0, 0, 0, C\r\n");
    return text.ToString();
  }

  static List<DirRecord> NewDir(int moduleCount, IReadOnlyList<VbaReference> references, Encoding encoding, int codePage)
  {
    var dir = new List<DirRecord>
    {
      new(0x0001, [3, 0, 0, 0]),                            // PROJECTSYSKIND: 64-bit Windows
      new(0x0002, [0x09, 0x04, 0, 0]),                      // PROJECTLCID: 1033
      new(0x0014, [0x09, 0x04, 0, 0]),                      // PROJECTLCIDINVOKE
      new(CodePageId, BitConverter.GetBytes((ushort)codePage)),
      new(0x0004, encoding.GetBytes("VBAProject")),         // PROJECTNAME
      new(0x0005, []), new(0x0040, []),                     // PROJECTDOCSTRING
      new(0x0006, []), new(0x003D, []),                     // PROJECTHELPFILEPATH
      new(0x0007, [0, 0, 0, 0]),                            // PROJECTHELPCONTEXT
      new(0x0008, [0, 0, 0, 0]),                            // PROJECTLIBFLAGS
      new(0x0009, [0x95, 0x12, 0xD1, 0x58, 0x05, 0x00]),    // PROJECTVERSION, as SimpleMacro.xlsm has it
      new(0x000C, []), new(0x003C, []),                     // PROJECTCONSTANTS
    };

    // The references Excel gives a new project, as SimpleMacro.xlsm has them; Excel finds each by its GUID.
    dir.AddRange(Registered("stdole", @"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation", encoding));
    dir.AddRange(Registered("Office", @"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#C:\Program Files\Common Files\Microsoft Shared\OFFICE14\MSO.DLL#Microsoft Office 14.0 Object Library", encoding));
    dir.AddRange(references.SelectMany(r => DirRecords.Read(r.Records)));
    dir.Add(new(ModulesCount, BitConverter.GetBytes((ushort)moduleCount)));
    dir.Add(new(0x0013, [0xFF, 0xFF]));                     // PROJECTCOOKIE
    dir.Add(new(Terminator, []));
    return dir;
  }

  static IEnumerable<DirRecord> Registered(string name, string libid, Encoding encoding)
  {
    var id = encoding.GetBytes(libid);
    return
    [
      new(ReferenceName, encoding.GetBytes(name)),
      new(ReferenceNameUnicode, Encoding.Unicode.GetBytes(name)),
      new(ReferenceRegistered, [.. BitConverter.GetBytes(id.Length), .. id, 0, 0, 0, 0, 0, 0]),
    ];
  }

  // A module's records, in the order Excel writes them (MS-OVBA 2.3.4.2.3.2), with its source at offset 0.
  static IEnumerable<DirRecord> ModuleRecords(ModuleToWrite module, string stream, Encoding encoding) =>
  [
    new(ModuleName, encoding.GetBytes(module.Name)),
    new(ModuleNameUnicode, Encoding.Unicode.GetBytes(module.Name)),
    new(ModuleStreamName, encoding.GetBytes(stream)),
    new(ModuleStreamNameUnicode, Encoding.Unicode.GetBytes(stream)),
    new(0x001C, []), new(0x0048, []),                       // MODULEDOCSTRING
    new(0x0031, [0, 0, 0, 0]),                              // MODULEOFFSET: no compiled code before the source
    new(0x001E, [0, 0, 0, 0]),                              // MODULEHELPCONTEXT
    new(0x002C, [0xFF, 0xFF]),                              // MODULECOOKIE
    new(module.Kind == VbaModuleKind.Standard ? ModuleTypeProcedural : ModuleTypeOther, []),
    new(ModuleEnd, []),
  ];

  // A module can be called Dir, but its stream can't: names in a compound file ignore case, and that one is the project's.
  static string FreeStreamName(Storage vba, string module)
  {
    var name = module;
    for (int n = 1; IsTaken(vba, name); n++) name = module + n;
    return name;
  }

  static bool IsTaken(Storage vba, string stream) =>
    stream.Equals(DirStream, StringComparison.OrdinalIgnoreCase) || stream.Equals(PerformanceCache, StringComparison.OrdinalIgnoreCase)
      || vba.EnumerateEntries().Any(e => e.Name.Equals(stream, StringComparison.OrdinalIgnoreCase));

  // The first and last record of a module, by name.
  static (int Start, int End)? FindModule(List<DirRecord> dir, string name, Encoding encoding)
  {
    for (int i = 0; i < dir.Count; i++)
    {
      if (dir[i].Id != ModuleName) continue;
      int end = dir.FindIndex(i, r => r.Id == ModuleEnd);
      var unicode = dir.GetRange(i, end - i).Find(r => r.Id == ModuleNameUnicode);
      var found = unicode is not null ? Encoding.Unicode.GetString(unicode.Data) : encoding.GetString(dir[i].Data);
      if (found.Equals(name, StringComparison.OrdinalIgnoreCase)) return (i, end);
    }

    return null;
  }

  static string StreamNameOf(List<DirRecord> module, Encoding encoding) =>
    module.Find(r => r.Id == ModuleStreamNameUnicode) is { Data.Length: > 0 } unicode
      ? Encoding.Unicode.GetString(unicode.Data)
      : encoding.GetString(module.First(r => r.Id == ModuleStreamName).Data);

  static string ModuleLine(ModuleToWrite module) => module.Kind switch
  {
    VbaModuleKind.Standard => $"Module={module.Name}",
    VbaModuleKind.Class => $"Class={module.Name}",
    _ => $"Document={module.Name}/&H00000000",
  };

  // A module's line goes after the others, before the project's own properties.
  static string AddModuleLine(string project, ModuleToWrite module)
  {
    var lines = project.Split("\r\n").ToList();
    int at = lines.FindIndex(l => l.StartsWith("HelpFile=", StringComparison.Ordinal) || l.StartsWith("ExeName32=", StringComparison.Ordinal)
      || l.StartsWith("Name=", StringComparison.Ordinal));
    lines.Insert(at < 0 ? Math.Min(1, lines.Count) : at, ModuleLine(module));
    return string.Join("\r\n", lines);
  }

  // Its line among the properties, and its line in [Workspace]. Those are kept apart, as a module can be called Name or ID.
  static string RemoveModuleLines(string project, string name)
  {
    bool Names(string line, string prefix) => line.StartsWith(prefix, StringComparison.Ordinal)
      && line[prefix.Length..].Split('/')[0].Equals(name, StringComparison.OrdinalIgnoreCase);

    var kept = new List<string>();
    string? section = null;
    foreach (var line in project.Split("\r\n"))
    {
      if (line.StartsWith('[')) section = line;
      bool itsLine = section switch
      {
        null => Names(line, "Module=") || Names(line, "Class=") || Names(line, "Document="),
        "[Workspace]" => line.Split('=')[0].Equals(name, StringComparison.OrdinalIgnoreCase),
        _ => false,
      };
      if (!itsLine) kept.Add(line);
    }

    return string.Join("\r\n", kept);
  }

  // PROJECTwm: each module's name in the code page and in UTF-16, each ending in zeros, then a zero to end the list.
  static List<string> ReadNameMap(byte[] map)
  {
    var names = new List<string>();
    int pos = 0;
    while (pos < map.Length && map[pos] != 0)
    {
      while (pos < map.Length && map[pos] != 0) pos++;
      int start = ++pos;
      while (pos + 1 < map.Length && (map[pos] != 0 || map[pos + 1] != 0)) pos += 2;
      if (pos + 1 >= map.Length) break;
      names.Add(Encoding.Unicode.GetString(map, start, pos - start));
      pos += 2;
    }

    return names;
  }

  static byte[] WriteNameMap(IEnumerable<string> names, Encoding encoding)
  {
    var output = new List<byte>();
    foreach (var name in names)
    {
      output.AddRange(encoding.GetBytes(name));
      output.Add(0);
      output.AddRange(Encoding.Unicode.GetBytes(name));
      output.Add(0);
      output.Add(0);
    }

    output.Add(0);
    output.Add(0);
    return output.ToArray();
  }

  static byte[] Read(Storage storage, string name)
  {
    using var stream = storage.OpenStream(name);
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
  }

  static void Write(Storage storage, string name, byte[] data)
  {
    if (storage.ContainsEntry(name)) storage.Delete(name);
    using var stream = storage.CreateStream(name);
    stream.Write(data);
  }
}
