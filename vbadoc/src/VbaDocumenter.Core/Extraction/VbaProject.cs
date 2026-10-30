namespace VbaDocumenter.Core.Extraction;

/// <summary>The four kinds of VBA module. The kind decides the file extension and how the module is shown.</summary>
public enum VbaModuleKind
{
    /// <summary>Standard module (.bas).</summary>
    Standard,
    /// <summary>Class module (.cls).</summary>
    Class,
    /// <summary>UserForm (.frm).</summary>
    Form,
    /// <summary>ThisWorkbook and sheet modules (.cls).</summary>
    Document,
}

/// <summary>One module as stored in the workbook.</summary>
public sealed class VbaModule
{
    public required string Name { get; init; }
    public required VbaModuleKind Kind { get; init; }

    /// <summary>Name of the stream inside the VBA storage. Usually the same as <see cref="Name"/>.</summary>
    public required string StreamName { get; init; }

    /// <summary>
    /// The module's source exactly as stored (after decompression), in the project's code page.
    /// Kept as bytes so that writing it back out is lossless, even for characters that
    /// don't round-trip through .NET strings.
    /// </summary>
    public required byte[] SourceBytes { get; init; }

    /// <summary><see cref="SourceBytes"/> decoded with the project's code page.</summary>
    public required string Source { get; init; }
}

/// <summary>Everything read from a workbook's VBA project.</summary>
public sealed class VbaProject
{
    /// <summary>The VBA project name, e.g. "VBAProject".</summary>
    public required string Name { get; init; }

    /// <summary>Windows code page used for all module source, e.g. 1252.</summary>
    public required int CodePage { get; init; }

    /// <summary>Modules sorted by name (case-insensitive) so everything downstream is in a stable order.</summary>
    public required IReadOnlyList<VbaModule> Modules { get; init; }
}

/// <summary>
/// A problem with the input that the user can understand and act on (encrypted file, no VBA project, corrupt data).
/// The console app prints the message without a stack trace.
/// </summary>
public sealed class ExtractionException(string message, Exception? inner = null) : Exception(message, inner);
