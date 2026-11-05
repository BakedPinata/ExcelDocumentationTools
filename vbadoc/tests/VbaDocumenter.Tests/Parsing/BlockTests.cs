using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>
/// The block statements around each statement in a procedure (step E3 of the feedback plan): If, ElseIf and Else
/// branches, single-line If, Select Case, loops, With, On Error handlers, and blocks under #If.
/// </summary>
public class BlockTests
{
  static Member Procedure(params string[] body)
  {
    var text = string.Join("\r\n", new[] { "Attribute VB_Name = \"Blocks\"", "Option Explicit", "Public Sub Run()" }.Concat(body).Append("End Sub").Append(""));
    return ModuleParser.Parse("Blocks", VbaModuleKind.Standard, text).Members.Single(m => m.Name == "Run");
  }

  // Each statement with the blocks it's in, outermost first, as "kind: text".
  static (string, string)[] Nesting(Member procedure) =>
      procedure.Body.Select(s => (s.Text, string.Join(" > ", Chain(procedure, s.Block)))).ToArray();

  static IEnumerable<string> Chain(Member procedure, int? block)
  {
    var chain = new List<string>();
    for (var at = block; at is { } id; at = procedure.Blocks[id].Parent) chain.Insert(0, procedure.Blocks[id].Kind + ": " + procedure.Blocks[id].Text);
    return chain;
  }

  [Fact]
  public void Blocks_nest_and_a_blocks_own_statement_is_in_the_block_around_it()
  {
    var run = Procedure(
        "    For Each item In items",
        "        If IsReady(item) Then",
        "            Process item",
        "        End If",
        "    Next item",
        "    Finish");

    Assert.Equal(
        [("For Each item In items", ""),
         ("If IsReady(item) Then", "for: For Each item In items"),
         ("Process item", "for: For Each item In items > if: If IsReady(item) Then"),
         ("End If", "for: For Each item In items > if: If IsReady(item) Then"),
         ("Next item", "for: For Each item In items"),
         ("Finish", "")],
        Nesting(run));
  }

  [Fact]
  public void ElseIf_and_Else_are_branches_beside_their_If()
  {
    var run = Procedure(
        "    If a Then",
        "        One",
        "    ElseIf b Then",
        "        Two",
        "    ElseIf c Then",
        "        Three",
        "    Else",
        "        Four",
        "    End If");

    string[] calls = ["One", "Two", "Three", "Four"];
    Assert.Equal([("if", (int?)null), ("elseIf", null), ("elseIf", null), ("else", null)], run.Blocks.Select(b => (b.Kind, b.Parent)));
    Assert.Equal([0, 1, 2, 3], run.Body.Where(s => calls.Contains(s.Text)).Select(s => s.Block ?? -1));
  }

  [Fact]
  public void Each_Case_is_a_block_in_its_Select_Case()
  {
    var run = Procedure(
        "    Select Case mode",
        "        Case 1, 2",
        "            First",
        "        Case Is > 5",
        "            Second",
        "        Case Else",
        "            Third",
        "    End Select",
        "    After");

    Assert.Equal(
        [("Select Case mode", ""),
         ("Case 1, 2", "select: Select Case mode"),
         ("First", "select: Select Case mode > case: Case 1, 2"),
         ("Case Is > 5", "select: Select Case mode"),
         ("Second", "select: Select Case mode > case: Case Is > 5"),
         ("Case Else", "select: Select Case mode"),
         ("Third", "select: Select Case mode > case: Case Else"),
         ("End Select", "select: Select Case mode > case: Case Else"),
         ("After", "")],
        Nesting(run));
  }

  [Fact]
  public void A_single_line_If_has_its_Then_and_Else_parts_with_everything_after_Then_on_the_line()
  {
    var run = Procedure(
        "    If ready Then Start: Go Else Wait: Retry",
        "    After");

    Assert.Equal(
        [("If ready Then", ""),
         ("Start", "if: If ready Then"),
         ("Go", "if: If ready Then"),
         ("Else", ""),
         ("Wait", "else: Else"),
         ("Retry", "else: Else"),
         ("After", "")],
        Nesting(run));
  }

  [Fact]
  public void Then_or_Else_in_a_string_doesnt_split_a_single_line_If()
  {
    var run = Procedure("    If label = \"Then Else\" Then Show \"Else\"");

    Assert.Equal([("If label = \"Then Else\" Then", ""), ("Show \"Else\"", "if: If label = \"Then Else\" Then")], Nesting(run));
  }

  [Fact]
  public void Loops_and_With_close_at_their_own_ends()
  {
    var run = Procedure(
        "    Do While more",
        "        With target",
        "            .Step",
        "        End With",
        "    Loop",
        "    While waiting",
        "        Poll",
        "    Wend",
        "    For i = 1 To 2: For j = 1 To 2",
        "        Cell i, j",
        "    Next j, i",
        "    Done");

    var nesting = Nesting(run).ToDictionary(n => n.Item1, n => n.Item2);
    Assert.Equal("do: Do While more > with: With target", nesting[".Step"]);
    Assert.Equal("while: While waiting", nesting["Poll"]);
    Assert.Equal("for: For i = 1 To 2 > for: For j = 1 To 2", nesting["Cell i, j"]);
    Assert.Equal("", nesting["Done"]); // "Next j, i" ends both loops
  }

  [Fact]
  public void An_On_Error_handler_is_a_block_from_its_label_to_the_end()
  {
    var run = Procedure(
        "    On Error GoTo Failed",
        "    Work",
        "    Exit Sub",
        "Failed:",
        "    Report Err.Description",
        "    Resume Next");

    var nesting = Nesting(run).ToDictionary(n => n.Item1, n => n.Item2);
    Assert.Equal("", nesting["Work"]);
    Assert.Equal("onError: On error: Failed", nesting["Report Err.Description"]);
    Assert.Equal("onError: On error: Failed", nesting["Resume Next"]);
    Assert.Equal(7, run.Blocks.Single().Line); // the label's line in the module
  }

  [Fact]
  public void A_label_On_Error_doesnt_name_isnt_a_handler()
  {
    var run = Procedure(
        "    On Error Resume Next",
        "    If missing Then GoTo Skip",
        "    Work",
        "Skip:",
        "    Tidy");

    Assert.DoesNotContain(run.Blocks, b => b.Kind == "onError");
    Assert.Null(run.Body.Single(s => s.Text == "Tidy").Block);
  }

  [Fact]
  public void Under_If_each_branch_starts_from_the_blocks_open_at_the_If_and_the_first_carries_on()
  {
    var run = Procedure(
        "#If VBA7 Then",
        "    If wide Then",
        "#Else",
        "    If narrow Then",
        "#End If",
        "        Shared",
        "    End If",
        "    After");

    var nesting = Nesting(run).ToDictionary(n => n.Item1, n => n.Item2);
    Assert.Equal("", nesting["If narrow Then"]); // not inside "If wide Then"
    Assert.Equal("if: If wide Then", nesting["Shared"]);
    Assert.Equal("", nesting["After"]);
  }

  [Fact]
  public void A_closing_statement_with_nothing_open_is_just_a_statement()
  {
    var run = Procedure("    End If", "    Next", "    Loop", "    Work");

    Assert.Empty(run.Blocks);
    Assert.All(run.Body, s => Assert.Null(s.Block));
  }
}
