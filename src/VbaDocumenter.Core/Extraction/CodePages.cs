using System.Text;

namespace VbaDocumenter.Core.Extraction;

/// <summary>Looks up Windows code pages such as 1252, which .NET only knows after registering the code-pages provider.</summary>
public static class CodePages
{
    static CodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Get(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new ExtractionException($"The VBA project uses code page {codePage}, which this tool can't decode.", ex);
        }
    }
}
