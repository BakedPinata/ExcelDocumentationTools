using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDocumenter.Shared.Workbooks;
using OpenMcdf;
using VbaDocumenter.Core.Extraction;
using PqReader = PowerQueryDocumenter.Core.Extraction.WorkbookReader;
using VbaReader = VbaDocumenter.Core.Extraction.WorkbookReader;

namespace ImportSpike;

/// <summary>
/// The spike on a workbook of the user's own, chiefly to find out what sensitivity labels mean for importing. A label
/// comes in two kinds, and which one a workbook has decides everything. A label without encryption is only metadata
/// (docMetadata/LabelInfo.xml, or MSIP_Label_ properties in docProps/custom.xml), so the file stays an ordinary zip the
/// tools can change, and the label should come through as long as those parts do. A label with encryption wraps the
/// whole file in a compound file with an EncryptedPackage stream, which nothing but Excel can open. What's printed here
/// may be pasted into a chat, so it has counts and yes or no, and never label names, ids, or what's in the workbook.
/// The one exception is the line saying where the copy was written, which names the file.
/// </summary>
static class SpecificWorkbook
{
  static readonly string[] Handled = [".xlsx", ".xlsm", ".xlsb"];

  /// <summary>Says what a workbook is, as far as importing goes. Returns whether the tools could change it.</summary>
  public static bool Inspect(string path)
  {
    var bytes = WorkbookFile.ReadAllBytes(path);
    Console.WriteLine($"Workbook: {Path.GetExtension(path)} file, {bytes.Length:N0} bytes");
    if (WorkbookFile.ContainerOf(bytes) != WorkbookContainer.Zip)
    {
      Console.WriteLine("What it is: " + NotAZip(bytes));
      return false;
    }

    Console.WriteLine("What it is: an ordinary zip package, so the tools can read it and write a changed copy.");
    var package = OpcPackage.Read(bytes);
    Console.WriteLine("Sensitivity label: " + DescribeLabel(package));

    var signed = IsSigned(package);
    var vba = Workbooks.VbaProjectPart(package);
    Console.WriteLine("VBA project: " + (vba is null ? "none" : $"{VbaReader.Read(bytes).Modules.Count} modules, {(IsLocked(package.Get(vba)) ? "locked with a password" : "not locked")}, {(signed ? "signed" : "not signed")}"));
    var workbook = package.WorkbookPart;
    var connections = package.RelationshipsOf(workbook).FirstOrDefault(r => r.Type.EndsWith("/connections", StringComparison.Ordinal)).Target;
    Console.WriteLine($"Queries: {ExistingQueries(bytes).Count}, connections {(connections is null ? "none" : "in " + connections)}");
    return true;
  }

  /// <summary>
  /// Writes a copy of the workbook with the spike's modules and queries in it, beside it unless told otherwise, and
  /// never over anything. Then reads the copy back, and says which parts changed: the label's must not have.
  /// </summary>
  public static string? Add(string path, IReadOnlyList<ModuleToWrite> modules, IReadOnlyList<QueryToWrite> queries, string? outputFolder)
  {
    var extension = Path.GetExtension(path).ToLowerInvariant();
    var bytes = WorkbookFile.ReadAllBytes(path);
    if (WorkbookFile.ContainerOf(bytes) != WorkbookContainer.Zip)
    {
      Console.WriteLine("Can't change this workbook: it's " + NotAZip(bytes));
      return null;
    }
    if (!Handled.Contains(extension))
    {
      Console.WriteLine($"The spike handles {string.Join(", ", Handled)}, not {extension}.");
      return null;
    }

    var package = OpcPackage.Read(bytes);
    if (modules.Count > 0 && IsSigned(package))
    {
      Console.WriteLine("The VBA project is signed, so changing it would break the signature. The tool would refuse unless told to go ahead; the spike stops here.");
      return null;
    }
    var clashes = ExistingQueries(bytes).Intersect(queries.Select(q => q.Name), StringComparer.OrdinalIgnoreCase)
      .Concat(Workbooks.VbaProjectPart(package) is null ? [] : VbaReader.Read(bytes).Modules.Select(m => m.Name).Intersect(modules.Select(m => m.Name), StringComparer.OrdinalIgnoreCase))
      .ToList();
    if (clashes.Count > 0)
    {
      Console.WriteLine($"It already has {string.Join(", ", clashes)}. Run the spike on the original workbook, not on a copy it wrote.");
      return null;
    }

    if (modules.Count > 0) Workbooks.AddModules(package, modules);
    // An .xlsb's connections are in a binary part, so its queries go in without them, as in P5.
    if (queries.Count > 0) Mashup.AddQueries(package, queries, withConnections: package.WorkbookPart.EndsWith(".xml", StringComparison.Ordinal));

    var outputExtension = modules.Count > 0 && extension == ".xlsx" ? ".xlsm" : extension;
    var written = FreeName(outputFolder ?? Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + "-spike", outputExtension);
    var output = package.Write();
    using (var file = new FileStream(written, FileMode.CreateNew, FileAccess.Write)) file.Write(output);
    Console.WriteLine($"Wrote {written}");
    Console.WriteLine($"The original was only read, and is unchanged.");

    Check(output, modules, queries);
    Compare(OpcPackage.Read(bytes), OpcPackage.Read(output));
    return written;
  }

  static void Check(byte[] output, IReadOnlyList<ModuleToWrite> modules, IReadOnlyList<QueryToWrite> queries)
  {
    if (modules.Count > 0)
    {
      var read = VbaReader.Read(output).Modules;
      foreach (var m in modules)
      {
        if (read.Single(r => r.Name == m.Name).Source != m.Source) throw new InvalidOperationException($"{m.Name} reads back differently.");
      }
      Console.WriteLine($"Read back: {read.Count} modules, the {modules.Count} added word for word.");
    }
    if (queries.Count > 0)
    {
      var read = PqReader.Read(output);
      foreach (var q in queries)
      {
        if (read.Single(r => r.Name == q.Name).Expression.ReplaceLineEndings("\r\n") != q.Expression) throw new InvalidOperationException($"{q.Name} reads back differently.");
      }
      Console.WriteLine($"Read back: {read.Count} queries, the {queries.Count} added word for word.");
    }
  }

  // Which parts differ, and above all whether the label's do.
  static void Compare(OpcPackage before, OpcPackage after)
  {
    var names = before.Names.ToHashSet();
    var changed = names.Where(n => after.Has(n) && !Hash(before.Get(n)).SequenceEqual(Hash(after.Get(n)))).Order().ToList();
    var added = after.Names.Where(n => !names.Contains(n)).Order().ToList();
    var removed = names.Where(n => !after.Has(n)).Order().ToList();
    Console.WriteLine($"Parts: {names.Count - changed.Count - removed.Count} unchanged, {changed.Count} changed, {added.Count} added, {removed.Count} removed.");
    Console.WriteLine("  changed: " + (changed.Count == 0 ? "none" : string.Join(", ", changed)));
    Console.WriteLine("  added: " + (added.Count == 0 ? "none" : string.Join(", ", added)));

    var labelParts = names.Where(IsLabelPart).ToList();
    if (labelParts.Count == 0) { Console.WriteLine("Label: the workbook has no label metadata, so there's none to keep."); return; }
    bool kept = labelParts.All(n => after.Has(n) && Hash(before.Get(n)).SequenceEqual(Hash(after.Get(n))));
    bool stillLinked = !labelParts.Contains("docMetadata/LabelInfo.xml") || after.RelationshipsOf("").Any(r => r.Target.TrimStart('/') == "docMetadata/LabelInfo.xml");
    Console.WriteLine(kept && stillLinked
      ? $"Label: kept. {string.Join(", ", labelParts)} {(labelParts.Count == 1 ? "is" : "are")} byte for byte as they were."
      : "Label: NOT kept as it was. This would be a bug in the spike.");
  }

  static bool IsLabelPart(string name) =>
    name.StartsWith("docMetadata/", StringComparison.OrdinalIgnoreCase) || name.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase);

  static byte[] Hash(byte[] data) => SHA256.HashData(data);

  static string FreeName(string folder, string name, string extension)
  {
    var path = Path.Combine(folder, name + extension);
    for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name}-{n}{extension}");
    return path;
  }

  // What a file that isn't a zip package is, for someone deciding what to do about it.
  static string NotAZip(byte[] bytes)
  {
    if (WorkbookFile.ContainerOf(bytes) != WorkbookContainer.CompoundFile) return "not an Excel workbook.";
    using var root = RootStorage.Open(new MemoryStream(bytes));
    if (!WorkbookFile.IsEncrypted(root)) return "an older .xls workbook, which the spike doesn't handle.";
    return root.ContainsEntry("EncryptionInfo")
      ? "encrypted with a password. Without it nothing inside can be read or changed, so the tools can't use this file."
      : "encrypted by rights management: a sensitivity label that encrypts, or IRM. Only Excel can open it, so the tools can't read or change this file.";
  }

  static string DescribeLabel(OpcPackage package)
  {
    var found = new List<string>();
    var info = package.Names.FirstOrDefault(n => n.Equals("docMetadata/LabelInfo.xml", StringComparison.OrdinalIgnoreCase));
    if (info is not null)
    {
      var labels = package.Xml(info).Descendants().Where(e => e.Name.LocalName == "label").ToList();
      var state = labels.Select(l => $"enabled {(string?)l.Attribute("enabled")}, removed {(string?)l.Attribute("removed")}, method {(string?)l.Attribute("method")}");
      found.Add($"{labels.Count} in {info} ({string.Join("; ", state)})");
    }
    if (package.Has("docProps/custom.xml"))
    {
      try
      {
        var properties = package.Xml("docProps/custom.xml").Descendants().Select(e => (string?)e.Attribute("name")).OfType<string>()
          .Where(n => n.StartsWith("MSIP_Label_", StringComparison.Ordinal)).ToList();
        if (properties.Count > 0) found.Add($"{properties.Count} MSIP_Label_ properties in docProps/custom.xml");
      }
      catch (XmlException) { found.Add("docProps/custom.xml couldn't be read"); }
    }
    return found.Count == 0 ? "no label metadata found" : string.Join("; ", found) + ". It isn't encrypted, so it's only metadata. (Names and ids aren't shown.)";
  }

  static bool IsSigned(OpcPackage package) => package.Names.Any(n => n.Contains("vbaProjectSignature", StringComparison.OrdinalIgnoreCase));

  // A locked project's CMG says so: it's all zeros when nothing protects it.
  static bool IsLocked(byte[] vbaProjectBin)
  {
    using var root = RootStorage.Open(new MemoryStream(vbaProjectBin));
    var line = Encoding.Latin1.GetString(Dump.Read(root, "PROJECT")).Split("\r\n").FirstOrDefault(l => l.StartsWith("CMG=", StringComparison.Ordinal));
    return line is not null && Ovba.Decrypt(line.Split('"')[1]).Data.Any(b => b != 0);
  }

  static List<string> ExistingQueries(byte[] bytes)
  {
    try { return PqReader.Read(bytes).Select(q => q.Name).ToList(); }
    catch (ExtractionException) { return []; } // none, or none that can be read
  }

  /// <summary>
  /// A stand-in for a labelled workbook, as far as the spike can make one without a real label: Queries.xlsx with
  /// label metadata in both places Office keeps it, made-up ids, and a sheet with a code name that a new one could
  /// clash with. It shows what <see cref="Add"/> does with the parts. Only Excel at work can say what a real label does.
  /// </summary>
  public static void WriteLabelledSample(string fixture, string path)
  {
    var package = OpcPackage.Read(File.ReadAllBytes(fixture));
    package.Set("docMetadata/LabelInfo.xml", Encoding.UTF8.GetBytes(
      "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\r\n<clbl:labelList xmlns:clbl=\"http://schemas.microsoft.com/office/2020/mipLabelMetadata\">" +
      "<clbl:label id=\"{00000000-0000-0000-0000-000000000001}\" enabled=\"1\" method=\"Privileged\" siteId=\"{00000000-0000-0000-0000-000000000002}\" removed=\"0\" /></clbl:labelList>"));
    package.SetOverride("docMetadata/LabelInfo.xml", "application/vnd.ms-office.classificationlabels+xml");
    package.AddRelationship("", "http://schemas.microsoft.com/office/2020/02/relationships/classificationlabels", "docMetadata/LabelInfo.xml");

    package.Set("docProps/custom.xml", Encoding.UTF8.GetBytes(
      "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" " +
      "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" " +
      "name=\"MSIP_Label_00000000-0000-0000-0000-000000000001_Enabled\"><vt:lpwstr>true</vt:lpwstr></property></Properties>"));
    package.SetOverride("docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml");
    package.AddRelationship("", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", "docProps/custom.xml");

    var sheet = package.Xml("xl/worksheets/sheet2.xml");
    sheet.Root!.AddFirst(new XElement(OpcPackage.Main + "sheetPr", new XAttribute("codeName", "Sheet1")));
    package.SetXml("xl/worksheets/sheet2.xml", sheet);
    File.WriteAllBytes(path, package.Write());
  }
}
