# CLAUDE.md

Guidance for Claude Code (and anyone else) working on xldocui, the desktop app over vbadoc and pqdoc. The rules shared
by every tool are in the [root CLAUDE.md](../CLAUDE.md) and [docs/GUIDELINES.md](../docs/GUIDELINES.md).

## Start here

1. [docs/PLAN.md](docs/PLAN.md): the stages, which one is current, and what each covers.
2. [docs/DECISIONS.md](docs/DECISIONS.md): decisions agreed so far. Follow them, and add new ones there.
3. [Requirements.md](Requirements.md): the specification.

## Commands

Run these from `xldocui/`.

```bash
dotnet build -warnaserror
dotnet test --project tests/ExcelDocumenter.Desktop.Tests
dotnet run --project src/ExcelDocumenter.Desktop [-- <workbook or folder>]
```

## Rules for xldocui

- **No logic of its own.** The app builds a tool's arguments and runs them through the tool's command-line library.
  Anything else a tool knows (defaults, help, what's wrong with an argument, where a page went) comes from the tool;
  if the app needs something a tool doesn't offer, add it to the tool's `.Cli` library or Core, not here.
- **A new option in a tool** needs a control on the tab, or an entry in `DocumentCommands.LeftOut` saying why not. The
  tests fail until it has one.
- Everything but the window is plain code under `Documenting/`, `Running/` and `Settings/`, tested with no window.
  `Window/` only shows it. After changing the window, look at it: start it, and take screenshots.
- Controls are made in code, with no designer, sized by their text and laid out by `TableLayoutPanel` and
  `FlowLayoutPanel`, so they're right at any screen scale. Don't give a control a fixed size.
- Windows only: the projects target `net10.0-windows`, from the root's target in `Directory.Build.props`.
