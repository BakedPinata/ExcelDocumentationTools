using PowerQueryDocumenter.Core.Extraction;

namespace PowerQueryDocumenter.Tests.Extraction;

/// <summary>Splitting a section document into queries (FR-IN-3), beyond what the sample workbooks cover.</summary>
public sealed class SectionSplitterTests
{
  private static string ErrorFor(string section) => Assert.Throws<ExtractionException>(() => SectionSplitter.Split(section)).Message;

  [Fact]
  public void Splits_members_and_trims_the_space_around_each_expression()
  {
    var queries = SectionSplitter.Split("section Section1;\n\nshared A =  1 + 2 ;\nshared B = A * 2;\n");

    Assert.Equal(new[] { new WorkbookQuery("A", "1 + 2", null), new WorkbookQuery("B", "A * 2", null) }, queries);
  }

  [Fact]
  public void Quoted_names_are_unescaped()
  {
    var query = Assert.Single(SectionSplitter.Split("section Section1;\nshared #\"Say \"\"hi\"\"\" = \"hi\";"));

    Assert.Equal("Say \"hi\"", query.Name);
  }

  [Fact]
  public void A_member_without_shared_is_still_a_query()
  {
    Assert.Equal(new[] { "A", "B" }, SectionSplitter.Split("section Section1;\nA = 1;\nshared B = 2;").Select(q => q.Name));
  }

  [Fact]
  public void Semicolons_inside_text_and_comments_do_not_end_a_query()
  {
    var query = Assert.Single(SectionSplitter.Split("section Section1;\nshared A = \"a;b\" & /* ; */ \"c\" // ;\n;"));

    Assert.Equal("\"a;b\" & /* ; */ \"c\" // ;", query.Expression);
  }

  [Fact]
  public void The_description_is_only_a_top_level_field_of_the_attribute_record()
  {
    var queries = SectionSplitter.Split(
      "section Section1;\n" +
      "[ Other = [ Description = \"nested\" ], Description = \"The real one\" ] shared A = 1;\n" +
      "[ Note = \"no description\" ] shared B = 2;");

    Assert.Equal("The real one", queries[0].Description);
    Assert.Null(queries[1].Description);
    Assert.Equal("1", queries[0].Expression);
  }

  [Fact]
  public void A_section_that_cannot_be_split_says_where()
  {
    Assert.Contains("section declaration", ErrorFor("shared A = 1;"));
    Assert.Contains("no \"=\" after the name of query A (line 2", ErrorFor("section Section1;\nshared A 1;"));
    Assert.Contains("query A has no \";\" at its end", ErrorFor("section Section1;\nshared A = 1"));
    Assert.Contains("query A is empty", ErrorFor("section Section1;\nshared A = ;"));
    Assert.Contains("name is missing", ErrorFor("section Section1;\nshared = 1;"));
    Assert.Contains("no closing \"]\"", ErrorFor("section Section1;\n[ Description = \"x\" shared A = 1;"));
  }

  [Fact]
  public void Two_queries_with_the_same_name_in_any_case_stop_the_run()
  {
    Assert.Contains("two queries called sales", ErrorFor("section Section1;\nshared Sales = 1;\nshared sales = 2;"));
  }
}
