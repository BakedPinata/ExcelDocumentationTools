# CLAUDE.md

Guidance for Claude Code (and anyone else) working on vbadoc. The rules shared by every tool are in the
[root CLAUDE.md](../CLAUDE.md) and [docs/GUIDELINES.md](../docs/GUIDELINES.md).

## Start here

1. [docs/PLAN.md](docs/PLAN.md): the stages, which one is current, and what each covers.
2. [docs/DECISIONS.md](docs/DECISIONS.md): decisions agreed so far. Follow them, and add new ones there.
3. [Requirements.md](Requirements.md): the specification.

## Commands

Run these from `vbadoc/`.

```bash
dotnet build -warnaserror
dotnet test                              # unit + browser tests (browser tests download Chromium on first run)
dotnet test tests/VbaDocumenter.Tests    # unit tests only
dotnet run --project src/VbaDocumenter -- <workbook> [--output <folder>] [--embed-source] [--verbose]
```

## Rules for vbadoc

- Everything goes in `VbaDocumenter.Core`; the console app only parses arguments and prints.
- Use the fixtures in `tests/VbaDocumenter.Tests/Fixtures/`, and the `SyntheticWorkbook` helper for cases no real
  fixture covers.
- `private/` holds a real workbook and its output. It is git-ignored and must never be committed.
