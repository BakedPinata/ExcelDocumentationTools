# CLAUDE.md

Guidance for Claude Code (and anyone else) working on pqdoc. The rules shared by every tool are in the
[root CLAUDE.md](../CLAUDE.md) and [docs/GUIDELINES.md](../docs/GUIDELINES.md).

## Start here

1. [docs/PLAN.md](docs/PLAN.md): the stages, which one is current, and what each covers.
2. [docs/DECISIONS.md](docs/DECISIONS.md): decisions agreed so far. Follow them, and add new ones there.
3. [Requirements.md](Requirements.md): the specification.

## Commands

Run these from `pqdoc/`.

```bash
dotnet build -warnaserror
dotnet test                                      # unit + browser tests (browser tests download Chromium on first run);
                                                 # also runs the PlantUML server tests, if a server is configured
dotnet test --project tests/PowerQueryDocumenter.Tests --filter "Category!=Integration"     # unit tests only
dotnet run --project src/PowerQueryDocumenter -- <workbook or folder> [--output <folder>] [-f html,csv,puml] [--verbose]
```

## Rules for pqdoc

- Everything goes in `PowerQueryDocumenter.Core`; `PowerQueryDocumenter.Cli` only parses arguments, sets up logging and
  prompts, and the console app is only its `Program.cs`. Code vbadoc needs too is in `../shared/` (see its README): change it from the repository root, so vbadoc builds and
  tests against the change.
- Adding an output format means writing an `IExporter` and registering it in `ExporterRegistry`; the analyser doesn't
  change.
- Requirement IDs (`FR-ANA-2`) appear in comments and tests, so keep them stable.
- Tests use the fixtures in `tests/PowerQueryDocumenter.Tests/Fixtures/`, temporary folders, and the stub HTTP handler
  for the PlantUML server.
- The page is built on the shell in `../shared/` (`ShellPage`, `Resources/Shell/`), as vbadoc's is: the top bar, tabs,
  theme, side panel, Load folder and source view are the shell's, and pqdoc brings its view, search box and details.
  Keep `window.pqdoc` shaped like `window.vbadoc` and change the two in step.
- Diagram text contains query names, so nothing may send it to a server the user hasn't configured.
