using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Tests.Support;

/// <summary>Paths to the files under Fixtures/, which the test project copies next to the test assembly.</summary>
public static class Fixtures
{
    public static string SampleVbaFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleVba");

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
                var kind = fileName.EndsWith(".doc.cls") ? VbaModuleKind.Document
                    : fileName.EndsWith(".cls") ? VbaModuleKind.Class
                    : VbaModuleKind.Standard;
                return new SyntheticModule(fileName.Split('.')[0], kind, File.ReadAllText(path));
            })
            .ToList();
}
