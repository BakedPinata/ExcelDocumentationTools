using System.Security.Cryptography;
using System.Text;
using PowerQueryDocumenter.Core.Export;

namespace PowerQueryDocumenter.Tests.Export;

public sealed class QueryFileTests
{
  private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

  [Fact]
  public void The_hash_is_the_start_of_the_files_SHA256_so_the_page_can_check_a_loaded_file()
  {
    var bytes = Encoding.UTF8.GetBytes("let\r\n    Source = 1\r\nin\r\n    Source\r\n");

    var file = QueryFile.FromBytes("Orders", "Orders.m", bytes);

    Assert.Equal("Orders", file.Query);
    Assert.Equal("Orders.m", file.FileName);
    Assert.Equal(Hash(bytes), file.Hash);
    Assert.Equal(16, file.Hash.Length);
    Assert.Equal("let\r\n    Source = 1\r\nin\r\n    Source\r\n", file.Text);
  }

  [Theory]
  [InlineData("utf-8")]
  [InlineData("utf-16")]
  [InlineData("utf-16BE")]
  public void A_byte_order_mark_sets_the_encoding_and_is_left_out_of_the_text_but_not_the_hash(string encoding)
  {
    var text = "let Région = \"Zürich\" in Région";
    var withMark = Encoding.GetEncoding(encoding);
    var bytes = withMark.GetPreamble().Concat(withMark.GetBytes(text)).ToArray();

    var file = QueryFile.FromBytes("Région", "Région.m", bytes);

    Assert.Equal(text, file.Text);
    Assert.Equal(Hash(bytes), file.Hash);
  }

  [Fact]
  public void Without_a_byte_order_mark_the_file_is_read_as_UTF8()
  {
    var file = QueryFile.FromBytes("A", "A.m", Encoding.UTF8.GetBytes("\"Zürich\""));

    Assert.Equal("\"Zürich\"", file.Text);
  }
}
