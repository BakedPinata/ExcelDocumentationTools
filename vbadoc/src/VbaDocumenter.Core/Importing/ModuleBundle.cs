using System.Text;
using CodeDocumenter.Shared.Importing;
using CodeDocumenter.Shared.Workbooks;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;

namespace VbaDocumenter.Core.Importing;

/// <summary>
/// A plan as a bundle: each module as the VBA editor imports it, and a README saying how, with the references to tick
/// and what the modules rely on that isn't there. It says nothing about when it was written, so the same choice gives
/// the same bundle.
/// </summary>
public static class ModuleBundle
{
  /// <summary>Each module's file name and bytes.</summary>
  /// <exception cref="ExtractionException">Two modules map to the same file name.</exception>
  public static Dictionary<string, byte[]> Files(VbaProject project, ImportPlan plan)
  {
    ArgumentNullException.ThrowIfNull(project);
    ArgumentNullException.ThrowIfNull(plan);
    var byName = project.Modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var module in plan.Items.Select(id => byName[id]))
    {
      var name = ProjectExporter.FileNameFor(module);
      if (files.Keys.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
        throw new ExtractionException($"Two modules map to the same file name '{name}'.");
      files[name] = ProjectExporter.FileContent(module);
    }

    return files;
  }

  /// <summary>The README, whose first line marks the folder as a bundle (see <see cref="BundleFolder"/>).</summary>
  /// <param name="workbook">The workbook's file name.</param>
  /// <param name="project">The project read.</param>
  /// <param name="plan">What's in the bundle.</param>
  /// <param name="references">The library references it needs.</param>
  public static string Readme(string workbook, VbaProject project, ImportPlan plan, IReadOnlyList<VbaReference> references)
  {
    ArgumentNullException.ThrowIfNull(project);
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(references);
    var byName = project.Modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    var text = new StringBuilder();
    text.Append(BundleFolder.Marker("vbadoc")).Append('\n');
    text.Append($"\n# Modules from {workbook}\n\n");
    text.Append("Chosen with vbadoc, with everything they use. To bring them into a workbook by hand:\n\n");
    text.Append("1. Open the workbook and the VBA editor (Alt+F11).\n");
    text.Append("2. Choose **File > Import File** for each file below, in any order.\n");
    text.Append(references.Count > 0
      ? "3. Tick the library references below in **Tools > References**.\n4. Choose **Debug > Compile VBAProject**.\n"
      : "3. Choose **Debug > Compile VBAProject**.\n");

    text.Append("\n| File | Module | Why it's here |\n|---|---|---|\n");
    foreach (var id in plan.Picked) text.Append($"| `{ProjectExporter.FileNameFor(byName[id])}` | {id} | Chosen |\n");
    foreach (var item in plan.Brought) text.Append($"| `{ProjectExporter.FileNameFor(byName[item.Id])}` | {item.Id} | Used by {string.Join(", ", item.UsedBy)} |\n");

    if (references.Count > 0)
    {
      text.Append("\n## Library references\n\nThe source project has these, and a module's text doesn't say which it uses:\n\n");
      foreach (var r in references) text.Append($"- {r.Description} (`{r.Name}`)\n");
    }

    if (plan.Needs.Count > 0)
    {
      text.Append("\n## To check in the target\n\nThese didn't come along:\n\n");
      foreach (var need in plan.Needs) text.Append($"- **{need.Need.What}:** {need.Need.Why}. Used by {string.Join(", ", need.UsedBy)}.\n");
    }

    if (plan.Offers.Count > 0)
    {
      text.Append("\n## Not included\n\nNothing here uses these, so they didn't come along, but you may want them:\n\n");
      foreach (var offer in plan.Offers) text.Append($"- {offer.Id}, which implements {offer.For}\n");
    }

    return text.ToString();
  }
}
