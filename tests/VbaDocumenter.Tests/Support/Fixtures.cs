using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Tests.Support;

/// <summary>Paths to the files under Fixtures/, which the test project copies next to the test assembly.</summary>
public static class Fixtures
{
    public static string SampleVbaFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleVba");
    public static string SampleVbaExtrasFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleVbaExtras");

    /// <summary>Parses one fixture file, e.g. Parse("SampleVba", "Order.cls"). The module name is the file name up to the first dot.</summary>
    public static ModuleModel Parse(string folder, string fileName)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, fileName));
        return ModuleParser.Parse(fileName.Split('.')[0], KindFromFileName(fileName), text);
    }

    static VbaModuleKind KindFromFileName(string fileName) =>
        fileName.EndsWith(".doc.cls") ? VbaModuleKind.Document
        : fileName.EndsWith(".cls") ? VbaModuleKind.Class
        : fileName.EndsWith(".frm") ? VbaModuleKind.Form
        : VbaModuleKind.Standard;

    public static string Workbook(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Workbooks", fileName);

    /// <summary>
    /// The SampleVba modules as they'd be stored in a workbook. The kind comes from the file name:
    /// ".doc.cls" is a document module, ".cls" a class, ".bas" a standard module.
    /// </summary>
    public static List<SyntheticModule> SampleVbaModules() =>
        Directory.GetFiles(SampleVbaFolder)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                return new SyntheticModule(fileName.Split('.')[0], KindFromFileName(fileName), File.ReadAllText(path));
            })
            .ToList();
}
