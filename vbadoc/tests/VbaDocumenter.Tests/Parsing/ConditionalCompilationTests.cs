using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Output;
using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Tests.Parsing;

/// <summary>
/// A name declared again under #If is one member (step A6 of the feedback plan): VBA compiles one branch and won't accept
/// a name declared twice, so the declarations are versions of the same member.
/// </summary>
public class ConditionalCompilationTests
{
  static ModuleModel Parse(string code)
  {
    var module = ModuleParser.Parse("Cache", VbaModuleKind.Standard, code);
    Assert.Empty(module.Warnings);
    return module;
  }

  static Member Only(ModuleModel module, string name) => Assert.Single(module.Members, m => m.Name == name);

  // The user's example, as they wrote it (lower-case "as" included), after the module's two header lines.
  const string EarlyOrLateBinding = """
      Attribute VB_Name = "Cache"
      Option Explicit
      #If EarlyBinding Then
          Private m_Dictionary as Scripting.Dictionary
      #Else
          Private m_Dictionary as Object
      #End If

      #If EarlyBinding Then
      Public Function GetMeTheDictionary() as Scripting.Dictionary
      #Else
      Public Function GetMeTheDictionary() as Object
      #End If

          If m_Dictionary Is Nothing Then

      #If EarlyBinding Then
          Set m_Dictionary = New Scripting.Dictionary
      #Else
          Set m_Dictionary = CreateObject("Scripting.Dictionary")
      #End If
              m_Dictionary.Add "Key1", True
              m_Dictionary.Add "Key2", False
              m_Dictionary.Add "Key3", True
          End If

          Set GetMeTheDictionary = m_Dictionary

      End Function
      """;

  [Fact]
  public void A_variable_declared_in_both_branches_is_one_member_spanning_the_block()
  {
    var variable = Only(Parse(EarlyOrLateBinding), "m_Dictionary");

    Assert.Equal(MemberKind.Variable, variable.Kind);
    Assert.Equal("Scripting.Dictionary", variable.Type);
    Assert.Equal(["Object"], variable.AlternativeTypes);
    Assert.Equal((3, 4, 7), (variable.StartLine, variable.DeclarationLine, variable.EndLine));
    Assert.Equal("Private m_Dictionary As Scripting.Dictionary | Object", MemberFormatter.Signature(variable));
  }

  [Fact]
  public void A_procedure_header_in_both_branches_before_its_body_is_one_procedure_with_no_warning()
  {
    var function = Only(Parse(EarlyOrLateBinding), "GetMeTheDictionary");

    Assert.Equal((9, 10, 29), (function.StartLine, function.DeclarationLine, function.EndLine));
    Assert.Equal(["Object"], function.AlternativeTypes);
    Assert.Equal("Public Function GetMeTheDictionary() As Scripting.Dictionary | Object", MemberFormatter.Signature(function));
    // Both versions of the body's statements are its body.
    Assert.Contains(function.Body, s => s.Text == "Set m_Dictionary = New Scripting.Dictionary");
    Assert.Contains(function.Body, s => s.Text == "Set m_Dictionary = CreateObject(\"Scripting.Dictionary\")");
  }

  [Fact]
  public void Two_complete_versions_in_different_branches_are_one_procedure_with_both_bodies()
  {
    var module = Parse("""
        Attribute VB_Name = "Cache"
        #If Mac Then
        Public Function Separator() As String
            Separator = "/"
        End Function
        #Else
        Public Function Separator() As String
            Separator = Application.PathSeparator
        End Function
        #End If
        Public Sub After()
        End Sub
        """);

    var function = Only(module, "Separator");
    Assert.Equal((2, 10), (function.StartLine, function.EndLine));
    Assert.Empty(function.AlternativeTypes);
    Assert.Equal(["Separator = \"/\"", "Separator = Application.PathSeparator"], function.Body.Select(s => s.Text));
    Assert.Equal((11, 12), (Only(module, "After").StartLine, Only(module, "After").EndLine));
  }

  [Fact]
  public void Versions_in_separate_blocks_and_ElseIf_branches_are_one_member()
  {
    var module = Parse("""
        Attribute VB_Name = "Cache"
        #If Win64 Then
        Private Handle As LongLong
        #ElseIf VBA7 Then
        Private Handle As LongPtr
        #Else
        Private Handle As Long
        #End If
        #If Debugging Then
        Private Const MODE As String = "debug"
        #End If
        #If Not Debugging Then
        Private Const MODE As String = "release"
        #End If
        """);

    var handle = Only(module, "Handle");
    Assert.Equal(["LongPtr", "Long"], handle.AlternativeTypes);
    Assert.Equal((2, 8), (handle.StartLine, handle.EndLine));
    var mode = Only(module, "MODE");
    Assert.Equal((9, 14), (mode.StartLine, mode.EndLine));
    Assert.Equal("\"debug\"", mode.Value); // the first version's
  }

  [Fact]
  public void A_parameter_declared_differently_in_each_version_shows_both_types()
  {
    var module = Parse("""
        Attribute VB_Name = "Cache"
        #If EarlyBinding Then
        Public Sub Fill(ByVal target As Scripting.Dictionary, Optional ByVal count As Long = 3)
        #Else
        Public Sub Fill(ByVal target As Object, Optional ByVal count As Long = 3)
        #End If
            target.Add "Key", count
        End Sub
        """);

    var fill = Only(module, "Fill");
    Assert.Single(fill.AlternativeParameters);
    Assert.Equal(["Scripting.Dictionary", "Object"], fill.ParameterTypes("target"));
    Assert.Equal(["Long"], fill.ParameterTypes("count"));
    Assert.Equal("Public Sub Fill(target As Scripting.Dictionary | Object, Optional count As Long = 3)", MemberFormatter.Signature(fill));
  }

  [Fact]
  public void A_Property_Get_and_Let_of_the_same_name_stay_apart_and_a_name_declared_twice_outside_If_is_left_alone()
  {
    var module = ModuleParser.Parse("Cache", VbaModuleKind.Class, """
        Attribute VB_Name = "Cache"
        Private m_Twice As Long
        Private m_Twice As String
        #If Mac Then
        Public Property Get Path() As String
        End Property
        #Else
        Public Property Let Path(ByVal value As String)
        End Property
        #End If
        """);

    Assert.Equal(2, module.Members.Count(m => m.Name == "m_Twice"));
    Assert.Equal([MemberKind.PropertyGet, MemberKind.PropertyLet], module.Members.Where(m => m.Name == "Path").Select(m => m.Kind));
  }

  [Fact]
  public void A_header_repeated_in_the_same_branch_is_still_a_missing_End_line()
  {
    var module = ModuleParser.Parse("Cache", VbaModuleKind.Standard, """
        Attribute VB_Name = "Cache"
        #If EarlyBinding Then
        Public Function Broken() As Long
        Public Function Broken() As Long
        End Function
        #End If
        """);

    Assert.Contains(module.Warnings, w => w.Contains("Broken has no End line", StringComparison.Ordinal));
  }
}
