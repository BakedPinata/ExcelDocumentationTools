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
  console.
- **The clash question** (step I4) says what's in the way and what Skip and Overwrite would each mean, as the console
  does, with its five answers: Skip, Overwrite, Skip all, Overwrite all and Quit, and without Overwrite for what can't
  be replaced. Skip is the default, as it keeps what the target has. **Changing the target itself** asks "Are you sure?"
  with No the default. Closing either is Quit, or no.
- **The questions are asked through `IAsk`:** a task dialog in the window, a stand-in in the tests, so what each answer
  means is tested with no window. The dialogs' labels are ones the console's own reader understands too.

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

## The Import tab (step I4)

- **One tool at a time.** An import takes modules and classes or queries, so the tab runs one tool's `import`: the one
  chosen, when both read the source, or the one that does.
- **The list is read by the tool that would take from it,** through `ImportSource` in its command-line library, by the
  rules `import` uses, so the list and the command can't disagree. It's read off the window's thread, half a second
  after typing stops, and again with Read again. What can't come is listed, greyed out, with the tool's reason, and
  can't be ticked.
- **Ticked names go in `--items`,** which is split at commas, so a name with a comma can only be chosen with a manifest;
  the tab says so. What was ticked for another source is let go once the new source's list is read.
- **Preview is the same command with nowhere to write,** which is how `import` already says what a choice comes to.
  It doesn't need a destination; Run does.
- **Each destination keeps its own path** (the bundle folder, the new workbook, the target), so switching between them
  doesn't lose one. `--on-clash` and `--drop-signature` only show with a target, `--on-exists` not for the target
  itself, which asks instead, and vbadoc's options only for vbadoc.
- **Left out on purpose:** `--force`, the same as Overwrite, and for the target itself answered in a dialog; and
  `--help`. `ImportCommands.LeftOut` says why, and a test checks every other option has a control.
- **A manifest dropped on the tab is the choice,** and anything else dropped is the source.
- **Nothing to open after an import,** so Open the page is only on the Document tab.

## Settings

- **In `%LOCALAPPDATA%\xldocui\settings.json`,** per person and per machine, where Windows Forms keeps its own. Written
  all at once (`AtomicFileWriter`), when a run starts and when the window closes.
- **Both tabs as they were left, which was showing, and the last ten inputs.** An input's output folders and names
  belong to it rather than to the tool, so they're kept with it and come back when it's chosen again; a new input starts
  from the defaults. An import's source joins the same list.
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
- **A dropped file is taken anywhere on the window,** since every control accepts the drop, by the tab that's showing.
- **The tabs share their rows' parts** (`TabGrid`, `TabParts`), so they look and size alike. The least width is the
  wider tab's, so changing tabs doesn't change it.
- **In a row of choices, each caption is shown before its box, and the row laid out again after:** shown together, a box
  could end up before its caption.
