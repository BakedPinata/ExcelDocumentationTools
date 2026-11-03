# Work log

Where the repository stands and how it got here. A new session reads this first. Keep it short and link to the detail
rather than repeating it.

- **Detail lives in:** [GUIDELINES.md](GUIDELINES.md) for conventions, each tool's `docs/PLAN.md` for stages, each
  tool's `docs/DECISIONS.md` for design decisions, and git history for the rest.
- **Updating it:** at the end of each stage, and after any session that changes direction, update *Current state* and
  add an entry at the top of the log. Give the date, what changed and why, and commits, in a few lines.

## Current state (2026-11-03)

- **vbadoc 0.6.0:** stages 0–5e are done. Stage 6 (UserForms) is parked; stage 7 waits for the feedback plan.
- **pqdoc 1.0.0**, plus stages 3b–3d: reads a folder of hand-exported `.m` files.
- **`FEEDBACK-PLAN.md`** (root): the current plan, agreed on 2026-11-03, steps A0–G4. Step A0 (.NET 10) is next.
- **`USER-FEEDBACK-NOTES.md`** (root, untracked): the user's feedback from trying both tools on real workbooks, and the
  source of the feedback plan. It holds details about the user's workplace, so keep them out of the repository.
  **Never commit it.**
- **.NET 9** support ends 2026-11-10. Moving to .NET 10 waits until the work machine allows it.

## Log

Newest first.

### 2026-11-03: Feedback gathered, plan revised, work log started

- Reorganised `USER-FEEDBACK-NOTES.md`, and the user answered its open questions.
- What it changed: the workbooks in use rarely hold both VBA and Power Query and never link them, and UserForms are
  seldom used. What matters is finding the execution path for one activity, cutting the clutter from items used
  everywhere, and leaving documentation for later developers and readers.
- Replaced `COMBINED-TOOL-PLAN.md` with `FEEDBACK-PLAN.md`. It keeps pqdoc reading workbooks and the shared library and
  page shell. It adds fixes, an explorer, favourites, hiding, references to exact lines, a Calls tab, a d3 class
  diagram, saved views and printing. One executable, the combined page and VBA-to-query links move to its Later section.
- The user agreed the plan's recommendations, and added step A0 to move to .NET 10, since the work machine now has
  its SDK. That machine only has Visual Studio 2022, which can't build .NET 10, so the command line must cover
  everything.
- vbadoc's stage 6 (UserForms) is parked in its plan's Later section; pqdoc's Later section now points to the plan.
- Started this log, back-filled from git history and the plans.

### 2026-10-31: Combined tool plan drafted

- `COMBINED-TOOL-PLAN.md`, steps 4a–4i: pqdoc reads Power Query straight from a workbook, writes `.m` files and shows
  their source; then a `shared/` library, a shared page shell, one executable (`xldoc`, name not settled) with `vba`
  and `pq` subcommands, a combined page, and links from VBA to the queries it refreshes.
- The `DataMashup` format (MS-QDEFF) was checked against the private workbook. It has only one query, so step 4a needs
  sample workbooks made in Excel.
- vbadoc's stages 6–7 wait for this plan, because stage 6 changes the source panel that the shell step moves.
- Fixed a click moving vbadoc's splitter (`700d2cd`). The same bug in two pages is part of the case for one shell.

### 2026-10-30: One repository for both tools

Carried out `MONOREPO-PLAN.md` (steps 1–3e), then deleted it. It's in history.

- The GitHub repository `vbadocumenter` became `ExcelDocumentationTools`, and vbadoc moved into `vbadoc/`. pqdoc came
  from PowerQueryDependencyAnalyser, first built in a Claude chat, and its first version was taken as 1.0.0.
- Naming: the short name is the product (`vbadoc`, `pqdoc`) and the long name is the code (`VbaDocumenter`,
  `PowerQueryDocumenter`).
- Shared at the root: editor, git and build settings, Central Package Management, and .NET 9 (the user's choice despite
  its end of support). C# re-indented to 2 spaces. Australian English throughout.
- Wrote `docs/GUIDELINES.md`, and gave each tool a README, Requirements, CLAUDE.md, PLAN and DECISIONS.
- Aligned the tools: xUnit v3; System.CommandLine, Serilog, the shared exit codes and `--output`; pqdoc split into Core
  and a console app; pqdoc's page rebuilt on vbadoc's design, with browser tests; a build stamp per tool.
- No shared code project yet, because copying the overlap is cheaper than coupling the tools. Revisit once both read
  workbooks.
- Added `publish.ps1` (every tool into one folder), a GitHub build workflow and Dependabot, and merged its first
  updates (#10–#13).

### 2026-10-26 to 2026-10-29: vbadoc built

- Stages 0–5: reading VBA from workbooks (MS-OVBA) and exporting it, a light parser, the dependency model, a
  self-contained HTML page with a source panel, a d3 dependency graph and a Mermaid class diagram. The graph was
  reworked after testing on a real workbook.
- Stages 5b–5d: graph legend and Show… options, a Uses / Used by list, then Home, the trail, Path from Home and browser
  Back/Forward. The graph also gained an all / dependencies / dependents toggle.
