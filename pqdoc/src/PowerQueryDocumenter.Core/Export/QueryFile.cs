using System.Security.Cryptography;
using System.Text;

namespace PowerQueryDocumenter.Core.Export;

/// <summary>
/// A query's <c>.m</c> file, as the page needs it (FR-SRC-2): the file's name, so Load folder can find it; a short hash
/// of its bytes, so the page can tell when a loaded folder no longer matches; and its text, which only goes in the page
/// with <c>--embed-source</c>.
/// </summary>
/// <param name="Query">The query's name.</param>
/// <param name="FileName">The file's name, without a folder.</param>
/// <param name="Hash">The first 16 hex digits of the SHA-256 of the file's bytes, as vbadoc's page uses.</param>
/// <param name="Text">The file's text.</param>
public sealed record QueryFile(string Query, string FileName, string Hash, string Text)
{
  /// <summary>
  /// Makes the record from a file's bytes, decoding them as pqdoc reads <c>.m</c> files: UTF-8, unless a byte order
  /// mark says otherwise (a hand export from Excel can be UTF-16).
  /// </summary>
  public static QueryFile FromBytes(string query, string fileName, byte[] bytes)
  {
    ArgumentNullException.ThrowIfNull(bytes);
    using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
    return new QueryFile(query, fileName, Convert.ToHexStringLower(SHA256.HashData(bytes))[..16], reader.ReadToEnd());
  }
}
