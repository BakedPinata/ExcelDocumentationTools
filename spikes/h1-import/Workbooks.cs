using System.Text;
using System.Xml.Linq;

namespace ImportSpike;

/// <summary>What happens to a workbook's package around its VBA project: where it goes, and what else has to agree.</summary>
static class Workbooks
{
  const string VbaProjectType = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
  const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
  const string MacroEnabledWorkbook = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
  const string WorksheetType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

  /// <summary>
  /// Adds modules to the workbook's VBA project, replacing those named; a workbook without one gets a new project,
  /// with a document module for the workbook and each sheet, named after their code names (given to them if they
  /// have none), and becomes macro-enabled.
  /// </summary>
  public static void AddModules(OpcPackage package, IReadOnlyList<ModuleToWrite> modules, IReadOnlyCollection<string>? replace = null)
  {
    var workbook = package.WorkbookPart;
    var existing = package.RelationshipsOf(workbook).FirstOrDefault(r => r.Type == VbaProjectType).Target;
    if (existing is not null)
    {
      var part = Resolve(workbook, existing);
      package.Set(part, VbaProjectWriter.Edit(package.Get(part), modules, replace ?? []));
      return;
    }

    var documents = workbook.EndsWith(".xml", StringComparison.Ordinal) ? GiveCodeNames(package, workbook) : [];
    package.Set("xl/vbaProject.bin", VbaProjectWriter.Create([.. documents, .. modules]));
    package.AddRelationship(workbook, VbaProjectType, "vbaProject.bin");
    package.SetOverride("xl/vbaProject.bin", VbaProjectContentType);
    // An .xlsb's workbook part is already macro-enabled; an .xlsx's has to say so (and the file be named .xlsm).
    if (workbook.EndsWith(".xml", StringComparison.Ordinal)) package.SetOverride(workbook, MacroEnabledWorkbook);
  }

  /// <summary>
  /// Makes sure the workbook and each sheet have a code name, which is how Excel ties them to their document modules,
  /// and returns those modules. Only for workbooks stored as XML; an .xlsb's code names are in its binary parts.
  /// </summary>
  static List<ModuleToWrite> GiveCodeNames(OpcPackage package, string workbook)
  {
    var main = OpcPackage.Main;
    var doc = package.Xml(workbook);
    var documents = new List<ModuleToWrite>();
    var pr = doc.Root!.Element(main + "workbookPr");
    if (pr is null)
    {
      pr = new XElement(main + "workbookPr");
      // workbookPr comes after fileVersion and fileSharing, if they're there.
      var before = doc.Root.Elements().LastOrDefault(e => e.Name == main + "fileVersion" || e.Name == main + "fileSharing");
      if (before is null) doc.Root.AddFirst(pr); else before.AddAfterSelf(pr);
    }
    var bookName = (string?)pr.Attribute("codeName") ?? "ThisWorkbook";
    pr.SetAttributeValue("codeName", bookName);
    package.SetXml(workbook, doc);
    documents.Add(ModuleFiles.Document(bookName, isWorkbook: true));

    var rels = package.RelationshipsOf(workbook).Where(r => r.Type == WorksheetType).ToList();
    var relIds = package.Xml(OpcPackage.RelsOf(workbook)).Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
    int n = 1;
    foreach (var sheet in doc.Root.Element(main + "sheets")!.Elements(main + "sheet"))
    {
      var target = relIds.GetValueOrDefault((string)sheet.Attribute(OpcPackage.R + "id")!);
      if (target is null || !rels.Any(r => r.Target == target)) continue; // a chart sheet, say
      var part = Resolve(workbook, target);
      var sheetDoc = package.Xml(part);
      var sheetPr = sheetDoc.Root!.Element(main + "sheetPr");
      if (sheetPr is null)
      {
        sheetPr = new XElement(main + "sheetPr");
        sheetDoc.Root.AddFirst(sheetPr); // sheetPr comes first
      }
      var name = (string?)sheetPr.Attribute("codeName") ?? "Sheet" + n;
      sheetPr.SetAttributeValue("codeName", name);
      package.SetXml(part, sheetDoc);
      documents.Add(ModuleFiles.Document(name, isWorkbook: false));
      n++;
    }
    return documents;
  }

  public static string Resolve(string fromPart, string target)
  {
    if (target.StartsWith('/')) return target.TrimStart('/');
    var parts = Path.GetDirectoryName(fromPart)!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
    foreach (var piece in target.Split('/'))
    {
      if (piece == "..") parts.RemoveAt(parts.Count - 1); else parts.Add(piece);
    }
    return string.Join('/', parts);
  }

  /// <summary>
  /// A new workbook with one sheet, built here rather than from a template made in Excel: the parts Excel needs, and
  /// nothing more. <paramref name="cells"/> are (cell, formula) pairs for the sheet.
  /// </summary>
  public static OpcPackage New(bool macroEnabled, IEnumerable<(string Cell, string? Text, string? Formula)> cells)
  {
    var package = OpcPackage.Read(EmptyZip());
    var main = OpcPackage.Main;
    package.SetXml("[Content_Types].xml", new XDocument(new XElement(OpcPackage.ContentTypes + "Types",
      new XElement(OpcPackage.ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
      new XElement(OpcPackage.ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")))));
    package.SetOverride("xl/workbook.xml", macroEnabled ? MacroEnabledWorkbook : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
    package.SetOverride("xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
    package.SetOverride("xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
    package.AddRelationship("", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");

    var sheetRel = package.AddRelationship("xl/workbook.xml", WorksheetType, "worksheets/sheet1.xml");
    package.AddRelationship("xl/workbook.xml", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
    package.SetXml("xl/workbook.xml", new XDocument(new XElement(main + "workbook",
      new XAttribute(XNamespace.Xmlns + "r", OpcPackage.R.NamespaceName),
      new XElement(main + "bookViews", new XElement(main + "workbookView")),
      new XElement(main + "sheets", new XElement(main + "sheet", new XAttribute("name", "Sheet1"), new XAttribute("sheetId", "1"), new XAttribute(OpcPackage.R + "id", sheetRel))),
      new XElement(main + "calcPr", new XAttribute("calcId", "191029"), new XAttribute("fullCalcOnLoad", "1")))));

    var rows = cells.GroupBy(c => int.Parse(new string(c.Cell.SkipWhile(char.IsLetter).ToArray()))).OrderBy(g => g.Key)
      .Select(g => new XElement(main + "row", new XAttribute("r", g.Key), g.Select(c => c.Formula is not null
        ? new XElement(main + "c", new XAttribute("r", c.Cell), new XElement(main + "f", c.Formula))
        : new XElement(main + "c", new XAttribute("r", c.Cell), new XAttribute("t", "inlineStr"), new XElement(main + "is", new XElement(main + "t", c.Text))))));
    package.SetXml("xl/worksheets/sheet1.xml", new XDocument(new XElement(main + "worksheet",
      new XElement(main + "sheetViews", new XElement(main + "sheetView", new XAttribute("workbookViewId", "0"))),
      new XElement(main + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")),
      new XElement(main + "cols", new XElement(main + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "60"), new XAttribute("customWidth", "1"))),
      new XElement(main + "sheetData", rows))));

    package.Set("xl/styles.xml", Encoding.UTF8.GetBytes(
      "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
      "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
      "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>" +
      "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
      "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
      "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
      "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
      "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
      "</styleSheet>"));
    return package;
  }

  static byte[] EmptyZip()
  {
    var buffer = new MemoryStream();
    new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true).Dispose();
    return buffer.ToArray();
  }
}
