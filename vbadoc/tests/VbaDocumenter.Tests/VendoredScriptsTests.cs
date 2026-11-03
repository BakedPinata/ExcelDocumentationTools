using VbaDocumenter.Core;

namespace VbaDocumenter.Tests;

public class VendoredScriptsTests
{
  [Fact]
  public void D3_and_its_licence_are_embedded()
  {
    Assert.True(VendoredScripts.D3.Code.Length > 100_000, $"d3 looks truncated ({VendoredScripts.D3.Code.Length} chars)");
    Assert.Contains("Permission", VendoredScripts.D3.LicenceText);
  }

  [Fact]
  public void D3_is_safe_to_inline_in_a_script_tag()
  {
    // A literal "</script" would end the inline <script> element early, and "<script" after "<!--"
    // changes how the HTML parser finds the end tag. Either would break the page.
    var code = VendoredScripts.D3.Code;

    Assert.DoesNotContain("</script", code, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("<script", code, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void D3_header_matches_pinned_version()
  {
    Assert.StartsWith($"// https://d3js.org v{VendoredScripts.D3Version} ", VendoredScripts.D3.Code);
  }
}
