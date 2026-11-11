# xldocui: a window for vbadoc and pqdoc

A Windows desktop app that runs [vbadoc](../vbadoc/README.md) and [pqdoc](../pqdoc/README.md) without typing their
options. Choose a workbook, tick what you want, and press Run. The window shows the command it's about to run, with a
Copy button, so anything set up there can go in a script. The command line stays as it is, and stays the way to script
the tools.

The app holds no logic of its own. It builds each tool's arguments and runs that tool's own command line inside the
app, so parsing, checks, messages and exit codes are the tool's, and the window and the console can't disagree. The
tools' questions, such as whether to overwrite a page, are asked in dialogs.

## Requirements

- Windows. The app is built with Windows Forms, which comes with .NET.
- To build it: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), from the command line like the
  tools (see the [root README](../README.md#building-and-testing)). Publishing it also needs the Windows desktop
  runtime pack (`Microsoft.WindowsDesktop.App.Runtime.win-x64`), which `dotnet publish` gets from NuGet.

## Build and run

From the `xldocui` folder:

```
dotnet build
dotnet test --project tests/ExcelDocumenter.Desktop.Tests
dotnet run --project src/ExcelDocumenter.Desktop [-- <workbook or folder>]
```

To publish it as a single self-contained executable, into the folder shared by every tool:

```
dotnet publish src/ExcelDocumenter.Desktop -c Release -p:PublishProfile=FolderProfile
```

`publish.ps1` at the root does this along with the tools. `xldocui.exe` is one file of about 50 MB: it's compressed,
which halves its size for about a tenth of a second at start-up. Nothing is unpacked when it runs.

## Using it

```
xldocui [<workbook or folder>]
```

**Choose what to document** with Browse… (a workbook) or Folder… (a folder of `.m` files, for Power Query), from the
recent ones in the list, by dropping it on the window, or by naming it when starting the app. Explorer's "Copy as path"
can be pasted as it is.

**The Document tab** has every option of both tools' main commands:

| In the window | vbadoc | pqdoc |
|---|---|---|
| Put the source in the page | `--embed-source` | `--embed-source` |
| If a file exists: Ask, Overwrite, Skip or Stop | `--on-exists` (Ask is the default, and asks in a dialog) | `--on-exists` |
| Log: Normal, Verbose or Quiet | `--verbose`, `--quiet` | `--verbose`, `--quiet` |
| Output folder | `--output` | `--output` |
| Page name, File names | `--output-name` | `--output-name` |
| Formats | | `--format` |
| PlantUML server, shown when a format needs one | | `--plantuml-server` |

Each option's tooltip is the tool's own help. Left empty, a field uses the tool's default, which it shows. Only what
applies is shown: a tool that doesn't read the chosen file is greyed out, with the reason (vbadoc doesn't read an `.xlsx`,
and pqdoc doesn't read an `.xls`); a tool's own options only show while it's ticked; and the PlantUML server only shows
when PDF, PNG or SVG is ticked. `--force` is left out, since it's the same as Overwrite, and so are `--help` and
`--version`, which describe whichever program is running. The status bar shows each tool's version instead.

**The command** shows what Run will do, one line for each tool, with only what differs from the defaults. Copy puts it
on the clipboard, ready for a Command Prompt or PowerShell. Anything the tool's own command line would reject, such as a
`#` in a page name, is shown below it in the tool's words, and Run waits until it's put right.

**Run** runs vbadoc and then pqdoc. The log shows what each tool says, as it would at a prompt, and how each ended, in
words and with its exit code. Cancel stops a run as Ctrl+C does, and Quit in a question stops the rest of the run too.
Each page is opened once it's written, unless that's turned off, and Open the page opens it again.

**The app remembers**, for each person, the tab as it was left, and the last ten workbooks and folders, each with the
output folders and names last used for it. They're in `%LOCALAPPDATA%\xldocui\settings.json`. If that file can't be
read, the app starts again from the defaults.

Importing comes next, in its own tab: step I4 of the [feedback plan](../FEEDBACK-PLAN.md).

## Project layout

```
src/ExcelDocumenter.Desktop/              the app
  Program.cs                              opens the window
  Documenting/                            the Document tab's settings, and the commands they make
  Running/                                running a tool in the app: the command, its text, the run, the log, exit codes
  Settings/                               what's remembered, and where
  Window/                                 the window, the Document tab, the log pane and the dialogs
tests/ExcelDocumenter.Desktop.Tests/      tests of everything but the window, with the real tools
```

It uses each tool's command-line library (`VbaDocumenter.Cli`, `PowerQueryDocumenter.Cli`), and so their Core libraries
and `../shared/`.

## Third-party components

None of its own. Windows Forms is part of .NET (MIT). The tools' components are listed in their READMEs.

## Licence

[MIT](../LICENSE).
