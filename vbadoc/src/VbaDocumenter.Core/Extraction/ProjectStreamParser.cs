namespace VbaDocumenter.Core.Extraction;

/// <summary>
/// Reads the module list from the text "PROJECT" stream (MS-OVBA section 2.3.1).
/// </summary>
/// <remarks>
/// The dir stream only says whether a module is a standard module or not. The PROJECT stream tells
/// the non-standard ones apart, with lines such as:
/// <code>
/// Module=Main
/// Class=Order
/// BaseClass=UserForm1
/// Document=Sheet1/&amp;H00000000
/// </code>
/// </remarks>
public static class ProjectStreamParser
{
  public static Dictionary<string, VbaModuleKind> ParseModuleKinds(string projectText)
  {
    var kinds = new Dictionary<string, VbaModuleKind>(StringComparer.OrdinalIgnoreCase);
    foreach (var rawLine in projectText.Split('\n'))
    {
      var line = rawLine.Trim();
      if (line.StartsWith('[')) break; // [Host Extender Info] and [Workspace] come after the module list

      int equals = line.IndexOf('=');
      if (equals < 0) continue;
      var key = line[..equals];
      var name = line[(equals + 1)..].Split('/')[0].Trim('"');

      switch (key)
      {
        case "Module": kinds[name] = VbaModuleKind.Standard; break;
        case "Class": kinds[name] = VbaModuleKind.Class; break;
        case "BaseClass": kinds[name] = VbaModuleKind.Form; break;
        case "Document": kinds[name] = VbaModuleKind.Document; break;
      }
    }
    return kinds;
  }
}
