using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>
/// What a module or member says about itself (step L5 of the generalisation plan): its VB_Description attribute,
/// otherwise the comments directly above it, and for a module the comments at its top.
/// </summary>
public class DescriptionTests
{
  static ModuleModel Module(params string[] lines) =>
      ModuleParser.Parse("Described", VbaModuleKind.Standard, string.Join("\r\n", lines.Prepend("Attribute VB_Name = \"Described\"").Append("")));

  static string? Of(ModuleModel module, string member) => module.Members.First(m => m.Name == member).Description;

  [Fact]
  public void A_members_description_is_the_comments_directly_above_it()
  {
    var module = Module(
        "Option Explicit",
        "",
        "' Works out the total, including tax.",
        "' Rounds to two places.",
        "Public Function Total(ByVal net As Double) As Double",
        "    ' not this one",
        "    Total = net * 1.1",
        "End Function",
        "",
        "' Not directly above: there's a blank line between.",
        "",
        "Public Sub Plain()",
        "End Sub");

    Assert.Equal("Works out the total, including tax.\nRounds to two places.", Of(module, "Total"));
    Assert.Null(Of(module, "Plain"));
  }

  [Fact]
  public void A_VB_Description_attribute_comes_before_the_comments_and_a_propertys_accessors_share_it()
  {
    var module = Module(
        "' A comment that the attribute outranks.",
        "Public Sub Run()",
        "Attribute Run.VB_Description = \"Runs the \"\"whole\"\" import.\"",
        "End Sub",
        "",
        "Public Property Get Name() As String",
        "Attribute Name.VB_Description = \"The customer's name.\"",
        "End Property",
        "Public Property Let Name(ByVal value As String)",
        "End Property",
        "",
        "Public Count As Long",
        "Attribute Count.VB_VarDescription = \"How many were read.\"");

    Assert.Equal("Runs the \"whole\" import.", Of(module, "Run"));
    Assert.Equal(["The customer's name.", "The customer's name."], module.Members.Where(m => m.Name == "Name").Select(m => m.Description));
    Assert.Equal("How many were read.", Of(module, "Count"));
  }

  [Fact]
  public void A_Rubberduck_description_annotation_is_the_description_and_other_annotations_are_left_out()
  {
    var module = Module(
        "'@EntryPoint",
        "'@Description(\"Starts the import.\")",
        "' and this isn't it",
        "Public Sub Start()",
        "End Sub",
        "",
        "'@Description \"Without brackets.\"",
        "Public Sub Other()",
        "End Sub",
        "",
        "'@Ignore ProcedureNotUsed",
        "' Only the comment counts.",
        "Public Sub Third()",
        "End Sub",
        "",
        "'@Ignore ProcedureNotUsed",
        "Public Sub Fourth()",
        "End Sub");

    Assert.Equal("Starts the import.", Of(module, "Start"));
    Assert.Equal("Without brackets.", Of(module, "Other"));
    Assert.Equal("Only the comment counts.", Of(module, "Third"));
    Assert.Null(Of(module, "Fourth"));
  }

  [Fact]
  public void Decoration_is_dropped_Rem_counts_and_a_comment_ending_the_declarations_line_is_the_last_resort()
  {
    var module = Module(
        "'==========================",
        "' Formats a date.",
        "'",
        "' Uses the ISO form.",
        "'--------------------------",
        "Public Function Iso(ByVal d As Date) As String",
        "End Function",
        "",
        "Rem An old-style comment.",
        "Public Sub Old()",
        "End Sub",
        "",
        "Public Const MaxRows As Long = 500 ' the most rows read at once",
        "Public Const Marker As String = \"it's\" ' after a string holding a quote mark",
        "Public Const Bare As Long = 1");

    Assert.Equal("Formats a date.\n\nUses the ISO form.", Of(module, "Iso"));
    Assert.Equal("An old-style comment.", Of(module, "Old"));
    Assert.Equal("the most rows read at once", Of(module, "MaxRows"));
    Assert.Equal("after a string holding a quote mark", Of(module, "Marker"));
    Assert.Null(Of(module, "Bare"));
  }

  [Fact]
  public void A_modules_description_is_the_first_comments_at_its_top_above_its_first_member()
  {
    var module = Module(
        "'@Folder(\"Import\")",
        "Option Explicit",
        "",
        "' Reads orders from the CSV files",
        "' the warehouse sends each night.",
        "",
        "' A second block, which isn't it.",
        "",
        "' This one belongs to Run.",
        "Public Sub Run()",
        "End Sub");

    Assert.Equal("Reads orders from the CSV files\nthe warehouse sends each night.", module.Description);
    Assert.Equal("This one belongs to Run.", Of(module, "Run"));
  }

  [Fact]
  public void A_modules_attribute_or_annotation_comes_before_its_comments_and_a_module_without_any_has_none()
  {
    var attribute = ModuleParser.Parse("Thing", VbaModuleKind.Class, string.Join("\r\n",
        "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END",
        "Attribute VB_Name = \"Thing\"", "Attribute VB_Description = \"One thing in the list.\"",
        "' A comment as well.", "Option Explicit", ""));
    var annotation = Module("' Some comment first.", "", "'@ModuleDescription(\"Shared helpers.\")", "Option Explicit");
    var none = Module("Option Explicit", "", "' Run's own comment.", "Public Sub Run()", "End Sub");

    Assert.Equal("One thing in the list.", attribute.Description);
    Assert.Equal("Shared helpers.", annotation.Description);
    Assert.Null(none.Description);   // the only comment is its first member's
    Assert.Null(Module("Option Explicit").Description);
  }

  [Fact]
  public void A_member_declared_again_under_If_takes_only_the_comments_straight_above_it()
  {
    var module = Module(
        "#If VBA7 Then",
        "    ' Sleeps, on 64-bit Office.",
        "    Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As LongPtr)",
        "    Private Declare PtrSafe Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long",
        "#Else",
        "    Private Declare Sub Sleep Lib \"kernel32\" (ByVal ms As Long)",
        "    Private Declare Function Ticks Lib \"kernel32\" Alias \"GetTickCount\" () As Long",
        "#End If");

    Assert.Equal("Sleeps, on 64-bit Office.", Of(module, "Sleep"));
    Assert.Null(Of(module, "Ticks"));
  }

  [Fact]
  public void A_very_long_description_is_cut()
  {
    var module = Module(Enumerable.Range(0, 60).Select(i => "' Line " + i + " of a long history of changes.").Append("Public Sub Run()").Append("End Sub").ToArray());

    var description = Of(module, "Run")!;
    Assert.InRange(description.Length, Descriptions.MaxLength - 1, Descriptions.MaxLength);
    Assert.EndsWith("…", description);
  }
}
