using VbaDocumenter.Core;

namespace VbaDocumenter.Tests;

public class VendoredScriptsTests
{
    public static TheoryData<string> ScriptNames => new() { "d3", "mermaid" };

    static VendoredScript Get(string name) => name == "d3" ? VendoredScripts.D3 : VendoredScripts.Mermaid;

    [Theory]
    [MemberData(nameof(ScriptNames))]
    public void Script_and_licence_are_embedded(string name)
    {
        var script = Get(name);

        Assert.True(script.Code.Length > 100_000, $"{name} looks truncated ({script.Code.Length} chars)");
        Assert.Contains("Permission", script.LicenseText);
    }

    [Theory]
    [MemberData(nameof(ScriptNames))]
    public void Script_is_safe_to_inline_in_a_script_tag(string name)
    {
        // A literal "</script" would end the inline <script> element early, and "<script" after "<!--"
        // changes how the HTML parser finds the end tag. Either would break the page.
        var code = Get(name).Code;

        Assert.DoesNotContain("</script", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void D3_header_matches_pinned_version()
    {
        Assert.StartsWith($"// https://d3js.org v{VendoredScripts.D3Version} ", VendoredScripts.D3.Code);
    }

    [Fact]
    public void Mermaid_registers_its_global()
    {
        Assert.Contains("globalThis[\"mermaid\"]", VendoredScripts.Mermaid.Code);
    }
}
