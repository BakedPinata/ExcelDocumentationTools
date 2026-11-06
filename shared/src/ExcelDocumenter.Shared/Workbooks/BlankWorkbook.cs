using System.Text;
using System.Xml.Linq;

namespace ExcelDocumenter.Shared.Workbooks;

/// <summary>
/// A new workbook with one empty sheet, built here rather than from a template made in Excel: the parts Excel needs, and
/// nothing more. It has no sensitivity label, so at work Excel asks for one, which is what should happen: the person
/// writing the workbook is the one to choose.
/// </summary>
public static class BlankWorkbook
{
  /// <summary>The content type of a workbook that can hold a VBA project.</summary>
  public const string MacroEnabledContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

  /// <summary>The relationship from a workbook to a sheet.</summary>
  public const string WorksheetRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

  private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
  private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

  private const string Styles =
    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
    + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
    + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>"
    + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
    + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
    + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
    + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
    + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
    + "</styleSheet>";

  /// <summary>Builds the workbook.</summary>
  /// <param name="macroEnabled">Whether it can hold a VBA project, which the file's name then has to say.</param>
  public static OpcPackage Build(bool macroEnabled)
  {
    var package = new OpcPackage();
    package.SetXml(OpcPackage.ContentTypesPart, new XDocument(new XElement(
      OpcPackage.ContentTypes + "Types",
      new XElement(OpcPackage.ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
      new XElement(OpcPackage.ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")))));
    package.SetOverride("xl/workbook.xml", macroEnabled ? MacroEnabledContentType : ContentType);
    package.SetOverride("xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
    package.SetOverride("xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
    package.AddRelationship(string.Empty, Relationships + "officeDocument", "xl/workbook.xml");

    var sheet = package.AddRelationship("xl/workbook.xml", WorksheetRelationship, "worksheets/sheet1.xml");
    package.AddRelationship("xl/workbook.xml", Relationships + "styles", "styles.xml");

    var main = OpcPackage.Main;
    package.SetXml("xl/workbook.xml", new XDocument(new XElement(
      main + "workbook",
      new XAttribute(XNamespace.Xmlns + "r", OpcPackage.R.NamespaceName),
      new XElement(main + "bookViews", new XElement(main + "workbookView")),
      new XElement(main + "sheets", new XElement(main + "sheet", new XAttribute("name", "Sheet1"), new XAttribute("sheetId", "1"), new XAttribute(OpcPackage.R + "id", sheet))),
      new XElement(main + "calcPr", new XAttribute("calcId", "191029"), new XAttribute("fullCalcOnLoad", "1")))));

    package.SetXml("xl/worksheets/sheet1.xml", new XDocument(new XElement(
      main + "worksheet",
      new XElement(main + "sheetViews", new XElement(main + "sheetView", new XAttribute("workbookViewId", "0"))),
      new XElement(main + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")),
      new XElement(main + "sheetData"))));

    package.Set("xl/styles.xml", Encoding.UTF8.GetBytes(Styles));
    return package;
  }
}
