using System.Text;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Output;

/// <summary>What an export did, so the console app can report it.</summary>
public sealed record ExportResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Deleted,
    bool CreatedGitAttributes);

/// <summary>
/// Writes each module to a source file that the VBA editor can import, and keeps the output
/// folder in step with the workbook.
/// </summary>
public static class ProjectExporter
{
    /// <summary>
    /// The header the VBA editor writes at the top of an exported class. It isn't stored in the
    /// workbook, but without it the editor imports a .cls file as a standard module.
    /// </summary>
    public const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    /// <summary>File types the tool owns. Any other file in the output folder is never touched.</summary>
    static readonly string[] ModuleExtensions = [".bas", ".cls", ".frm"];

    const string GitAttributesContent =
        "# Created by vbadoc. Keep exported VBA exactly as the VBA editor expects it:\n" +
        "# CRLF line endings and the project's code page, with no conversion by git.\n" +
        "# (\"-text\" turns conversion off; \"diff\" keeps line-by-line diffs.)\n" +
        "*.bas -text diff\n" +
        "*.cls -text diff\n" +
        "*.frm -text diff\n" +
        "*.frx binary\n" +
        "index.html -text diff\n";

    public static string FileNameFor(VbaModule module)
    {
        string extension = module.Kind switch
        {
            VbaModuleKind.Standard => ".bas",
            VbaModuleKind.Form => ".frm",
            _ => ".cls", // Class and Document
        };
        return SafeFileName(module.Name) + extension;
    }

    /// <summary>The exact bytes of the exported file: the module source with CRLF line endings, plus the class header where needed.</summary>
    public static byte[] FileContent(VbaModule module)
    {
        var source = NormaliseLineEndings(module.SourceBytes);
        if (module.Kind is not (VbaModuleKind.Class or VbaModuleKind.Document)) return source;

        // The header is plain ASCII, which is the same in every code page VBA uses.
        return [.. Encoding.ASCII.GetBytes(ClassHeader), .. source];
    }

    public static ExportResult Export(VbaProject project, string outputFolder)
    {
        // Work out every file first, so a problem (e.g. a name clash) stops us before anything changes on disk.
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var module in project.Modules)
        {
            var name = FileNameFor(module);
            if (files.Keys.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ExtractionException($"Two modules map to the same file name '{name}'.");
            files[name] = FileContent(module);
        }

        Directory.CreateDirectory(outputFolder);

        // Delete old module files first. Comparing names exactly (not ignoring case) means a module
        // renamed only by case, e.g. utils -> Utils, is deleted and rewritten, so the new spelling
        // shows up even on Windows, where the file system ignores case.
        var deleted = new List<string>();
        foreach (var path in Directory.GetFiles(outputFolder))
        {
            var fileName = Path.GetFileName(path);
            bool isModuleFile = ModuleExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
            if (isModuleFile && !files.ContainsKey(fileName))
            {
                File.Delete(path);
                deleted.Add(fileName);
            }
        }

        // Only rewrite files whose content changed, so unchanged files keep their timestamps.
        var written = new List<string>();
        var unchanged = new List<string>();
        foreach (var (fileName, content) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(outputFolder, fileName);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                unchanged.Add(fileName);
                continue;
            }
            File.WriteAllBytes(path, content);
            written.Add(fileName);
        }

        var gitAttributesPath = Path.Combine(outputFolder, ".gitattributes");
        bool createdGitAttributes = !File.Exists(gitAttributesPath);
        if (createdGitAttributes) File.WriteAllText(gitAttributesPath, GitAttributesContent, new UTF8Encoding(false));

        deleted.Sort(StringComparer.OrdinalIgnoreCase);
        return new ExportResult(written, unchanged, deleted, createdGitAttributes);
    }

    /// <summary>Converts lone CR or LF bytes to CRLF. VBA source is normally CRLF already, so this is usually a no-op.</summary>
    static byte[] NormaliseLineEndings(byte[] source)
    {
        var result = new List<byte>(source.Length + 16);
        for (int i = 0; i < source.Length; i++)
        {
            byte b = source[i];
            if (b == '\r')
            {
                result.Add((byte)'\r');
                result.Add((byte)'\n');
                if (i + 1 < source.Length && source[i + 1] == '\n') i++;
            }
            else if (b == '\n')
            {
                result.Add((byte)'\r');
                result.Add((byte)'\n');
            }
            else
            {
                result.Add(b);
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Replaces characters that Windows doesn't allow in file names. VBA module names are identifiers,
    /// so this should never change anything; it's a safety net. Uses a fixed list rather than
    /// Path.GetInvalidFileNameChars(), which differs between Windows and Linux.
    /// </summary>
    static string SafeFileName(string name)
    {
        const string invalid = "<>:\"/\\|?*";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(c < 32 || invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
