using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;

namespace VbaDocumenter.Core.Parsing;

public static class ProjectParser
{
    /// <summary>
    /// Parses every module. Each module is parsed from the exact text of its exported file, so member line
    /// numbers match the files in the output folder (and the source shown in the page).
    /// </summary>
    public static ProjectModel Parse(VbaProject project)
    {
        var encoding = CodePages.Get(project.CodePage);
        return new ProjectModel
        {
            Name = project.Name,
            CodePage = project.CodePage,
            Modules = project.Modules
                .Select(m => ModuleParser.Parse(m.Name, m.Kind, encoding.GetString(ProjectExporter.FileContent(m))))
                .ToList(),
        };
    }
}
