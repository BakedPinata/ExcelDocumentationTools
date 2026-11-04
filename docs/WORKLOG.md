# Work log

Where the repository stands and how it got here. A new session reads this first. Keep it short and link to the detail
rather than repeating it.

- **Detail lives in:** [GUIDELINES.md](GUIDELINES.md) for conventions, each tool's `docs/PLAN.md` for stages, each
  tool's `docs/DECISIONS.md` for design decisions, and git history for the rest.
- **Updating it:** at the end of each stage, and after any session that changes direction, update *Current state* and
  add an entry at the top of the log. Give the date, what changed and why, and commits, in a few lines.

## Current state (2026-11-04)

- **vbadoc 0.16.0:** stages 0–5e are done, the class diagram is drawn with d3 (no Mermaid), and the page is
  `<workbook>-vba.html`, with an explorer of modules and members, in `'@Folder` folders or by kind. It asks
  before overwriting a page that would change (`--on-exists`, `--force`), as pqdoc does. Stage 6 (UserForms) is parked;
  stage 7 waits for the feedback plan.
- **pqdoc 1.9.1**, plus stages 3b–3d: reads a workbook's queries directly, or a folder of hand-exported `.m` files,
  and writes HTML by default as `<workbook or folder>-powerquery.html`. From a workbook it also writes each query to a
  `.m` file, kept in step with the workbook. The page shows each query's M code, highlighted, and an explorer of the
  queries in their Excel groups, nested as Excel shows them, or as queries and functions.
- **`shared/`** (`ExcelDocumenter.Shared`): the code both tools use, since step C1, and the page shell both pages are
  built on, since C2. See its README.
- **`FEEDBACK-PLAN.md`** (root): the current plan, agreed on 2026-11-03, steps A0–H4. The user's build, test and
  publish of A0 on the work machine is still to come. A0–A5, F1, B1–B4, C1 and C2 are pushed and reviewed. D1 is done.
  The user's review of D1 added steps D1a to D1d, before D2, now settled (see the plan's "After D1's review"). D1b is
  pushed; the user's check of the workbook with 20 groups is still to come. D1a (with the copy button the user asked
  for after D1b) is pushed. D1e (`'@Folder` after a label, asked for after D1a) and D1c (view controls and both pages'
  summary) are pushed. The user's review of D1c added the explorer's filter (checkboxes, apart from selecting) and
  Expand / Collapse all to D3, which moved widely used items to a new D4. D1d (speed) is pushed; the user found it a
  little better on the production workbook, good enough for now. A6 (a name declared again under `#If` is one member)
  is done, waiting for review. **Next: D2** (favourites).
- **`USER-FEEDBACK-NOTES.md`** (root, untracked): the user's feedback from trying both tools on real workbooks, and the
  source of the feedback plan. It holds details about the user's workplace, so keep them out of the repository.
  **Never commit it.**
- **.NET 10** since step A0. `dotnet test` runs on the Microsoft Testing Platform, so it takes `--project` or
  `--solution`, not a bare path.

## Log

Newest first.

### 2026-11-04: Step A6, declarations repeated under `#If`

- The user writes early and late binding as `#If EarlyBinding Then` / `As Scripting.Dictionary` / `#Else` /
  `As Object`, and vbadoc showed each such variable and function twice; a function's first header even lost its body.
  They chose to fold every version into one member (option 1) over evaluating the branches.
- The parser tracks `#If` blocks and folds a name declared again with the same kind into the first declaration, which
  spans every version and keeps their types and parameter lists (`Member.AlternativeTypes`, `AlternativeParameters`).
  Dependencies and type lookups use every version; the signature and class diagram show `A | B`. Enums, Types and
  repeats outside `#If` are left as before. vbadoc 0.16.0.
- Tests: 7 parser tests from the user's example and other forms, 2 resolver tests and 1 class diagram test; two tests
  of the sample's repeated Declare now expect one member. The private workbook has no such repeats; its counts are
  unchanged.

### 2026-11-04: D1d reviewed

- The user found the production workbook a little better after D1d, good enough for now, and chose to leave a pinned
  node's release at Ctrl+double-click while focus on click is on.
- They found that a variable or procedure declared in both branches of `#If … #Else` (early or late binding) shows as
  two members. A fix is proposed, waiting for the user's choice.

### 2026-11-04: Step D1d, speed on large projects

- Measured on a generated project shaped like the user's (70 modules, about 320 module and 1,540 member dependencies),
  CPU slowed four times. Hovering at member level spent about 400 ms per hover laying out the whole graph, because
  showing or hiding the tooltip by display did; it's now hidden by visibility, in both tools (10 s of long tasks for
  25 hovers became under 0.2 s).
- The whole graph's layout is kept when a focus or the other level replaces it, and put back when it returns, pinned
  nodes included: clearing a focus went from 4.3 s to 1.6 s there. Zooming at member level is left alone: its cost is
  painting the names, which stay. vbadoc 0.15.0, pqdoc 1.9.1.
- `SpeedTests` keep a generous hover budget on that project and check the tooltip's style and that layouts come back
  unchanged; pqdoc checks its tooltip's style.
- Found on the way: with focus on click on, a double-click's first click focuses and redraws the graph, so a pinned
  node isn't released by it; Ctrl+double-click (tested) or Release pinned does it. Raised with the user.

### 2026-11-04: D1c reviewed; the explorer's filter planned in D3

- The user asked for Expand all and Collapse all in the explorer, and for a way to filter the views to items and
  folders picked in it, apart from selecting (checkboxes), with clear ways to remove the filter. Both went into D3,
  since filtering and hiding are two sides of which items the views show; widely used items and predeclared classes
  moved from D3 to a new D4. To settle in review: whether the filter is remembered (recommended: yes, per workbook).
- The user now rates the lag on their large project at about 3 out of 10, and would rather keep every feature than gain
  speed; D1d says so.

### 2026-11-04: Step D1c, view controls and both pages' summary

- Focus on click is one shell setting for the page, on by default and remembered per tool, shown by a Focus button at
  the start of every view's toolbar (vbadoc's graph and class diagram, and pqdoc's graph, which gains Focus: a query
  and its chains, fitted). Ctrl+click (Cmd+click) does the other thing for a click, in the views, the explorer and the
  side panel's links. Esc clears the selection in the shell, after an open menu or a box with text in it.
- vbadoc's Direct / Transitive moved into the graph's Settings (an earlier choice is kept). The class diagram's legend
  picks out kinds of box and relationships as the graph's does.
- Both pages' top bar has a summary, a shell slot: the counts and when the workbook was last saved
  (`WorkbookFile.LastSaved`, from `docProps/core.xml` or an .xls file's summary information), written in UTC and shown
  in the viewer's time. pqdoc dropped its generation time (FR-HTML-8), so it doesn't ask before overwriting a text
  output it would write unchanged (FR-OVR-1); its `TimeProvider` went.
- Requirements (vbadoc's views; pqdoc's FR-HTML-8, FR-HTML-13, FR-OVR-1), both decisions and READMEs, the shared README
  and the guidelines follow. vbadoc 0.14.0, pqdoc 1.9.0.
- Tests: 9 vbadoc and 5 pqdoc focus browser tests, 2 class diagram legend tests, 5 shared, 5 pqdoc and 3 vbadoc unit
  tests; tests not about focus open the page with it off. Screenshots in both themes, wide and narrow.

### 2026-11-04: Step D1e, `'@Folder` after a label

- The user's modules start with a header block of labelled values tabbed to line up, and they wanted the folder there:
  `' Folder:<tabs>@Folder("My Folder")`. Rubberduck reads annotations only straight after the `'`, so it ignores that
  line (nothing breaks). vbadoc now reads the annotation straight after a label ending in a colon too; prose that
  mentions it still doesn't count. The options weighed are in the plan. vbadoc 0.13.0.
- Tests: 5 new parser cases. vbadoc's requirements, decisions and README follow.

### 2026-11-04: Step D1a, panel controls and the copy button

- The explorer's and side panel's buttons are icons at the two ends of the top bar (three bars beside the title, `</>`
  after the version), with their names in tooltips and `aria-label`s. Our own SVG, as are the lock and copy icons.
- The side panel follows the selection unless locked open: `shell.selectionChanged(id)` (replacing
  `shell.explorer.reveal`) opens or closes it, and the lock in its corner is remembered per tool. Closing it by hand
  unlocks it. The views keep still while it follows (`shell.panel.following()`), moving the selection into sight only
  if the panel would cover it; before, the resize also interrupted vbadoc's move into sight.
- The user asked for a copy button in the source after D1b, added to D1a in the plan (79481b7): it's in the shell's
  code view, copies the lines shown as plain text, stays in view as the code scrolls, and selects the code for Ctrl+C
  if the browser won't copy.
- vbadoc's Source panel requirements, pqdoc's FR-HTML-12 and FR-SRC-4, both decisions and READMEs, the shared README
  and the guidelines follow. vbadoc 0.12.0, pqdoc 1.8.0.
- Tests: 12 new vbadoc and 6 new pqdoc browser tests; tests of the panel's size open the page with it locked open.
  Screenshots checked in both themes, wide and narrow.

### 2026-11-04: Step D1b, explorer layouts and nested query groups

- pqdoc reads nested query groups to any depth: after a group's description, a byte says whether a parent's GUID
  follows, then its order. D1 read those as five fixed bytes, so one nested group lost them all. A list that still
  can't be read now gives a warning. Groups with no queries aren't shown; a group's description is its tooltip. The
  page data's `groups` is a list of `{ name, parent, description }`, referred to by place.
- The explorer has layouts, in the shell (`shell.explorer.setLayouts`): Folders or Types in vbadoc, Groups or Types in
  pqdoc, with a switch when there's more than one, remembered per tool. Folders and groups have a folder icon, our own
  SVG. A node's tooltip moved to its row; the segmented control's styles moved into `shell.css`.
- The user's updated `Queries.xlsx` and `Queries.xlsb` are committed with this step; their README lists the groups.
  pqdoc's FR-HTML-11 and decisions, vbadoc's requirements and decisions, both READMEs, the shared README and the
  guidelines (layouts, icons) follow. vbadoc 0.11.0, pqdoc 1.7.0.
- Tests: 6 new pqdoc unit tests and 1 CLI test; 4 new pqdoc and 2 new vbadoc explorer browser tests, with others
  extended. Screenshots checked in both themes, wide and narrow.

### 2026-11-04: D1's follow-ups settled

- The user settled the plan's three questions: our own SVG icons; both pages show when the workbook was last saved,
  never when the page was made, so pqdoc drops its generation time (FR-HTML-8 changes in D1c); and one Focus on click
  switch for every view, on by default, with Ctrl+click doing the other and Esc clearing the selection everywhere.
- The user added nested query groups to `Queries.xlsx` and `Queries.xlsb` (committed with D1b). They show the layout
  D1 didn't know: a has-parent byte, then the parent's GUID, before the order. D1b moves ahead of D1a.

### 2026-11-04: D1 reviewed; follow-up steps added to the plan

- The user tried D1 on real workbooks. Added steps D1a (panel toggles as icons at the ends of the top bar; the side
  panel follows the selection, with a lock), D1b (explorer layouts, folders or kinds; pqdoc's nested query groups,
  which showed none on a workbook with 20 groups up to 4 deep), D1c (Ctrl+click to focus, Transitive into Settings, the
  class diagram's legend as the graph's, vbadoc's summary) and D1d (speed on a project with 70 modules and 1,540 member
  dependencies). The Show menus move into the explorer as part of D3.
- To settle in review: Font Awesome's icons are CC BY 4.0, outside the guidelines' licences (recommend drawing our own);
  a generation time in vbadoc's page breaks its determinism and A5's check (recommend the workbook's saved time);
  whether pqdoc's graph gets Focus so Ctrl+click works there too (recommended).

### 2026-11-04: Step D1, the explorer

- Both pages have an explorer on the left, in the shell: a tree with a filter box, ARIA tree keys, following the
  selection, and hiding and resizing like the side panel (whose code became `resizablePanel`, used by both). Open by
  default on a wide screen. vbadoc 0.10.0, pqdoc 1.6.0.
- vbadoc groups modules by `'@Folder("Parent.Child")` comments (read in the declarations, `ModuleModel.Folder`), or by
  kind, with members in source order. pqdoc reads query groups and result types from the workbook's metadata
  (`Extraction/QueryMetadata`, failing safe), and for a folder finds functions in the code (`Analysis/FunctionDetector`);
  it shows Excel's groups and Other queries, or queries and functions.
- vbadoc's and pqdoc's requirements (an Explorer section; FR-HTML-11), decisions and READMEs, the guidelines' pages
  section and the shared README follow. Four vbadoc browser tests written for the graph's geometry open the page
  without the explorer; one width test now measures beside it.
- Tests: 27 new pqdoc unit tests, 12 new vbadoc unit tests, 9 vbadoc and 5 pqdoc explorer browser tests. Screenshots
  checked in both themes, wide and narrow.

### 2026-11-03: Step A5, vbadoc asks before overwriting its page

- vbadoc gains `--on-exists` and `--force` (`-y`), with pqdoc's rules: by default it asks when interactive and refuses
  with exit code 4 when not. Only a page this run would change is asked about; module files and `.gitattributes`
  follow the workbook. The check comes before anything is written. vbadoc 0.9.0; its README notes that scripts now need
  `--force`, and that a new build's stamp counts as a change.
- pqdoc's overwrite code moved to `shared/` (`Files/OverwriteResolver`, `OnExistsBehaviour`, `IUserInteraction`;
  `CommandLine/OverwriteOptions`, `ConsoleUserInteraction`), unchanged in behaviour and wording. pqdoc 1.5.3.
- vbadoc's requirements, decisions and README, the guidelines' shared options, and the shared README follow. Tests:
  19 new in vbadoc, 12 new in shared, and pqdoc's 11 resolver tests moved to shared.

### 2026-11-03: Step C2, the shared page shell

- Both pages are built on one shell in `shared/`: `ShellPage` with `Resources/Shell/` (`shell.html`, `shell.css`,
  `shell.js`), owning the top bar, tabs, theme, stored settings, side panel and splitter, Load folder and source view.
  Each tool keeps its views, search, side panel contents and data; its `page.html` became fragments. vbadoc 0.8.2,
  pqdoc 1.5.2. vbadoc's `page.js` went from 692 to 444 lines, pqdoc's from 484 to 235.
- The side panel's ids are now `side-panel-…` in both; stored settings keep their keys. vbadoc's heading and Load folder
  gained tooltips; nothing else changed.
- Checked: screenshots of both pages in both themes, wide and narrow, compared pixel by pixel before and after with the
  stamp made the same, differ only as much as two runs of the same page do. All browser tests pass, changed only for
  the renamed ids (87 vbadoc, 79 pqdoc); 11 new shell tests (50 shared). Both pages work on the private workbook.

### 2026-11-03: vbadoc's overwrite options added to the plan

- The user asked for vbadoc to have pqdoc's `--on-exists` and `--force` (`-y`), and to ask before overwriting by
  default in the same way. Added as step A5, straight after C2: no existing step fits, since C2 allows no new features
  and the later steps are page work or importing.
- It covers only the page: module files and `.gitattributes` follow the workbook as pqdoc's `.m` files do, and an
  unchanged page isn't asked about. pqdoc's overwrite code moves into `shared/` for both tools to use.

### 2026-11-03: Step C1, the shared library

- New `shared/`: `ExcelDocumenter.Shared` and its tests, in the root solution and both tools' solutions. One copy each
  of opening a workbook (`WorkbookFile`, `ExtractionException`), `ExitCodes`, `ToolVersion`, `AtomicFileWriter`,
  `SourceFolderWriter` (behind vbadoc's `ProjectExporter` and pqdoc's `QueryFileExporter`), the page helpers
  (`PageBuilder`, `EmbeddedResources`, `VendoredScripts`) and d3. vbadoc 0.8.1, pqdoc 1.5.1.
- Each tool keeps its own messages. vbadoc now writes its files atomically, as pqdoc did. pqdoc escapes text in its
  page as vbadoc does, rather than with `WebUtility.HtmlEncode`.
- Both stamps now follow `shared/` too. The guidelines' "no shared code project" rule is replaced by what belongs in
  `shared/`.
- Checked: output from every fixture in both tools (36 runs, 233 files, logs and exit codes included) is byte-identical
  to the output before the move, apart from the build stamp and pqdoc's generation time. All tests pass: 39 shared,
  250 + 87 vbadoc, 394 + 79 pqdoc (6 skipped as before). Duplicate tests moved to `shared/` with the code.

### 2026-11-03: Step B4, pqdoc's page shows M source

- The details panel has a Source section: line numbers and highlighting by a new `m-highlight.js`. As in vbadoc, the
  page holds only each query's file name and hash, and Load folder reads the `.m` files, warning when one has changed;
  `--embed-source` puts the code in the page instead. pqdoc 1.5.0.
- Copied from vbadoc's source panel under the same names, for C2 to merge. The syntax colours are vbadoc's, checked as
  text: all at least 4.5:1 in both themes.
- Requirements gained 4.10 (FR-SRC-1 to FR-SRC-3), the option and acceptance criterion 12; the README has a privacy
  note. 34 new tests, including a 1 MB Enter Data line (about 0.1 s to show).

### 2026-11-03: Step B3, pqdoc writes `.m` files

- With a workbook as input, each query goes in its own `.m` file beside the outputs (`Output/QueryFileExporter`), kept
  in step like vbadoc's module files: unchanged files kept, stale top-level `.m` files deleted, `.gitattributes`
  created, `--on-exists` not applied. pqdoc 1.4.0.
- File names escape Windows-forbidden characters, control characters and `%` as `%XX`, and a device name's first
  letter; the folder reader reverses exactly those escapes, so names round-trip. CRLF outside text literals.
- Requirements gained 4.9 (FR-QF-1 to FR-QF-4) and acceptance criterion 11. 35 new tests. The private workbook's file
  matches the user's hand export apart from line endings.

### 2026-11-03: Step A4, documentation file names

- vbadoc writes `<workbook>-vba.html` (was `index.html`) and gains `--output-name`; pqdoc writes
  `<workbook or folder>-powerquery.*` (was `powerquery-dependencies.*`). `#` and `%` become `_`, and `--output-name`
  refuses them. vbadoc 0.8.0, pqdoc 1.3.0.
- Older outputs a tool wrote under another name are found by their page's generator tag and deleted from a folder the
  tool owns (vbadoc's output folder, a workbook's `PowerQuery` folder), or only mentioned in a folder of `.m` files.
- pqdoc's titles now show the workbook's file name or the folder's name, as vbadoc's do (`ExportContext.Title`).
- pqdoc's requirements gained 4.8 (FR-OUT-1, FR-OUT-2); the guidelines' naming section has the shared rule.

### 2026-11-03: Documentation file names added to the plan

- The user asked for the two tools' documentation files to be named the same way, representative of the source.
  Added as step A4, before B3: `<workbook>-vba.html` and `<workbook>-powerquery.html` (and `.csv`, `.puml`), a
  folder's name for a folder of `.m` files, `--output-name` in both, older pages cleared from folders the tools own,
  `#` and `%` replaced. Views files keep fixed names.

### 2026-11-03: Step B2, pqdoc reads workbooks

- `pqdoc <workbook>` reads the queries straight from `.xlsx`, `.xlsm` or `.xlsb` (`Extraction/`: `WorkbookReader`,
  `SectionSplitter`, `ExtractionException`), and writes into a `PowerQuery` folder beside the workbook. The argument is
  now `<input>`, a workbook or a folder. pqdoc 1.2.0.
- Requirements gained section 4.2 (FR-IN-1 to FR-IN-4) and acceptance criterion 10. Every unreadable case stops with
  exit code 3 and says why. 24 new tests, with damaged workbooks built by a test helper.
- The private workbook's query reads exactly as the user's hand export.

### 2026-11-03: Step B1, sample workbooks

- The user made `Queries.xlsx` (14 queries) and saved it as `Queries.xlsb`, in
  `pqdoc/tests/PowerQueryDocumenter.Tests/Fixtures/Workbooks/`, with a README of what each query covers.
- A throwaway script confirmed the format (details in pqdoc's decisions). The `.xlsb` holds identical Power Query data.
  Descriptions are attribute records in `Section1.m`, not metadata. Query groups are a binary list in the metadata.
- Agreed with the user: `.m` files use CRLF outside text literals, and keep literal line breaks as Excel stored them.
- Fixtures go into a public repo, so the author fields were removed before committing. Excel also stores the folder a
  workbook was saved in, and the data model a temporary path. Both show only the home machine's user name, and the user
  chose to commit them as they are.

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
