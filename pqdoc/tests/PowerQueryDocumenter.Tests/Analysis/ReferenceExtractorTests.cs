using PowerQueryDocumenter.Core.Analysis;

namespace PowerQueryDocumenter.Tests.Analysis;

public sealed class ReferenceExtractorTests
{
  private static string[] Free(string m) => ReferenceExtractor.FindFreeIdentifiers(m).Order(StringComparer.Ordinal).ToArray();

  [Fact]
  public void Let_steps_are_definitions_not_references()
  {
    Assert.Equal(new[] { "Orders" }, Free("let Source = Orders, Filtered = Source in Filtered"));
  }

  [Fact]
  public void Quoted_step_names_are_definitions_and_their_uses_are_local()
  {
    Assert.Equal(
      new[] { "Orders", "Table.TransformColumnTypes" },
      Free("let #\"Changed Type\" = Table.TransformColumnTypes(Orders, {}) in #\"Changed Type\""));
  }

  [Fact]
  public void A_quoted_reference_matches_a_bare_definition()
  {
    Assert.Empty(Free("let Source = 1, Result = #\"Source\" in Result"));
  }

  [Fact]
  public void A_step_used_before_it_is_defined_is_still_local()
  {
    Assert.Empty(Free("let A = B, B = 1 in A"));
  }

  [Fact]
  public void Local_names_are_case_sensitive_like_M()
  {
    Assert.Equal(new[] { "Source" }, Free("let source = 1, Result = Source in Result"));
  }

  [Fact]
  public void Field_access_names_fields_not_queries()
  {
    Assert.Equal(new[] { "Orders", "Table.SelectRows" }, Free("Table.SelectRows(Orders, each [Customers] > 0)"));
  }

  [Fact]
  public void Generalised_field_names_with_spaces_are_ignored()
  {
    Assert.Empty(Free("each [Customer Name] & [Sales Region]"));
  }

  [Fact]
  public void Projection_names_fields_not_queries()
  {
    Assert.Equal(new[] { "Orders" }, Free("Orders[[Customers], [Sales]]"));
  }

  [Fact]
  public void Record_lookup_keys_and_field_access_are_not_references()
  {
    Assert.Equal(new[] { "Source" }, Free("Source{[Schema = \"dbo\", Item = \"Sales\"]}[Data]"));
  }

  [Fact]
  public void Record_literal_values_are_scanned_but_field_names_are_not()
  {
    Assert.Equal(new[] { "Orders" }, Free("[Customers = Orders, Total = 1]"));
  }

  [Fact]
  public void Record_fields_can_refer_to_each_other()
  {
    Assert.Empty(Free("[A = 1, B = A + 1]"));
  }

  [Fact]
  public void Generalised_record_field_names_are_definitions()
  {
    Assert.Equal(new[] { "Orders" }, Free("[Customer Name = Orders]"));
  }

  [Fact]
  public void Function_parameters_are_local_to_the_function()
  {
    Assert.Equal(new[] { "Orders" }, Free("(Sales as table, optional Region as text) as table => Sales & Region & Orders"));
  }

  [Fact]
  public void A_function_body_ends_at_the_next_step()
  {
    Assert.Equal(new[] { "Orders" }, Free("let F = (Orders) => Orders, G = Orders in G"));
  }

  [Fact]
  public void Each_binds_the_underscore()
  {
    Assert.Equal(new[] { "List.Transform", "Orders" }, Free("List.Transform(Orders, each _ + 1)"));
  }

  [Fact]
  public void Nested_let_steps_are_visible_in_their_body_only()
  {
    // B is defined by the inner let, so the outer step C refers to something else called B.
    Assert.Equal(new[] { "B" }, Free("let A = let B = 1 in B, C = B in C"));
  }

  [Fact]
  public void Recursive_reference_with_the_scoping_operator_is_local()
  {
    Assert.Empty(Free("let Fact = (n) => if n = 0 then 1 else n * @Fact(n - 1) in Fact"));
  }

  [Fact]
  public void Comments_and_text_never_produce_references()
  {
    Assert.Empty(Free("let a = \"Orders\" /* Customers */ // Sales\nin a"));
  }

  [Fact]
  public void Section_member_names_are_not_references()
  {
    Assert.Equal(new[] { "Orders" }, Free("section Section1; shared Sales = Orders;"));
  }

  [Fact]
  public void Type_names_are_returned_as_written_for_the_analyser_to_judge()
  {
    Assert.Equal(new[] { "table", "text" }, Free("let T = type table [A = text] in T"));
  }

  [Fact]
  public void Malformed_input_does_not_throw()
  {
    var exception = Record.Exception(() => Free("let a = ( [ { , ] ) } in in let"));

    Assert.Null(exception);
  }
}
