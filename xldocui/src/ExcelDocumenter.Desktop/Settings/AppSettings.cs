using ExcelDocumenter.Desktop.Documenting;
using ExcelDocumenter.Desktop.Importing;

namespace ExcelDocumenter.Desktop.Settings;

/// <summary>A workbook or folder used before, with the outputs it was given, which belong to it rather than to the tool.</summary>
internal sealed class RecentInput
{
  public string Input { get; set; } = "";

  public string VbadocOutput { get; set; } = "";

  public string VbadocOutputName { get; set; } = "";

  public string PqdocOutput { get; set; } = "";

  public string PqdocOutputName { get; set; } = "";
}

/// <summary>What the app remembers for the person using it, between runs of the app.</summary>
internal sealed class AppSettings
{
  /// <summary>How many recent workbooks and folders are kept.</summary>
  public const int RecentCount = 10;

  /// <summary>The Document tab as it was last left.</summary>
  public DocumentSettings Document { get; set; } = new();

  /// <summary>The Import tab as it was last left.</summary>
  public ImportSettings Import { get; set; } = new();

  /// <summary>Whether to open each page once it's written.</summary>
  public bool OpenPages { get; set; } = true;

  /// <summary>Recent workbooks and folders, the most recent first.</summary>
  public List<RecentInput> Recent { get; set; } = [];

  /// <summary>Puts the document's input at the top of the recent list, with the outputs it has now.</summary>
  public void Remember(DocumentSettings document)
  {
    ArgumentNullException.ThrowIfNull(document);
    var input = DocumentCommands.Tidy(document.Input);
    if (input.Length == 0)
    {
      return;
    }

    Recent.RemoveAll(recent => SameInput(recent.Input, input));
    Recent.Insert(0, new RecentInput
    {
      Input = input,
      VbadocOutput = document.Vbadoc.Output,
      VbadocOutputName = document.Vbadoc.OutputName,
      PqdocOutput = document.Pqdoc.Output,
      PqdocOutputName = document.Pqdoc.OutputName,
    });
    if (Recent.Count > RecentCount)
    {
      Recent.RemoveRange(RecentCount, Recent.Count - RecentCount);
    }
  }

  /// <summary>Puts an import's source at the top of the recent list, keeping the outputs it had if it's there already.</summary>
  public void RememberSource(string source)
  {
    ArgumentNullException.ThrowIfNull(source);
    var tidied = DocumentCommands.Tidy(source);
    if (tidied.Length == 0)
    {
      return;
    }

    var known = Recent.FirstOrDefault(recent => SameInput(recent.Input, tidied)) ?? new RecentInput { Input = tidied };
    Recent.Remove(known);
    Recent.Insert(0, known);
    if (Recent.Count > RecentCount)
    {
      Recent.RemoveRange(RecentCount, Recent.Count - RecentCount);
    }
  }

  /// <summary>
  /// Makes an input the document's, with the outputs last used for it, or the tools' defaults if it's new. The outputs
  /// of the input before it don't carry over, since they were for that workbook.
  /// </summary>
  public void Choose(string input)
  {
    ArgumentNullException.ThrowIfNull(input);
    var recent = Recent.FirstOrDefault(r => SameInput(r.Input, DocumentCommands.Tidy(input)));
    Document.Input = input;
    Document.Vbadoc.Output = recent?.VbadocOutput ?? "";
    Document.Vbadoc.OutputName = recent?.VbadocOutputName ?? "";
    Document.Pqdoc.Output = recent?.PqdocOutput ?? "";
    Document.Pqdoc.OutputName = recent?.PqdocOutputName ?? "";
  }

  /// <summary>Fills in anything a hand-edited file left out, so the rest of the app can rely on it.</summary>
  public AppSettings Normalise()
  {
    Document ??= new DocumentSettings();
    Document.Input ??= "";
    Document.Vbadoc ??= new VbadocSettings();
    Document.Pqdoc ??= new PqdocSettings();
    Document.Pqdoc.Formats ??= [];
    Import ??= new ImportSettings();
    Import.Source ??= "";
    Import.Manifest ??= "";
    Import.Bundle ??= "";
    Import.NewWorkbook ??= "";
    Import.Target ??= "";
    Import.Items = (Import.Items ?? []).Where(item => !string.IsNullOrEmpty(item)).ToList();
    Recent = (Recent ?? []).Where(recent => recent is { Input.Length: > 0 }).Take(RecentCount).ToList();
    return this;
  }

  // Windows paths are compared without case.
  private static bool SameInput(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
