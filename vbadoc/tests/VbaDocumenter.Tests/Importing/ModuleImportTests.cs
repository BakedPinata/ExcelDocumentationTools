using CodeDocumenter.Shared.Importing;
using VbaDocumenter.Core.Analysis;
using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Core.Parsing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

/// <summary>
/// What a choice of modules comes to, on the shape of project the feature is for: a logging interface, classes that get
/// a path in different ways through an interface, and modules that depend on each other.
/// </summary>
public class ModuleImportTests
{
  static readonly SyntheticModule[] Project =
  [
    new("ILogger", VbaModuleKind.Class, """
      Attribute VB_Name = "ILogger"
      Option Explicit
      Public Sub Write(ByVal message As String)
      End Sub
      """),
    new("FileLogger", VbaModuleKind.Class, """
      Attribute VB_Name = "FileLogger"
      Option Explicit
      Implements ILogger
      Private Sub ILogger_Write(ByVal message As String)
      End Sub
      """),
    new("IFilePath", VbaModuleKind.Class, """
      Attribute VB_Name = "IFilePath"
      Option Explicit
      Public Property Get FilePath() As String
      End Property
      """),
    new("PathFromString", VbaModuleKind.Class, """
      Attribute VB_Name = "PathFromString"
      Option Explicit
      Implements IFilePath
      Private Property Get IFilePath_FilePath() As String
      End Property
      """),
    new("PathFromDialog", VbaModuleKind.Class, """
      Attribute VB_Name = "PathFromDialog"
      Option Explicit
      Implements IFilePath
      Private m_log As ILogger
      Private Property Get IFilePath_FilePath() As String
          m_log.Write "asked"
      End Property
      """),
    new("Reports", VbaModuleKind.Standard, """
      Attribute VB_Name = "Reports"
      Option Explicit
      Public Sub Run()
          Sheet1.Range("A1").Value = ThisWorkbook.Path
          ThisWorkbook.Refresh
          UserForm1.Show
          Application.Run "Other"
          Dim thing As Object
          thing.Go
      End Sub
      """),
    new("Plain", VbaModuleKind.Standard, """
      Attribute VB_Name = "Plain"
      Option Explicit
      Public Function Where() As String
          Where = ThisWorkbook.Path
      End Function
      """),
    new("ThisWorkbook", VbaModuleKind.Document, """
      Attribute VB_Name = "ThisWorkbook"
      Attribute VB_Base = "0{00020819-0000-0000-C000-000000000046}"
      Option Explicit
      Public Sub Refresh()
      End Sub
      """),
    new("Sheet1", VbaModuleKind.Document, """
      Attribute VB_Name = "Sheet1"
      Attribute VB_Base = "0{00020820-0000-0000-C000-000000000046}"
      Option Explicit
      """),
    new("UserForm1", VbaModuleKind.Form, """
      Attribute VB_Name = "UserForm1"
      Option Explicit
      """),
  ];

  static readonly ModuleImport Import =
      new(DependencyAnalyser.Analyse(ProjectParser.Parse(WorkbookReader.Read(SyntheticWorkbook.Xlsm(Project)))));

  [Fact]
  public void A_class_brings_what_it_uses_and_the_interface_it_implements()
  {
    var plan = Import.Plan(["PathFromDialog"]);

    Assert.Equal(["PathFromDialog"], plan.Picked);
    Assert.Equal(["IFilePath", "ILogger"], plan.Brought.Select(b => b.Id));
  }

  [Fact]
  public void An_interface_brings_none_of_its_implementations_but_offers_them()
  {
    var plan = Import.Plan(["IFilePath"]);

    Assert.Empty(plan.Brought);
    Assert.Equal(["PathFromDialog", "PathFromString"], plan.Offers.Select(o => o.Id));
    Assert.All(plan.Offers, o => Assert.Equal("IFilePath", o.For));
  }

  [Fact]
  public void What_a_class_brings_can_offer_implementations_too()
  {
    // PathFromDialog brings ILogger, and an interface is no use without something that implements it.
    Assert.Equal(["PathFromString", "FileLogger"], Import.Plan(["PathFromDialog"]).Offers.Select(o => o.Id));
    Assert.Equal(["FileLogger"], Import.Plan(["ILogger"]).Offers.Select(o => o.Id));
  }

  [Fact]
  public void Document_modules_and_forms_cant_be_picked_and_say_why()
  {
    Assert.Null(Import.WhyNot("Reports"));
    Assert.Equal("A document module belongs to its workbook", Import.WhyNot("ThisWorkbook"));
    Assert.Equal("A document module belongs to its workbook", Import.WhyNot("Sheet1"));
    Assert.Equal("A UserForm can't be imported yet", Import.WhyNot("UserForm1"));
  }

  [Fact]
  public void Plain_ThisWorkbook_is_nothing_to_mention_as_it_just_means_the_target()
  {
    Assert.Empty(Import.Needs("Plain"));
    Assert.Empty(Import.Plan(["Plain"]).Brought);
  }

  [Fact]
  public void What_the_target_may_lack_or_cant_be_followed_is_listed_and_never_comes_along()
  {
    var plan = Import.Plan(["Reports"]);

    Assert.Empty(plan.Brought);
    Assert.Equal(
        [
          new ImportNeed("Application.Run", "runs a procedure by name, which can't be followed"),
          new ImportNeed("Late-bound calls", "made on an Object or Variant, so what they reach can't be followed"),
          new ImportNeed("Sheet1", "a sheet's code name, so the target needs a sheet called that"),
          new ImportNeed("ThisWorkbook.Refresh", "a procedure on the source's workbook module, which stays there"),
          new ImportNeed("UserForm1", "a UserForm, which can't come along"),
        ],
        plan.Needs.Select(n => n.Need));
    Assert.All(plan.Needs, n => Assert.Equal(["Reports"], n.UsedBy));
  }

  [Fact]
  public void A_name_is_found_whatever_its_case_and_a_missing_one_isnt()
  {
    Assert.Equal("ILogger", Import.Find("ilogger"));
    Assert.Null(Import.Find("Nope"));
  }
}
