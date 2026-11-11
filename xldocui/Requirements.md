# xldocui requirements

A desktop app that gives vbadoc's and pqdoc's command lines a window. Steps I1 to I4 of the
[feedback plan](../FEEDBACK-PLAN.md) set out why, and how it's built.

## 1. Scope

- Windows only, built with Windows Forms, published as one self-contained executable beside the tools.
- In addition to the command line, not in place of it. Scripts keep using the console tools.
- Out of scope: logic of its own. Anything a tool does, it does through that tool's command line.

## 2. Running the tools

| ID | Requirement |
|---|---|
| FR-RUN-1 | The app MUST run a tool by passing arguments to the tool's command-line library (`CliRunner`), in its own process, so parsing, checks, messages and exit codes are the tool's. |
| FR-RUN-2 | The window MUST show the command each run will make, as it would be typed, with a Copy button. What's shown, what's copied and what runs MUST be the same arguments. |
| FR-RUN-3 | A run MUST happen off the window's thread, with a Cancel that stops it as Ctrl+C does (exit code 130). |
| FR-RUN-4 | The tools' log MUST show in a pane in the window, with the console's wording, and how each run ended MUST be said in words with its exit code. |
| FR-RUN-5 | The tools' questions MUST be asked in dialogs, with the same answers as the console's prompts. The tools MUST see someone to ask, so they don't stop with exit code 4. Quit MUST stop the rest of the run. |
| FR-RUN-6 | The app MUST NOT pass `--help` or `--version`, which describe the program that's running. It MUST show each tool's version instead. |
| FR-RUN-7 | Problems a tool's command line finds in the arguments MUST be shown before running, in its words, and MUST stop Run until they're put right. |

## 3. The Document tab

| ID | Requirement |
|---|---|
| FR-DOC-1 | The tab MUST set every option of both tools' main commands, except those it lists as left out on purpose, with the reason. A test MUST check both against the tools' command lines, so a new option can't be forgotten. |
| FR-DOC-2 | A workbook or folder MUST be choosable by Browse… or Folder…, from recent ones, by dropping it on the window, by typing or pasting it (with or without quotes), or on the app's command line. |
| FR-DOC-3 | Only what applies MUST be shown: a tool that doesn't read the input is offered greyed out with the reason; a tool's options only while it's ticked; the PlantUML server only when a format needs it. |
| FR-DOC-4 | A field left empty MUST use the tool's default, and show it. |
| FR-DOC-5 | A run MUST take vbadoc and then pqdoc, for each one ticked that reads the input. |
| FR-DOC-6 | Each page MUST be opened once it's written, unless that's turned off. The tools MUST say where they put it. |
| FR-DOC-7 | Only what differs from a tool's default MUST be written in its command, with long option names. |

## 4. Settings

| ID | Requirement |
|---|---|
| FR-SET-1 | The app MUST remember, for each person, the tab as it was left, whether to open pages, and the last ten workbooks and folders, each with the output folders and names last used for it. |
| FR-SET-2 | Settings that can't be read MUST NOT stop the app: it starts again from the defaults. |

## 5. Platform

| ID | Requirement |
|---|---|
| NFR-1 | It MUST build, test and publish from the `dotnet` command line, with no designer and no IDE. |
| NFR-2 | It MUST publish as one compressed, self-contained file with the tools' publish settings, unpacking nothing when it runs. |
| NFR-3 | Its tests MUST run with no window. |
