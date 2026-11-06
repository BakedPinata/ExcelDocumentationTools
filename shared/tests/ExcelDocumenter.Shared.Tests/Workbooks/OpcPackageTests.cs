using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ExcelDocumenter.Shared.Workbooks;

namespace ExcelDocumenter.Shared.Tests.Workbooks;

public sealed class OpcPackageTests
{
  private const string Signature = "http://schemas.microsoft.com/office/2006/relationships/vbaProjectSignature";
  private static readonly byte[] Label = Encoding.UTF8.GetBytes("<label  id=\"x\"  />\r\n");

  private static OpcPackage Sample()
  {
    var package = BlankWorkbook.Build(macroEnabled: false);
    package.Set("docMetadata/LabelInfo.xml", Label);
    return package;
  }

  [Fact]
  public void A_part_nobody_touched_comes_back_byte_for_byte()
  {
    var package = OpcPackage.Read(Sample().Write());
    package.Set("xl/extra.bin", [1, 2, 3]);

    var again = OpcPackage.Read(package.Write());

    Assert.Equal(Label, again.Get("docMetadata/LabelInfo.xml"));
    Assert.Equal([1, 2, 3], again.Get("xl/extra.bin"));
  }

  [Fact]
  public void The_part_listing_the_types_comes_first_wherever_it_was()
  {
    var package = new OpcPackage();
    package.Set("xl/a.xml", [1]);
    package.Set(OpcPackage.ContentTypesPart, [2]);

    using var zip = new ZipArchive(new MemoryStream(package.Write()), ZipArchiveMode.Read);

    Assert.Equal(OpcPackage.ContentTypesPart, zip.Entries[0].FullName);
  }

  [Fact]
  public void Changing_xml_keeps_a_cell_that_is_only_spaces()
  {
    var package = new OpcPackage();
    package.Set("xl/sheet.xml", Encoding.UTF8.GetBytes("<a><t> </t></a>"));

    package.SetXml("xl/sheet.xml", package.Xml("xl/sheet.xml"));

    Assert.Equal(" ", package.Xml("xl/sheet.xml").Root!.Element("t")!.Value);
  }

  [Fact]
  public void Xml_is_written_as_Excel_writes_it()
  {
    var package = new OpcPackage();
    package.SetXml("a.xml", new XDocument(new XElement("a")));

    var bytes = package.Get("a.xml");

    Assert.NotEqual(0xEF, bytes[0]); // no byte order mark
    Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void The_workbook_part_comes_from_the_packages_own_relationships()
  {
    var package = BlankWorkbook.Build(macroEnabled: true);

    Assert.Equal("xl/workbook.xml", package.WorkbookPart);
  }

  [Fact]
  public void A_package_that_doesnt_say_where_its_workbook_is_is_damaged()
  {
    var package = new OpcPackage();
    package.Set("xl/workbook.xml", [1]);

    var error = Assert.Throws<ExtractionException>(() => package.WorkbookPart);

    Assert.Contains("damaged", error.Message);
  }

  [Fact]
  public void What_isnt_a_zip_is_damaged()
  {
    var error = Assert.Throws<ExtractionException>(() => OpcPackage.Read([0x50, 0x4B, 0x03, 0x04, 1, 2, 3]));

    Assert.Contains("damaged", error.Message);
  }

  [Theory]
  [InlineData("", "_rels/.rels")]
  [InlineData("xl/workbook.xml", "xl/_rels/workbook.xml.rels")]
  [InlineData("xl/worksheets/sheet1.xml", "xl/worksheets/_rels/sheet1.xml.rels")]
  [InlineData("top.xml", "_rels/top.xml.rels")]
  public void A_parts_relationships_are_beside_it(string part, string expected)
  {
    Assert.Equal(expected, OpcPackage.RelsOf(part));
  }

  [Theory]
  [InlineData("", "xl/workbook.xml", "xl/workbook.xml")]
  [InlineData("xl/workbook.xml", "worksheets/sheet1.xml", "xl/worksheets/sheet1.xml")]
  [InlineData("xl/workbook.xml", "../customXml/item1.xml", "customXml/item1.xml")]
  [InlineData("xl/workbook.xml", "/xl/styles.xml", "xl/styles.xml")]
  public void A_target_is_relative_to_its_part_unless_it_starts_at_the_root(string from, string target, string expected)
  {
    Assert.Equal(expected, OpcPackage.Resolve(from, target));
  }

  [Fact]
  public void A_new_relationship_takes_an_id_nothing_has()
  {
    var package = BlankWorkbook.Build(macroEnabled: true);
    var taken = package.RelationshipsOf("xl/workbook.xml").Select(r => r.Id).ToList();

    var id = package.AddRelationship("xl/workbook.xml", "type", "x.bin");

    Assert.DoesNotContain(id, taken);
    Assert.Contains(package.RelationshipsOf("xl/workbook.xml"), r => r is { Type: "type", Target: "x.bin" } && r.Id == id);
  }

  [Fact]
  public void Removing_relationships_leaves_the_others_and_an_emptied_part_goes()
  {
    var package = new OpcPackage();
    package.AddRelationship("xl/vbaProject.bin", Signature, "vbaProjectSignature.bin");
    package.AddRelationship("xl/vbaProject.bin", "other", "other.bin");

    package.RemoveRelationships("xl/vbaProject.bin", type => type == Signature);
    Assert.Equal("other", Assert.Single(package.RelationshipsOf("xl/vbaProject.bin")).Type);

    package.RemoveRelationships("xl/vbaProject.bin", type => type == "other");
    Assert.False(package.Has("xl/_rels/vbaProject.bin.rels"));
  }

  [Fact]
  public void A_parts_own_type_can_be_changed_and_taken_out()
  {
    var package = BlankWorkbook.Build(macroEnabled: false);
    string? TypeOf(string part) => (string?)package.Xml(OpcPackage.ContentTypesPart).Root!.Elements(OpcPackage.ContentTypes + "Override")
      .SingleOrDefault(o => (string?)o.Attribute("PartName") == "/" + part)?.Attribute("ContentType");

    package.SetOverride("xl/workbook.xml", BlankWorkbook.MacroEnabledContentType);
    Assert.Equal(BlankWorkbook.MacroEnabledContentType, TypeOf("xl/workbook.xml"));

    package.RemoveOverride("xl/workbook.xml");
    Assert.Null(TypeOf("xl/workbook.xml"));
  }

  [Theory]
  [InlineData(true, "application/vnd.ms-excel.sheet.macroEnabled.main+xml")]
  [InlineData(false, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")]
  public void A_blank_workbook_has_a_sheet_and_says_whether_it_can_hold_macros(bool macroEnabled, string contentType)
  {
    var package = OpcPackage.Read(BlankWorkbook.Build(macroEnabled).Write());

    Assert.True(package.Has("xl/worksheets/sheet1.xml"));
    Assert.True(package.Has("xl/styles.xml"));
    Assert.Contains(contentType, Encoding.UTF8.GetString(package.Get(OpcPackage.ContentTypesPart)));
    Assert.Equal("Sheet1", (string?)package.Xml("xl/workbook.xml").Descendants(OpcPackage.Main + "sheet").Single().Attribute("name"));
  }
}
