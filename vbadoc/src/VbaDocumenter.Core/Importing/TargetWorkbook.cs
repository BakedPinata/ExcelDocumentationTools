using System.Text;
using System.Xml.Linq;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Importing;

/// <summary>What to write into a workbook.</summary>
/// <param name="Add">The modules to add, as the source project has them.</param>
/// <param name="Replace">The modules of the target's to take out first.</param>
/// <param name="References">The library references to add.</param>
/// <param name="SourceCodePage">The code page the modules' source is in.</param>
public sealed record ModuleChange(IReadOnlyList<VbaModule> Add, IReadOnlyCollection<string> Replace, IReadOnlyList<VbaReference> References, int SourceCodePage);

/// <summary>
/// The workbook modules are written into: an existing one, read here and changed only in memory, or a new one. Only the
/// parts that have to change are touched, so a sensitivity label, which is metadata, comes through as it was.
/// </summary>
public sealed class TargetWorkbook
{
  const string VbaProjectRelationship = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
  const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
  const string Signature = "vbaProjectSignature";

  const string WorkbookBase = "\"0{00020819-0000-0000-C000-000000000046}\"";
  const string WorksheetBase = "\"0{00020820-0000-0000-C000-000000000046}\"";

  readonly OpcPackage _package;
  readonly string? _projectPart;

  TargetWorkbook(OpcPackage package, string? projectPart, VbaProject? project)
  {
    _package = package;
    _projectPart = projectPart;
    Project = project;
  }

  /// <summary>Its VBA project, or null when it has none.</summary>
  public VbaProject? Project { get; }

  /// <summary>Whether its VBA project is signed, so that changing it would break the signature.</summary>
  public bool IsSigned => _package.Names.Any(n => n.Contains(Signature, StringComparison.OrdinalIgnoreCase));

  /// <summary>Whether its VBA project is locked. It stays that way: a locked project can gain modules.</summary>
  public bool IsLocked => _projectPart is not null && HasProtection(_package.Get(_projectPart));

  /// <summary>
  /// The code names of the workbook and its sheets, when it has no project, as they'd take those names from any module.
  /// With a project they're its document modules. An .xlsb keeps them in its binary parts, so it has none to give.
  /// </summary>
  public IReadOnlyList<string> CodeNames => _projectPart is null && IsXml(_package.WorkbookPart) ? ReadCodeNames(_package.WorkbookPart) : [];

  /// <summary>What modules can be written into: the workbooks both tools write into, and add-ins (step J1).</summary>
  public static IReadOnlyList<string> Extensions { get; } = [.. TargetFile.Workbooks, ".xlam"];

  /// <summary>The kinds of new file <see cref="New"/> builds, with their dots: a workbook, or an add-in.</summary>
  public static IReadOnlyList<string> NewExtensions { get; } = [".xlsm", ".xlam"];

  /// <summary>Whether it's an add-in, which it stays, project or not.</summary>
  public bool IsAddin => _package.IsAddin;

  /// <summary>A new workbook with one sheet.</summary>
  /// <param name="addin">Whether it's an add-in, which is only a workbook with another content type.</param>
  public static TargetWorkbook New(bool addin = false)
  {
    var package = BlankWorkbook.Build(macroEnabled: true);
    if (addin) package.SetOverride(package.WorkbookPart, BlankWorkbook.AddinContentType);
    return new(package, null, null);
  }

  /// <summary>Reads a workbook to write into. It's only read: the result is the bytes <see cref="Write"/> returns.</summary>
  /// <exception cref="ExtractionException">It can't be a target, and says why.</exception>
  public static TargetWorkbook Read(string path)
  {
    var bytes = TargetFile.ReadBytes(path, "modules", ".xlsm", Extensions);
    var package = OpcPackage.Read(bytes);
    var part = ProjectPartOf(package);
    return new TargetWorkbook(package, part, part is null ? null : WorkbookReader.Read(bytes));
  }

  /// <summary>
  /// Makes the change, reads the result back and returns it, without touching any file. A signed project loses its
  /// signature, which the change would break anyway, so only call this for one once it's been agreed.
  /// </summary>
  /// <exception cref="ExtractionException">What was written didn't read back, or a module can't be held by the target.</exception>
  public byte[] Write(ModuleChange change)
  {
    ArgumentNullException.ThrowIfNull(change);
    var codePage = Project?.CodePage ?? change.SourceCodePage;
    var modules = change.Add.Select(m => new ModuleToWrite(m.Name, m.Kind, SourceIn(m, change.SourceCodePage, codePage))).ToList();

    if (IsSigned) RemoveSignature();
    if (_projectPart is { } part)
      _package.Set(part, ProjectWriter.Edit(_package.Get(part), new ProjectChange(modules, change.Replace, change.References)));
    else
      AddProject(modules, change.References, codePage);

    var output = _package.Write();
    var kept = (Project?.Modules ?? []).Where(m => !change.Replace.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).Select(m => (m.Name, m.SourceBytes));
    CheckReadsBack(output, [.. kept, .. modules.Select(m => (m.Name, m.Source))]);
    return output;
  }

  /// <summary>Reads a written workbook back: every module expected has to be there, word for word.</summary>
  /// <exception cref="ExtractionException">One isn't.</exception>
  public static void CheckReadsBack(byte[] output, IEnumerable<(string Name, byte[] Source)> expected)
  {
    ArgumentNullException.ThrowIfNull(expected);
    var read = WorkbookReader.Read(output).Modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    foreach (var (name, source) in expected)
    {
      if (!read.TryGetValue(name, out var module) || !module.SourceBytes.AsSpan().SequenceEqual(source))
        throw new ExtractionException($"The result didn't read back as written ({name}), so nothing was written.");
    }
  }

  /// <summary>A module's source in the target's code page. Bytes are kept as they are unless it differs.</summary>
  /// <exception cref="ExtractionException">It has characters the target's code page can't hold.</exception>
  public static byte[] SourceIn(VbaModule module, int sourceCodePage, int targetCodePage)
  {
    ArgumentNullException.ThrowIfNull(module);
    if (sourceCodePage == targetCodePage) return module.SourceBytes;

    _ = CodePages.Get(targetCodePage); // says so if it's one this tool can't use
    var strict = Encoding.GetEncoding(targetCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    try
    {
      return strict.GetBytes(module.Source);
    }
    catch (EncoderFallbackException ex)
    {
      throw new ExtractionException(
        $"{module.Name} has a character the target's VBA project can't hold, as it's in code page {targetCodePage} and the source in {sourceCodePage}. Nothing was written.", ex);
    }
  }

  static string? ProjectPartOf(OpcPackage package)
  {
    var workbook = package.WorkbookPart;
    var target = package.RelationshipsOf(workbook).FirstOrDefault(r => r.Type == VbaProjectRelationship).Target;
    return target is null ? null : OpcPackage.Resolve(workbook, target);
  }

  static bool IsXml(string part) => part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

  // A locked project's CMG says so: it's all zeros when nothing protects it. It only decides what's said, so a value that
  // can't be read counts as not locked.
  static bool HasProtection(byte[] vbaProjectBin)
  {
    using var root = RootStorage.Open(new MemoryStream(vbaProjectBin));
    if (!root.ContainsEntry("PROJECT")) return false;
    using var stream = root.OpenStream("PROJECT");
    var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    var line = Encoding.Latin1.GetString(buffer.ToArray()).Split("\r\n").FirstOrDefault(l => l.StartsWith("CMG=\"", StringComparison.Ordinal));
    try
    {
      return line is not null && ProjectEncryption.Decrypt(line.Split('"')[1]).Data.Any(b => b != 0);
    }
    catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
    {
      return false;
    }
  }

  // The signature's parts are tied to the project by its own relationships, and each has a type of its own.
  void RemoveSignature()
  {
    foreach (var name in _package.Names.Where(n => n.Contains(Signature, StringComparison.OrdinalIgnoreCase) && !n.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToList())
    {
      _package.Remove(name);
      _package.RemoveOverride(name);
    }

    if (_projectPart is not null) _package.RemoveRelationships(_projectPart, type => type.Contains(Signature, StringComparison.OrdinalIgnoreCase));
  }

  // A workbook without a project gets one, and becomes macro-enabled. Its .xlsb form already is, and so is an add-in,
  // which has to keep its own type to stay one.
  void AddProject(IReadOnlyList<ModuleToWrite> modules, IReadOnlyList<VbaReference> references, int codePage)
  {
    var workbook = _package.WorkbookPart;
    var documents = IsXml(workbook) ? GiveCodeNames(workbook, modules.Select(m => m.Name), codePage) : [];
    var part = workbook[..(workbook.LastIndexOf('/') + 1)] + "vbaProject.bin";
    _package.Set(part, ProjectWriter.Create([.. documents, .. modules], references, codePage));
    _package.AddRelationship(workbook, VbaProjectRelationship, "vbaProject.bin");
    _package.SetOverride(part, VbaProjectContentType);
    if (IsXml(workbook) && !IsAddin) _package.SetOverride(workbook, BlankWorkbook.MacroEnabledContentType);
  }

  // The sheets' parts, in order. A chart sheet isn't one of them, and Excel gives it a module itself.
  List<string> SheetParts(string workbook)
  {
    var main = OpcPackage.Main;
    var targets = _package.RelationshipsOf(workbook).Where(r => r.Type == BlankWorkbook.WorksheetRelationship).ToDictionary(r => r.Id, r => r.Target);
    return _package.Xml(workbook).Root!.Element(main + "sheets")?.Elements(main + "sheet")
      .Select(sheet => targets.GetValueOrDefault((string?)sheet.Attribute(OpcPackage.R + "id") ?? string.Empty))
      .OfType<string>().Select(target => OpcPackage.Resolve(workbook, target)).ToList() ?? [];
  }

  List<string> ReadCodeNames(string workbook)
  {
    var main = OpcPackage.Main;
    var book = (string?)_package.Xml(workbook).Root!.Element(main + "workbookPr")?.Attribute("codeName");
    var sheets = SheetParts(workbook).Select(part => (string?)_package.Xml(part).Root!.Element(main + "sheetPr")?.Attribute("codeName"));
    return [.. sheets.Prepend(book).OfType<string>()];
  }

  /// <summary>
  /// Makes sure the workbook and each sheet have a code name, which is how Excel ties them to their document modules, and
  /// returns those modules. A new name can't be one a sheet already has, or one of the modules coming in.
  /// </summary>
  List<ModuleToWrite> GiveCodeNames(string workbook, IEnumerable<string> incoming, int codePage)
  {
    var main = OpcPackage.Main;
    var encoding = CodePages.Get(codePage);
    var taken = ReadCodeNames(workbook).Concat(incoming).ToHashSet(StringComparer.OrdinalIgnoreCase);
    string NewName(string stem, bool numberFirst)
    {
      var name = numberFirst ? stem + "1" : stem;
      for (int n = numberFirst ? 2 : 1; !taken.Add(name); n++) name = stem + n;
      return name;
    }

    var document = _package.Xml(workbook);
    var properties = document.Root!.Element(main + "workbookPr");
    if (properties is null)
    {
      properties = new XElement(main + "workbookPr");

      // workbookPr comes after fileVersion and fileSharing, if they're there.
      var before = document.Root.Elements().LastOrDefault(e => e.Name == main + "fileVersion" || e.Name == main + "fileSharing");
      if (before is null) document.Root.AddFirst(properties);
      else before.AddAfterSelf(properties);
    }

    var bookName = (string?)properties.Attribute("codeName") ?? NewName("ThisWorkbook", numberFirst: false);
    properties.SetAttributeValue("codeName", bookName);
    _package.SetXml(workbook, document);

    var documents = new List<ModuleToWrite> { Document(bookName, WorkbookBase, encoding) };
    foreach (var part in SheetParts(workbook))
    {
      var sheet = _package.Xml(part);
      var sheetProperties = sheet.Root!.Element(main + "sheetPr");
      if (sheetProperties is null)
      {
        sheetProperties = new XElement(main + "sheetPr");
        sheet.Root.AddFirst(sheetProperties); // sheetPr comes first
      }

      var name = (string?)sheetProperties.Attribute("codeName");
      if (name is null)
      {
        name = NewName("Sheet", numberFirst: true);
        sheetProperties.SetAttributeValue("codeName", name);
        _package.SetXml(part, sheet);
      }

      documents.Add(Document(name, WorksheetBase, encoding));
    }

    return documents;
  }

  // A document module with no code, as Excel stores one.
  static ModuleToWrite Document(string name, string vbBase, Encoding encoding) => new(name, VbaModuleKind.Document, encoding.GetBytes(
    $"Attribute VB_Name = \"{name}\"\r\nAttribute VB_Base = {vbBase}\r\nAttribute VB_GlobalNameSpace = False\r\nAttribute VB_Creatable = False\r\n"
    + "Attribute VB_PredeclaredId = True\r\nAttribute VB_Exposed = True\r\nAttribute VB_TemplateDerived = False\r\nAttribute VB_Customizable = True\r\n"));
}
