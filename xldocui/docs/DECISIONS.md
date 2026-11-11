# Design decisions

Decisions made while building the app that aren't spelled out in [Requirements.md](../Requirements.md), or that change
it. Add to this file whenever a new decision is agreed. Conventions shared with the tools are in
[GUIDELINES.md](../../docs/GUIDELINES.md).

## Platform

- **Windows Forms** (settled when H4 was reviewed): it comes with .NET under MIT, so it adds no package, and it needs no
  designer, which matters because the work machine can only build from the command line. Windows only, which is the
  only platform the tools are used on.
- **The name is `xldocui`,** in a folder of that name, with `ExcelDocumenter.Desktop` for the code. `xldoc` is kept for
  the combined tool in the feedback plan's Later section.
- **The target is the root's with `-windows` added,** in `xldocui/Directory.Build.props`, so moving .NET is still one
  change. Only this folder targets Windows.
- **The first version is 0.1.0.**
- **The build stamp follows `vbadoc/` and `pqdoc/` too** (`ToolStampPaths`), since the app is built from them.
- **Published compressed** (the user's choice, 2026-11-11): about 50 MB rather than 111 MB, for about 0.1 s more at
  start-up, which isn't noticeable. The spike found nothing is unpacked either way. Otherwise the publish profile is
  the tools'.

## Running the tools

- **In the app's own process, through each tool's `.Cli` library** (step I2), not by starting `vbadoc.exe`: it keeps the
  app one file, and a child process with its streams redirected isn't interactive, so it couldn't ask its questions.
- **The tools say where their page went** through `PageWritten` in their services, so the app doesn't work the path out
  itself. A page that's skipped isn't reported, so it isn't opened.
- **The log uses the console's templates**, so a line reads the same in the window as at a prompt. Text a tool writes
  outside its log, such as usage after an invalid argument, comes into the log too.
- **A run takes vbadoc, then pqdoc.** One that fails doesn't stop the other, since they're independent; Cancel and Quit
  do, as both mean stop.
- **Closing the window during a run** cancels it, as Ctrl+C would, and closes once it's stopped.
- **The overwrite question** is a task dialog with the console's four answers: Overwrite, Keep it, Overwrite all and
  Quit. Keep it is the default, so Enter changes nothing, and closing the dialog is Quit, as end of input is at the
  console. The clash question and `--in-place`'s comes with the Import tab (I4).

## The Document tab

- **One input, both tools.** A tool is offered when it reads the input, which follows what each tool's help says it
  reads, by extension, since the tools tell a workbook by what's in it. A test keeps the two in step. A folder, or a
  name with no extension that isn't a file, is pqdoc's.
- **Each tool has its own output folder and name,** since their defaults differ (a `VBAProject` folder and a
  `PowerQuery` folder beside the workbook).
- **`--embed-source`, `--on-exists`, `--verbose` and `--quiet` are set once for both,** since they mean the same to
  each.
- **Only what differs from a default is written,** with long option names, so the command is the one a person would
  type. Formats are listed in the order pqdoc's help gives them. Arguments are quoted only when they need to be, by the rules a .NET
  program reads its arguments by, and a comma is quoted for PowerShell.
- **Left out on purpose:** `--force`, the same as Overwrite; `--help` and `--version`, which describe the program
  that's running. `DocumentCommands.LeftOut` says why, and a test checks every other option has a control.
- **What's wrong is found by the tool's own command line,** by parsing the arguments with its `CliDefinition` as they
  change. Problems that need the files, such as a workbook that isn't there, are found by running.
- **Defaults and tooltips come from the tools:** the default names from `HtmlPageWriter.DefaultName` and
  `AnalyserOptions.DefaultOutputName`, the default folders from each option's `[default: …]`, and the tooltips from the
  options' help.

## Settings

- **In `%LOCALAPPDATA%\xldocui\settings.json`,** per person and per machine, where Windows Forms keeps its own. Written
  all at once (`AtomicFileWriter`), when a run starts and when the window closes.
- **The tab as it was left, and the last ten inputs.** An input's output folders and names belong to it rather than to
  the tool, so they're kept with it and come back when it's chosen again; a new input starts from the defaults.
- **Settings that can't be read start again** from the defaults, rather than stopping the app.

## The window

- **Made in code, with no designer,** laid out by `TableLayoutPanel` and `FlowLayoutPanel`. Buttons are sized by their
  text: Windows Forms already scales a new control's default size for the screen, and the form scales it again, so a
  button keeping its default size came out twice as tall.
- **The tab is as tall as what it shows,** and the log has the rest; the split between them can be dragged.
- **The window can't be made smaller than what it shows** (found on a work machine, after I3): its least size is worked
  out from what's showing, at the screen's scale, whenever that changes. A field asks for its least width rather than
  the width of its text, so a long path doesn't widen the window. If Windows makes the window smaller anyway, as
  snapping it to half of a small screen can, the top scrolls rather than cutting off what's on the right.
- **A dropped file is taken anywhere on the window,** since every control accepts the drop.
