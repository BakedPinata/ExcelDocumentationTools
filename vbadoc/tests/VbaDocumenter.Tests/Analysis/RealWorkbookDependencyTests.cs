using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Analysis;

/// <summary>Runs the analysis over the real Excel workbooks: code that wasn't written with this tool in mind.</summary>
public class RealWorkbookDependencyTests
{
    static DependencyModel Analyze(string fileName) =>
        DependencyAnalyzer.Analyze(ProjectParser.Parse(WorkbookReader.Read(Fixtures.Workbook(fileName))));

    [Theory]
    [InlineData("SimpleMacro.xlsm")]
    [InlineData("SimpleMacro.xls")]
    [InlineData("SquareMacro.xls")]
    [InlineData("45431.xlsm")]
    [InlineData("57181.xlsm")]
    [InlineData("suffix-generator.xlsm")]
    public void Produces_a_consistent_model(string fileName)
    {
        var model = Analyze(fileName);
        var ids = model.Members.Select(m => m.Id).ToHashSet();

        Assert.Equal(ids.Count, model.Members.Count);
        Assert.All(model.MemberEdges, e => Assert.True(ids.Contains(e.From) && ids.Contains(e.To), $"{e} has an unknown end"));
        Assert.All(model.ModuleEdges, e => Assert.NotEqual(e.From, e.To));
        Assert.All(model.LateBound, l => Assert.Contains(l.FromId, ids));
    }

    [Fact]
    public void Worksheet_functions_are_not_flagged_as_unreferenced()
    {
        // SquareMacro's module holds UDFs used in cell formulas, which the code itself never calls.
        var model = Analyze("SquareMacro.xls");

        Assert.All(model.Members.Where(m => m.Module.Name == "UserDefinedFunctions" && m.Member != null),
            m => Assert.Equal((MemberRole.WorksheetFunction, false), (m.Role, m.IsUnreferenced)));
    }

    [Fact]
    public void Finds_cross_module_dependencies_in_a_larger_real_project()
    {
        // 57181's sheet modules call procedures in the CCSInfo sheet module and read its public variables.
        var model = Analyze("57181.xlsm");

        Assert.Contains(model.ModuleEdges, e => e.To == "CCSInfo" && e.Kind == EdgeKind.Uses);
    }

    [Fact]
    public void ThisWorkbook_finds_the_workbook_module_under_its_localised_name()
    {
        // German Excel names the workbook module "DieseArbeitsmappe", but code still says "ThisWorkbook.Sheets(...)".
        var model = Analyze("57181.xlsm");

        Assert.Contains(model.ModuleEdges, e => e.From == "Detailed_graph" && e.To == "DieseArbeitsmappe");
    }
}
