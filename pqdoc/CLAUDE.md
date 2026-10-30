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
dotnet test tests/PowerQueryDocumenter.Tests --filter "Category!=Integration"     # unit tests only
dotnet run --project src/PowerQueryDocumenter -- <input_folder> [--output <folder>] [-f puml,html] [--verbose]
```

## Rules for pqdoc

- Everything goes in `PowerQueryDocumenter.Core`; the console app only parses arguments, sets up logging and prompts.
- Adding an output format means writing an `IExporter` and registering it in `ExporterRegistry`; the analyser doesn't
  change.
- Requirement IDs (`FR-ANA-2`) appear in comments and tests, so keep them stable.
- Tests use the fixtures in `tests/PowerQueryDocumenter.Tests/Fixtures/`, temporary folders, and the stub HTTP handler
  for the PlantUML server.
- The page is built like vbadoc's, with the same structure and names, so a later combined page can host both tools'
  views. Keep `window.pqdoc` shaped like `window.vbadoc` and change the two in step.
- Diagram text contains query names, so nothing may send it to a server the user hasn't configured.
