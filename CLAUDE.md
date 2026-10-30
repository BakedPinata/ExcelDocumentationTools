# CLAUDE.md

Guidance for Claude Code (and anyone else) working in this repository. Each tool has its own folder, solution and
CLAUDE.md, so start in a tool's folder to work on that tool, and here for work across tools.

## Start here

1. [docs/GUIDELINES.md](docs/GUIDELINES.md): the conventions every tool follows.
2. The tool's own CLAUDE.md: [vbadoc](vbadoc/CLAUDE.md) or [pqdoc](pqdoc/CLAUDE.md).
3. [MONOREPO-PLAN.md](MONOREPO-PLAN.md): the plan for aligning the tools, while it is still in progress.

## Commands

```bash
dotnet build ExcelDocumentationTools.sln -warnaserror    # every tool, from the root
```

## Way of working

- Work one stage at a time. At the end of a stage: the build and tests pass, the tool's docs/PLAN.md (and
  DECISIONS.md if needed) are up to date, the work is committed, and you summarise what changed and how to check it.
- Wait for review before pushing or starting the next stage.
- Ask before changing anything in a tool's Requirements.md or DECISIONS.md, or anything that goes against them or
  the guidelines.
- Use Australian English in everything we write (see the guidelines).
