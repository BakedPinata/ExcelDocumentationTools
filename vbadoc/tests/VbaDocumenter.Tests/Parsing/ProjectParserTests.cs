using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>Parses the real Excel workbooks: code that wasn't written with this parser in mind.</summary>
public class ProjectParserTests
{
  [Theory]
  [InlineData("SimpleMacro.xlsm")]
  [InlineData("SimpleMacro.xls")]
  [InlineData("SquareMacro.xls")]
  [InlineData("45431.xlsm")]
  [InlineData("57181.xlsm")]
  [InlineData("suffix-generator.xlsm")]
  public void Parses_real_workbooks_without_warnings(string fileName)
  {
    var project = ProjectParser.Parse(WorkbookReader.Read(Fixtures.Workbook(fileName)));

    var warnings = project.Modules.SelectMany(m => m.Warnings.Select(w => $"{m.Name}: {w}")).ToList();
    Assert.True(warnings.Count == 0, string.Join("\n", warnings));
    Assert.Contains(project.Modules, m => m.Members.Any(p => p.IsProcedure));
  }

  [Fact]
  public void Skips_attribute_lines_inside_procedures()
  {
    // SimpleMacro's TestMacro has "Attribute TestMacro.VB_Description = ..." lines after the Sub line.
    var module1 = ProjectParser.Parse(WorkbookReader.Read(Fixtures.Workbook("SimpleMacro.xlsm"))).Modules.Single(m => m.Name == "Module1");
    var macro = module1.Members.Single();

    Assert.Equal(("TestMacro", MemberKind.Sub), (macro.Name, macro.Kind));
    Assert.DoesNotContain(macro.Body, s => s.Text.StartsWith("Attribute"));
    Assert.Contains(macro.Body, s => s.Text == "Range(\"A2\").Select");
  }

  [Fact]
  public void Keeps_modules_in_project_order()
  {
    var reader = WorkbookReader.Read(Fixtures.Workbook("57181.xlsm"));

    Assert.Equal(reader.Modules.Select(m => m.Name), ProjectParser.Parse(reader).Modules.Select(m => m.Name));
  }
}
