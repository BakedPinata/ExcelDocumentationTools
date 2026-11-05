namespace ImportSpike;

enum ModuleKind { Standard, Class, Document }

/// <summary>A module to write into a VBA project, with its source as the project stores it (CRLF line ends).</summary>
sealed record ModuleToWrite(string Name, ModuleKind Kind, string Source);

/// <summary>
/// Turns exported module files (.bas, .cls, as the VBA editor and vbadoc write them) into the source a VBA project
/// stores. An export of a class starts with a VERSION 1.0 CLASS ... END block and leaves out some of the class's
/// attributes; the stored source has no such block, and has them all (as SimpleMacro.xlsm's modules show).
/// </summary>
static class ModuleFiles
{
  static readonly string[] ClassAttributes =
    ["VB_Name", "VB_Base", "VB_GlobalNameSpace", "VB_Creatable", "VB_PredeclaredId", "VB_Exposed", "VB_TemplateDerived", "VB_Customizable"];

  const string ClassBase = "\"0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}\"";
  const string WorkbookBase = "\"0{00020819-0000-0000-C000-000000000046}\"";
  const string WorksheetBase = "\"0{00020820-0000-0000-C000-000000000046}\"";

  public static ModuleToWrite Read(string path)
  {
    // The spike's inputs are UTF-8. A bundle exported by vbadoc is in the source project's code page.
    var lines = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').ToList();
    bool isClass = Path.GetExtension(path).Equals(".cls", StringComparison.OrdinalIgnoreCase);
    if (lines.Count > 0 && lines[0].StartsWith("VERSION ", StringComparison.Ordinal))
    {
      lines.RemoveRange(0, lines.FindIndex(l => l.Trim() == "END") + 1);
    }

    var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    int body = 0;
    while (body < lines.Count && lines[body].StartsWith("Attribute VB_", StringComparison.Ordinal))
    {
      var parts = lines[body]["Attribute ".Length..].Split('=', 2);
      attributes[parts[0].Trim()] = parts[1].Trim();
      body++;
    }
    var name = attributes.TryGetValue("VB_Name", out var quoted) ? quoted.Trim('"')
      : throw new InvalidDataException($"{path} has no Attribute VB_Name line.");

    IEnumerable<string> header = isClass
      ? ClassHeader(name, attributes, ClassBase, predeclared: "False", exposed: "False", customizable: "False")
      : [$"Attribute VB_Name = \"{name}\""];
    return new ModuleToWrite(name, isClass ? ModuleKind.Class : ModuleKind.Standard, Join(header.Concat(lines.Skip(body))));
  }

  /// <summary>A document module with no code, for ThisWorkbook or a sheet, as Excel writes one.</summary>
  public static ModuleToWrite Document(string name, bool isWorkbook) =>
    new(name, ModuleKind.Document, Join(ClassHeader(name, new Dictionary<string, string>(), isWorkbook ? WorkbookBase : WorksheetBase,
      predeclared: "True", exposed: "True", customizable: "True")));

  static IEnumerable<string> ClassHeader(string name, Dictionary<string, string> given, string vbBase, string predeclared, string exposed, string customizable)
  {
    var defaults = new Dictionary<string, string>
    {
      ["VB_Name"] = $"\"{name}\"", ["VB_Base"] = vbBase, ["VB_GlobalNameSpace"] = "False", ["VB_Creatable"] = "False",
      ["VB_PredeclaredId"] = predeclared, ["VB_Exposed"] = exposed, ["VB_TemplateDerived"] = "False", ["VB_Customizable"] = customizable,
    };
    return ClassAttributes.Select(a => $"Attribute {a} = {(given.TryGetValue(a, out var v) ? v : defaults[a])}");
  }

  static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines) + "\r\n";
}
