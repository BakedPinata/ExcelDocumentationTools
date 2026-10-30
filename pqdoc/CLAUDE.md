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
dotnet test                                      # also runs the PlantUML server tests, if a server is configured
dotnet test --filter "Category!=Integration"     # unit tests only
dotnet run --project src/PowerQueryDocumenter -- <input_folder> [--output-folder <folder>] [-f puml,html] [--verbose]
```

## Rules for pqdoc

- The console entry point stays thin. Until stage 3c everything is one project, with a folder per area.
- Adding an output format means writing an `IExporter` and registering it in `ExporterRegistry`; the analyser doesn't
  change.
- Requirement IDs (`FR-ANA-2`) appear in comments and tests, so keep them stable.
- Tests use the fixtures in `tests/PowerQueryDocumenter.Tests/Fixtures/`, temporary folders, and the stub HTTP handler
  for the PlantUML server.
- Diagram text contains query names, so nothing may send it to a server the user hasn't configured.
