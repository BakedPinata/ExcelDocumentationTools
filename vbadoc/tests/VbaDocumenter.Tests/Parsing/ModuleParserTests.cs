using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>Parser tests against the SampleVba fixture.</summary>
public class ModuleParserTests
{
  static ModuleModel Sample(string fileName) => Fixtures.Parse("SampleVba", fileName);

  static Member Single(ModuleModel module, string name, MemberKind kind) =>
      module.Members.Single(m => m.Name == name && m.Kind == kind);

  public static TheoryData<string> SampleVbaFiles =>
      new(Directory.GetFiles(Fixtures.SampleVbaFolder).Select(Path.GetFileName).Order().ToArray()!);

  [Theory]
  [MemberData(nameof(SampleVbaFiles))]
  public void Parses_every_SampleVba_module_without_warnings(string fileName)
  {
    var module = Sample(fileName);

    Assert.Empty(module.Warnings);
    Assert.All(module.Members, m => Assert.True(m.EndLine >= m.DeclarationLine, $"{m} has no valid end line"));
  }

  [Fact]
  public void Lists_members_in_source_order_with_kinds_visibility_and_types()
  {
    var order = Sample("Order.cls");

    Assert.Equal(
    [
        ("m_Id", MemberKind.Variable, Visibility.Private, "String"),
            ("m_Status", MemberKind.Variable, Visibility.Private, "String"),
            ("m_Amount", MemberKind.Variable, Visibility.Private, "Double"),
            ("Class_Initialize", MemberKind.Sub, Visibility.Private, null),
            ("Id", MemberKind.PropertyGet, Visibility.Public, "String"),
            ("Status", MemberKind.PropertyGet, Visibility.Public, "String"),
            ("Status", MemberKind.PropertyLet, Visibility.Public, null),
            ("Load", MemberKind.Sub, Visibility.Public, null),
            ("Parse", MemberKind.Sub, Visibility.Public, null),
            ("IsValid", MemberKind.Function, Visibility.Public, "Boolean"),
            ("Describe", MemberKind.Function, Visibility.Public, "String"),
        ], order.Members.Select(m => (m.Name, m.Kind, m.Visibility, m.Type)));
  }

  [Fact]
  public void Records_line_ranges()
  {
    var order = Sample("Order.cls");

    Assert.Equal((8, 10), Range(Single(order, "Class_Initialize", MemberKind.Sub)));
    Assert.Equal((20, 22), Range(Single(order, "Status", MemberKind.PropertyLet)));
    Assert.Equal((38, 40), Range(Single(order, "Describe", MemberKind.Function)));
    Assert.Equal((5, 5), Range(Single(order, "m_Status", MemberKind.Variable)));
    Assert.Equal(40, order.LineCount);
  }

  static (int, int) Range(Member m) => (m.StartLine, m.EndLine);

  [Fact]
  public void Reads_parameters()
  {
    var statusLet = Single(Sample("Order.cls"), "Status", MemberKind.PropertyLet);

    Assert.Equal(new Parameter("value", "String", false, true, false, false, null), statusLet.Parameters.Single());
  }

  [Fact]
  public void Reads_locals_and_keeps_every_statement_including_declarations()
  {
    var process = Single(Sample("Main.bas"), "ProcessOrders", MemberKind.Sub);

    Assert.Equal(
        [("svc", "OrderService"), ("repo", "IOrderRepository"), ("reporter", "ProgressReporter"), ("o", "Order"), ("legacy", "Object")],
        process.Locals.Select(l => (l.Name, l.Type)));
    Assert.Contains(new Statement("Dim svc As OrderService", 5), process.Body);
    Assert.Contains(new Statement("With svc", 32), process.Body);
    Assert.Contains(new Statement(".Summarise", 33), process.Body);
  }

  [Fact]
  public void Splits_statements_at_colons_but_not_at_named_arguments()
  {
    var body = Single(Sample("Main.bas"), "ProcessOrders", MemberKind.Sub).Body;

    Assert.Contains(new Statement("Set reporter = New ProgressReporter", 19), body);
    Assert.Contains(new Statement("reporter.Attach svc", 19), body);
    Assert.Contains(new Statement("legacy.ExportAll repo:=Nothing", 37), body);
  }

  [Fact]
  public void Drops_line_labels_and_comments_from_the_body()
  {
    var body = Single(Sample("Main.bas"), "ProcessOrders", MemberKind.Sub).Body;

    Assert.DoesNotContain(body, s => s.Text.StartsWith("ErrHandler"));
    Assert.Contains(new Statement("Logger.LogError \"ProcessOrders failed: \" & Err.Description", 41), body);
    Assert.DoesNotContain(body, s => s.Text.Contains("nothing"));
    Assert.Contains(new Statement("Case Else", 27), body);
  }

  [Fact]
  public void Keeps_a_single_line_If_as_one_statement()
  {
    var factorial = Single(Sample("Main.bas"), "Factorial", MemberKind.Function);

    Assert.Equal("If n <= 1 Then Factorial = 1 Else Factorial = n * Factorial(n - 1)", factorial.Body.Single().Text);
  }

  [Fact]
  public void Strips_comments_that_contain_colons_and_member_names()
  {
    var writeLine = Single(Sample("Logger.bas"), "WriteLine", MemberKind.Sub);

    Assert.Equal("Debug.Print Format(Now, \"hh:nn:ss\") & \" [\" & level & \"] \" & msg", writeLine.Body.Single().Text);
  }

  [Fact]
  public void Reads_WithEvents_variables_and_skips_their_attribute_line()
  {
    var reporter = Sample("ProgressReporter.cls");
    var variable = Single(reporter, "m_svc", MemberKind.Variable);

    Assert.True(variable.IsWithEvents);
    Assert.Equal("OrderService", variable.Type);
    Assert.Equal(["m_svc", "Attach", "m_svc_Progress"], reporter.Members.Select(m => m.Name));
  }

  [Fact]
  public void Reads_events_and_implements()
  {
    var progress = Single(Sample("OrderService.cls"), "Progress", MemberKind.Event);

    Assert.Equal(Visibility.Public, progress.Visibility);
    Assert.Equal("message", progress.Parameters.Single().Name);
    Assert.Equal(["IOrderRepository"], Sample("CsvOrderRepository.cls").Implements);
  }

  [Fact]
  public void Reads_public_module_variables()
  {
    var useCsv = Single(Sample("AppInit.bas"), "g_UseCsv", MemberKind.Variable);

    Assert.Equal((Visibility.Public, "Boolean"), (useCsv.Visibility, useCsv.Type));
  }

  [Fact]
  public void Reads_module_attributes_and_options()
  {
    var sheet = Sample("Sheet1.doc.cls");

    Assert.Equal("0{00020820-0000-0000-C000-000000000046}", sheet.Attributes["VB_Base"]);
    Assert.Equal(["Option Explicit"], sheet.Options);
  }

  [Fact]
  public void Parses_the_exported_file_so_line_numbers_include_the_class_header()
  {
    // ProjectParser parses the file as exported, which has the 4-line class header in front.
    var project = WorkbookReader.Read(SyntheticWorkbook.Xlsm(Fixtures.SampleVbaModules()));
    var order = ProjectParser.Parse(project).Modules.Single(m => m.Name == "Order");

    Assert.Equal((12, 14), Range(Single(order, "Class_Initialize", MemberKind.Sub)));
    Assert.Equal(44, order.LineCount);
  }

  [Fact]
  public void Reports_a_missing_End_line_and_carries_on()
  {
    var module = ModuleParser.Parse("M", VbaModuleKind.Standard,
        "Sub A()\r\n    x = 1\r\nSub B()\r\nEnd Sub\r\n");

    Assert.Equal(["A", "B"], module.Members.Select(m => m.Name));
    Assert.Equal(2, module.Members[0].EndLine);
    Assert.Contains(module.Warnings, w => w.Contains("Sub A has no End line"));
  }

  [Fact]
  public void Warns_about_module_level_lines_it_does_not_understand()
  {
    var module = ModuleParser.Parse("M", VbaModuleKind.Standard, "Option Explicit\r\nThis is not VBA\r\n");

    Assert.Equal("Line 2: unrecognised module-level line 'This is not VBA'.", module.Warnings.Single());
  }
}
