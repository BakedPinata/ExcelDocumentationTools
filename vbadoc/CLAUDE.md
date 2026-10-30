# CLAUDE.md

Guidance for Claude Code (and anyone else) working in this repository.

## Start here

1. [docs/PLAN.md](docs/PLAN.md): the stages, which one is current, and what each covers.
2. [docs/DECISIONS.md](docs/DECISIONS.md): decisions agreed so far. Follow them, and add new ones there.
3. [Requirements.md](Requirements.md): the specification.

## Commands

```bash
dotnet build -warnaserror
dotnet test                              # unit + browser tests (browser tests download Chromium on first run)
dotnet test tests/VbaDocumenter.Tests    # unit tests only
dotnet run --project src/VbaDocumenter -- <workbook> [--out <folder>] [--embed-source] [--verbose]
```

## Way of working

- Work one stage at a time. At the end of a stage: tests pass, update docs/PLAN.md (and DECISIONS.md if needed),
  commit, then summarise what changed and how to try it. Wait for review before pushing or starting the next stage.
- Ask before changing anything in Requirements.md or DECISIONS.md, or anything that goes against them.
- Write clear, conventional C# rather than clever code. Briefly explain non-obvious design decisions in comments.
- Everything goes in `VbaDocumenter.Core`; the console app only parses arguments and prints.
- Tests use xUnit. Use the fixtures in `tests/VbaDocumenter.Tests/Fixtures/`, and the `SyntheticWorkbook` helper for
  cases no real fixture covers.
- The generated HTML must never load anything from the network.
- Check page changes in the browser tests, and look at screenshots too: tests don't catch layout or colour problems.
