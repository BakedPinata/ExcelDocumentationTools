using System.Text.Json;
using System.Text.RegularExpressions;
using VbaDocumenter.Core;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Output;

public class HtmlPageWriterTests
{
  static string Build(bool embed, IEnumerable<SyntheticModule>? modules = null, string title = "SampleVba.xlsm")
  {
    var project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(modules ?? Fixtures.SampleVbaModules()));
    return HtmlPageWriter.Build(DependencyAnalyser.Analyse(ProjectParser.Parse(project)), project, title, embed);
  }

  static JsonElement Data(string html)
  {
    var json = Regex.Match(html, "<script type=\"application/json\" id=\"vbadoc-data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
    return JsonDocument.Parse(json).RootElement;
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Is_identical_every_time_it_is_built(bool embed)
  {
    Assert.Equal(Build(embed), Build(embed));
  }

  [Fact]
  public void Inlines_the_vendored_scripts_with_their_licences_and_refers_to_no_other_file()
  {
    var html = Build(embed: false);

    Assert.Contains(VendoredScripts.D3.Code.Trim(), html);
    Assert.Contains($"/*! d3 {VendoredScripts.D3Version} (ISC licence)", html);
    Assert.Contains($"/*! mermaid {VendoredScripts.MermaidVersion} (MIT licence)", html);
    Assert.DoesNotMatch("<script[^>]+src=", html);
    Assert.DoesNotMatch("<link[^>]+href=\"(?!data:)", html);
    Assert.Contains("Content-Security-Policy\" content=\"default-src 'none';", html);
  }

  [Fact]
  public void Embeds_the_favicon_as_an_svg_data_uri()
  {
    var href = Regex.Match(Build(embed: false), "<link rel=\"icon\" type=\"image/svg\\+xml\" href=\"data:image/svg\\+xml;base64,([^\"]+)\">").Groups[1].Value;

    var svg = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(href));
    Assert.StartsWith("<svg", svg);
  }

  [Fact]
  public void Shows_which_build_of_vbadoc_made_it()
  {
    var html = Build(embed: false);
    var stamp = ToolVersion.Current.ToString().Replace("&", "&amp;");

    Assert.Contains($"<meta name=\"generator\" content=\"{stamp}\">", html);
    Assert.Contains($">{stamp}</span>", html);
    Assert.Contains(ToolVersion.Current.Description, html);
  }

  [Fact]
  public void Links_the_version_to_the_repository_in_a_new_tab()
  {
    var link = Regex.Match(Build(embed: false), "<a class=\"tool-version\"[^>]*>").Value;

    Assert.Contains($"href=\"{ToolVersion.RepositoryUrl}\"", link);
    Assert.Contains("target=\"_blank\"", link);
    Assert.Contains("rel=\"noopener noreferrer\"", link);
  }

  [Fact]
  public void Uses_LF_line_endings_throughout()
  {
    Assert.DoesNotContain("\r", Build(embed: true));
  }

  [Fact]
  public void Records_the_code_page_and_a_hash_of_each_file()
  {
    var data = Data(Build(embed: false));

    Assert.Equal("windows-1252", data.GetProperty("project").GetProperty("encoding").GetString());
    Assert.Equal("folder", data.GetProperty("project").GetProperty("sourceMode").GetString());
    var order = data.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "Order");
    Assert.Equal("Order.cls", order.GetProperty("file").GetString());
    Assert.Matches("^[0-9a-f]{16}$", order.GetProperty("hash").GetString()!);
  }

  [Fact]
  public void Folder_mode_contains_no_source()
  {
    var html = Build(embed: false);

    Assert.DoesNotContain("class=\"vbadoc-source\"", html);
    Assert.DoesNotContain("Describe = m_Id", html);
  }

  [Fact]
  public void Embedded_mode_has_one_block_per_module()
  {
    var html = Build(embed: true);

    Assert.Equal(12, Regex.Matches(html, "<pre hidden class=\"vbadoc-source\"").Count);
    Assert.Contains("<pre hidden class=\"vbadoc-source\" data-module=\"Order\">\nVERSION 1.0 CLASS\n", html);
  }

  [Fact]
  public void Embedded_source_and_data_cannot_break_out_of_the_page()
  {
    // VBA that contains HTML, including a closing script tag, in a string and in a member name's neighbourhood.
    var html = Build(embed: true, modules:
    [
        new SyntheticModule("Tricky", VbaModuleKind.Standard,
                "Attribute VB_Name = \"Tricky\"\r\nPublic Const MARKUP = \"</script><b>&amp;</b>\"\r\n"),
        ], title: "<Book & Co>.xlsm");

    Assert.Contains("Public Const MARKUP = &quot;&lt;/script&gt;&lt;b&gt;&amp;amp;&lt;/b&gt;&quot;", html);
    Assert.Contains("<title>&lt;Book &amp; Co&gt;.xlsm</title>", html);
    // The constant's signature in the JSON has "<" escaped, so it can't close the data script either.
    Assert.DoesNotContain("\"</script>", html);
    Assert.Contains("\\u003C/script\\u003E", html);
  }

  [Fact]
  public void Write_only_rewrites_a_changed_page()
  {
    var folder = Path.Combine(Path.GetTempPath(), $"vbadoc-page-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try
    {
      Assert.True(HtmlPageWriter.Write(folder, "<p>one</p>"));
      Assert.False(HtmlPageWriter.Write(folder, "<p>one</p>"));
      Assert.True(HtmlPageWriter.Write(folder, "<p>two</p>"));
    }
    finally
    {
      Directory.Delete(folder, recursive: true);
    }
  }
}
