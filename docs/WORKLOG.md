# Work log

Where the repository stands and how it got here. A new session reads this first. Keep it short and link to the detail
rather than repeating it.

- **Detail lives in:** [GUIDELINES.md](GUIDELINES.md) for conventions, each tool's `docs/PLAN.md` for stages, each
  tool's `docs/DECISIONS.md` for design decisions, and git history for the rest.
- **Updating it:** at the end of each stage, and after any session that changes direction, update *Current state* and
  add an entry at the top of the log. Give the date, what changed and why, and commits, in a few lines.

## Current state (2026-11-03)

- **vbadoc 0.7.0:** stages 0–5e are done, and the class diagram is drawn with d3 (no Mermaid). Stage 6 (UserForms) is
  parked; stage 7 waits for the feedback plan.
- **pqdoc 1.1.0**, plus stages 3b–3d: reads a folder of hand-exported `.m` files, and writes HTML by default.
- **`FEEDBACK-PLAN.md`** (root): the current plan, agreed on 2026-11-03, steps A0–H4. A0 and A1 are pushed; the
  user's build, test and publish of A0 on the work machine is still to come. A0–A3 are pushed. F1 is done, waiting
  for review. B1 (sample workbooks with Power Query, made by the user in Excel) is next.
- **`USER-FEEDBACK-NOTES.md`** (root, untracked): the user's feedback from trying both tools on real workbooks, and the
  source of the feedback plan. It holds details about the user's workplace, so keep them out of the repository.
  **Never commit it.**
- **.NET 10** since step A0. `dotnet test` runs on the Microsoft Testing Platform, so it takes `--project` or
  `--solution`, not a bare path.

## Log

Newest first.

### 2026-11-03: Step F1, class diagram drawn with d3

- `classes.js` rewritten on d3: UML boxes sized to their text, a layered layout worked out before drawing (users above
  what they use, cycles sharing a layer, wide layers wrapping, loose modules in a grid), draggable boxes remembered per
  workbook and Show… choice until Re-run layout, zoom and pan like the graph. Mermaid removed. vbadoc 0.7.0.
- The page went from 4.3 MB to 0.8 MB on the private workbook. Writing it now takes 1–2 ms, and the antivirus scan a
  browser waits for takes 0.1–0.2 s instead of about 3 s.
- vbadoc's requirements (class diagram section, theme, vendored scripts) and decisions (stage 5) describe the d3
  version. Browser tests rewritten, with new ones for dragging and the layout.

### 2026-11-03: Step A3, pqdoc writes HTML by default

- `--format` now lists exactly what to write (`html`, `csv`, `puml`, `pdf`, `png`, `svg`) and defaults to `html`;
  `--no-csv` is gone. pqdoc 1.1.0. Its requirements (options, FR-CSV-3, new FR-HTML-10, acceptance criteria 1–3) and
  tests follow.

### 2026-11-03: Step A2, export speed

- `--verbose` now shows how long each step took. vbadoc 0.6.3.
- The pause was Windows Defender scanning the 4.3 MB page whenever a run opened the previous one, taking 2–3 s; each
  step otherwise takes 80–180 ms. Mermaid is 3.6 MB of the page, and without it the scan took 0.2 s, so removing it
  (F1) is the fix. Writing differently and ReadyToRun were measured and not kept. Details are in vbadoc's decisions.
- The user agreed to move F1 up to straight after A3, since it fixes both the export pause and slow page opening.

### 2026-11-03: Importing added to the plan

- The user wants chosen modules and queries importable into a new or existing workbook, with their dependencies.
  Added as phase H, last: a spike on writing workbooks (H1), choosing items with their dependencies and writing bundle
  folders (H2), then writing workbooks directly (H3, H4) only if the spike shows it's reliable. The workbook read is
  never changed, and Excel's automation isn't used.

### 2026-11-03: Step A1, graph clicks leave the layout alone

- vbadoc's graph pinned nodes and woke the simulation as soon as the mouse button went down, so every click on a
  module outline pulled the modules together until the graph came to rest. A click with "Keep dragged nodes where
  dropped" on also pinned the node. Both now wait until the pointer has moved past the click distance, as pqdoc's
  graph already did. vbadoc 0.6.2.
- Two browser tests, which failed before the fix.

### 2026-11-03: Step A0, .NET 10

- Moved every project to .NET 10 (`Directory.Build.props`, `global.json`), a week before .NET 9's support ended.
  vbadoc 0.6.1, pqdoc 1.0.1.
- The .NET 10 SDK refuses to run xUnit v3 4.x projects through VSTest, so `global.json` switches `dotnet test` to the
  Microsoft Testing Platform. Commands changed from a bare path to `--project`/`--solution`, in the build workflow,
  every CLAUDE.md, the READMEs and vbadoc's decisions.
- The root README now covers building, testing, running and publishing from the command line, because the work machine
  only has Visual Studio 2022, which can't build .NET 10.
- Checked: clean build; all 723 tests (6 skipped as before); published executables run; output from every fixture and
  the private workbook is byte-identical to .NET 9's apart from the build stamp (and pqdoc's generation time).

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
