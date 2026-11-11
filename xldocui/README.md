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
can be pasted as it is. The Import tab chooses its source the same way.

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

**The Import tab** takes chosen modules and classes (vbadoc) or queries (pqdoc), with what they use, out of a workbook,
or out of a folder of `.m` files for queries, as `vbadoc import` and `pqdoc import` do:

| In the window | `import` |
|---|---|
| Source | the source |
| Take: modules and classes, or queries, when both tools read the source | which tool runs |
| Tick them in the list, or From a manifest | `--items`, or `--manifest` |
| Write to: a bundle folder, a new workbook, a copy of a target, or the target itself | `--bundle`, `--new`, `--into`, or `--into` and `--in-place` |
| If it's in the target: Ask, Overwrite or Skip, with a target | `--on-clash` (Ask asks in a dialog) |
| If a file exists, but for the target itself | `--on-exists` |
| Log | `--verbose`, `--quiet` |
| Leave out library references; Remove the target's signature, with a target (vbadoc) | `--no-references`; `--drop-signature` |

The list is read from the source by the tool that would take from it, each item with what it is, and greyed out with
the reason when it can't come, such as a sheet's module. What a ticked item uses comes with it. **Preview** runs the
choice with nowhere to write, which says what it comes to: what's chosen, what's brought along, and what to check in the
target. Run then writes it. A manifest dropped on the tab is taken as the choice, and anything else dropped as the source.
A name with a comma in it can only be chosen with a manifest, since `--items` is split at commas.

**The tools' questions** are dialogs: a page or file that's already there (Overwrite, Keep it, Overwrite all, Quit), an
item the target already has (what's in the way, what Skip and Overwrite would each mean, then Skip, Overwrite, Skip all,
Overwrite all, Quit, without Overwrite for what can't be replaced), and, before changing the target itself, whether
you're sure. Enter gives the answer that changes nothing, and closing a question is Quit.

**The app remembers**, for each person, both tabs as they were left and which was showing, and the last ten workbooks and
folders, each with the output folders and names last used for it. They're in `%LOCALAPPDATA%\xldocui\settings.json`. If
that file can't be read, the app starts again from the defaults.

## Project layout

```
src/ExcelDocumenter.Desktop/              the app
  Program.cs                              opens the window
  Documenting/                            the Document tab's settings, and the commands they make
  Importing/                              the Import tab's settings, the commands they make, and its list
  Asking/                                 the tools' questions, and what each answer means
  Running/                                running a tool in the app: the command, its text, the run, the log, exit codes
  Settings/                               what's remembered, and where
  Window/                                 the window, the tabs, the log pane and the dialogs
tests/ExcelDocumenter.Desktop.Tests/      tests of everything but the window, with the real tools
```

It uses each tool's command-line library (`VbaDocumenter.Cli`, `PowerQueryDocumenter.Cli`), and so their Core libraries
and `../shared/`.

## Third-party components

None of its own. Windows Forms is part of .NET (MIT). The tools' components are listed in their READMEs.

## Licence

[MIT](../LICENSE).
