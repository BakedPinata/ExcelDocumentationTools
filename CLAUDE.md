# CLAUDE.md

Guidance for Claude Code (and anyone else) working in this repository. Each tool has its own folder, solution and
CLAUDE.md, so start in a tool's folder to work on that tool, and here for work across tools.

## Start here

1. [docs/WORKLOG.md](docs/WORKLOG.md): where things stand and how they got there.
2. [docs/GUIDELINES.md](docs/GUIDELINES.md): the conventions every tool follows.
3. The tool's own CLAUDE.md: [vbadoc](vbadoc/CLAUDE.md) or [pqdoc](pqdoc/CLAUDE.md).

## Commands

```bash
dotnet build ExcelDocumentationTools.sln -warnaserror    # every tool, from the root
dotnet test --solution ExcelDocumentationTools.sln       # every test; one project: --project <path>
pwsh ./publish.ps1                                       # publish every tool into one folder
```

`dotnet test` runs on the Microsoft Testing Platform (set in `global.json`), so it takes `--project` or `--solution`
rather than a bare path.

## Way of working

- Work one stage at a time. At the end of a stage: the build and tests pass, the tool's docs/PLAN.md (and
  DECISIONS.md if needed) and docs/WORKLOG.md are up to date, the work is committed, and you summarise what changed
  and how to check it.
- Wait for review before pushing or starting the next stage.
- Ask before changing anything in a tool's Requirements.md or DECISIONS.md, or anything that goes against them or
  the guidelines.
- Use Australian English in everything we write (see the guidelines).
