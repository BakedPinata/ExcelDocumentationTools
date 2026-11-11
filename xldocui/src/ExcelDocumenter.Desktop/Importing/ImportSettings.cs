using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Running;
using ExcelDocumenter.Shared.Files;
using ExcelDocumenter.Shared.Importing;

namespace ExcelDocumenter.Desktop.Importing;

/// <summary>How the items to take are chosen.</summary>
internal enum ChooseFrom
{
  /// <summary>Ticked in a list read from the source: <c>--items</c>.</summary>
  List,

  /// <summary>A manifest from the page's Export tab: <c>--manifest</c>.</summary>
  Manifest,
}

/// <summary>Where the items go.</summary>
internal enum ImportDestination
{
  /// <summary>A folder, with a README on importing them by hand: <c>--bundle</c>.</summary>
  Bundle,

  /// <summary>A new workbook: <c>--new</c>.</summary>
  NewWorkbook,

  /// <summary>A copy of a target, written beside it: <c>--into</c>.</summary>
  Copy,

  /// <summary>The target itself: <c>--into</c> and <c>--in-place</c>.</summary>
  InPlace,
}

/// <summary>
/// What the Import tab is set to. Text is kept as typed; <see cref="ImportCommands"/> tidies it into arguments. It's
/// saved with the app's settings, so the tab opens as it was left.
/// </summary>
internal sealed class ImportSettings
{
  /// <summary>The workbook, or for pqdoc the folder of .m files, to take from.</summary>
  public string Source { get; set; } = "";

  /// <summary>Whose items to take, when both tools read the source.</summary>
  public Tool Tool { get; set; } = Tool.Vbadoc;

  public ChooseFrom ChooseFrom { get; set; } = ChooseFrom.List;

  /// <summary><c>--manifest</c>.</summary>
  public string Manifest { get; set; } = "";

  /// <summary><c>--items</c>: the names ticked in the list.</summary>
  public List<string> Items { get; set; } = [];

  public ImportDestination WriteTo { get; set; } = ImportDestination.Bundle;

  /// <summary><c>--bundle</c>.</summary>
  public string Bundle { get; set; } = "";

  /// <summary><c>--new</c>.</summary>
  public string NewWorkbook { get; set; } = "";

  /// <summary><c>--into</c>, for a copy of it or for the target itself.</summary>
  public string Target { get; set; } = "";

  /// <summary><c>--on-clash</c>, with a target. Prompt, the default, asks in a dialog.</summary>
  public OnClashBehaviour OnClash { get; set; } = OnClashBehaviour.Prompt;

  /// <summary><c>--on-exists</c>, for the file or bundle written. Prompt, the default, asks in a dialog.</summary>
  public OnExistsBehaviour OnExists { get; set; } = OnExistsBehaviour.Prompt;

  /// <summary><c>--verbose</c> or <c>--quiet</c>.</summary>
  public LogDetail LogDetail { get; set; }

  /// <summary>vbadoc's <c>--no-references</c>.</summary>
  public bool NoReferences { get; set; }

  /// <summary>vbadoc's <c>--drop-signature</c>, with a target.</summary>
  public bool DropSignature { get; set; }
}
