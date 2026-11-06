using VbaDocumenter.Core.Extraction;
using VbaDocumenter.Core.Importing;
using VbaDocumenter.Tests.Support;

namespace VbaDocumenter.Tests.Importing;

public sealed class ReferenceChoiceTests
{
  const string ScriptingGuid = "{420B2830-E718-11CF-893D-00A0C9054228}";

  static readonly VbaReference Scripting = VbaReference.From("Scripting", $@"*\G{ScriptingGuid}#1.0#0#C:\Windows\System32\scrrun.dll#Microsoft Scripting Runtime", VbaReferenceKind.Registered);
  static readonly VbaReference Missing = VbaReference.From("Nope", @"*\G{11111111-2222-3333-4444-555555555555}#1.0#0#C:\nope.dll#Not installed", VbaReferenceKind.Registered);

  static VbaProject Project(params VbaReference[] references) => new() { Name = "VBAProject", CodePage = 1252, Modules = [], References = references };

  static bool? Installed(VbaReference reference) => reference != Missing;

  [Fact]
  public void What_the_target_lacks_and_this_machine_has_is_added()
  {
    var choice = ReferenceChoice.Make([Scripting], Project(), Installed);

    Assert.Equal([Scripting], choice.Add);
    Assert.Empty(choice.NotFound);
  }

  [Fact]
  public void What_cant_be_found_is_left_out_rather_than_stopping_the_whole_project_compiling()
  {
    var choice = ReferenceChoice.Make([Missing, Scripting], null, Installed);

    Assert.Equal([Scripting], choice.Add);
    Assert.Equal([Missing], choice.NotFound);
  }

  [Fact]
  public void What_the_target_has_is_left_alone_though_it_names_another_version()
  {
    var older = VbaReference.From("Scripting", $@"*\G{ScriptingGuid}#0.9#0#scrrun.dll#Older", VbaReferenceKind.Registered);

    var choice = ReferenceChoice.Make([Scripting], Project(older), _ => throw new InvalidOperationException("Nothing to look up"));

    Assert.Empty(choice.Add);
    Assert.Empty(choice.NameTaken);
  }

  [Fact]
  public void A_control_reference_is_matched_by_its_library_not_the_cache_each_machine_makes()
  {
    var theirs = WorkbookReader.Read(Fixtures.Workbook("suffix-generator.xlsm")).References[0];
    var ours = theirs with { Libid = @"*\G{99999999-0000-0000-0000-000000000000}#2.0#0#C:\Temp\MSForms.exd#Microsoft Forms 2.0 Object Library" };

    Assert.Empty(ReferenceChoice.Make([ours], Project(theirs), Installed).Add);
  }

  [Fact]
  public void A_different_library_of_the_same_name_is_left_out_since_two_can_have_it()
  {
    var other = VbaReference.From("Scripting", @"*\G{99999999-0000-0000-0000-000000000000}#1.0#0#other.dll#Another", VbaReferenceKind.Registered);

    var choice = ReferenceChoice.Make([Scripting], Project(other), Installed);

    Assert.Empty(choice.Add);
    Assert.Equal([Scripting], choice.NameTaken);
  }

  [Fact]
  public void What_cant_be_looked_up_is_added_and_said_to_be_unchecked()
  {
    var choice = ReferenceChoice.Make([Scripting], null, _ => null);

    Assert.Equal([Scripting], choice.Add);
    Assert.Equal([Scripting], choice.NotChecked);
  }

  [Fact]
  public void A_library_is_looked_up_by_its_GUID_alone_in_the_registry()
  {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry is Windows's");

    var stdole = VbaReference.From("stdole", @"*\G{00020430-0000-0000-C000-000000000046}#99.0#0#C:\nowhere\stdole.tlb#OLE Automation", VbaReferenceKind.Registered);

    Assert.True(ReferenceChoice.IsInstalled(stdole));
    Assert.False(ReferenceChoice.IsInstalled(Missing));
  }

  [Fact]
  public void Off_Windows_a_library_cant_be_looked_up()
  {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "It can be here");

    Assert.Null(ReferenceChoice.IsInstalled(Scripting));
  }

  [Fact]
  public void A_reference_to_another_VBA_project_is_there_if_its_file_is()
  {
    var path = Path.GetTempFileName();
    try
    {
      Assert.True(ReferenceChoice.IsInstalled(VbaReference.From("Tools", $@"*\C{path}", VbaReferenceKind.Project)));
    }
    finally
    {
      File.Delete(path);
    }

    Assert.False(ReferenceChoice.IsInstalled(VbaReference.From("Tools", $@"*\C{path}", VbaReferenceKind.Project)));
  }
}
