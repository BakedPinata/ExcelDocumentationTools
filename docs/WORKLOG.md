# Work log

Where the repository stands and how it got here. A new session reads this first. Keep it short and link to the detail
rather than repeating it.

- **Detail lives in:** [GUIDELINES.md](GUIDELINES.md) for conventions, each tool's `docs/PLAN.md` for stages, each
  tool's `docs/DECISIONS.md` for design decisions, and git history for the rest.
- **Updating it:** at the end of each stage, and after any session that changes direction, update *Current state* and
  add an entry at the top of the log. Give the date, what changed and why, and commits, in a few lines.

## Current state (2026-11-11)

- **vbadoc 0.30.0:** stages 0–5e are done, the class diagram is drawn with d3 (no Mermaid), and the page is
  `<workbook>-vba.html`, with an explorer of modules and members, in `'@Folder` folders or by kind. It asks
  before overwriting a page that would change (`--on-exists`, `--force`), as pqdoc does. Stage 6 (UserForms) is parked;
  stage 7 waits for the feedback plan.
- **pqdoc 1.21.0**, plus stages 3b–3d: reads a workbook's queries directly, or a folder of hand-exported `.m` files,
  and writes HTML by default as `<workbook or folder>-powerquery.html`. From a workbook it also writes each query to a
  `.m` file, kept in step with the workbook. The page shows each query's M code, highlighted, and an explorer of the
  queries in their Excel groups, nested as Excel shows them, or as queries and functions.
- **`shared/`** (`ExcelDocumenter.Shared`): the code both tools use, since step C1, and the page shell both pages are
  built on, since C2. See its README.
- **`FEEDBACK-PLAN.md`** (root): the current plan, agreed on 2026-11-03, steps A0–I4. The user's build, test and
  publish of A0 on the work machine is still to come. A0–A5, F1, B1–B4, C1 and C2 are pushed and reviewed. D1 is done.
  The user's review of D1 added steps D1a to D1d, before D2, now settled (see the plan's "After D1's review"). D1b is
  pushed; the user's check of the workbook with 20 groups is still to come. D1a (with the copy button the user asked
  for after D1b) is pushed. D1e (`'@Folder` after a label, asked for after D1a) and D1c (view controls and both pages'
  summary) are pushed. The user's review of D1c added the explorer's filter (checkboxes, apart from selecting) and
  Expand / Collapse all to D3, which moved widely used items to a new D4. D1d (speed) is pushed; the user found it a
  little better on the production workbook, good enough for now. A6 (a name declared again under `#If` is one member)
  is pushed. D2 (favourites) is pushed. D3 is split into D3a (Expand / Collapse all and the views' filter), D3b
  (hiding) and D3c (the Show menus in the explorer), all pushed, as are D4 (widely used items and predeclared classes)
  and E1 (Uses / Used by with references). E2 (the Calls tab) is pushed. E3 (the logic around each call) and E4 (a
  query's steps, in pqdoc) are pushed. E5 (the sequence diagram, which the user asked for) is pushed, as are its
  review's follow-ups (E5a) and a gentler drag. G1 (saved views) and its review's follow-ups (G1a) are pushed. The
  user parked G2–G4 (the views file in the page, printing, copying a view) to come back to later. H1 (the spike on
  writing modules and queries into workbooks), H2 (the Export tab, the manifest and bundles) and H3 (vbadoc writing into
  workbooks) are pushed: Excel accepts what the spike wrote, at home and at work. H4 (pqdoc writing into workbooks) is
  pushed. I1, the first of the desktop app's steps (`xldocui`, in Windows Forms), is on `feedback/i1-desktop-spike`: the
  spike worked at home and on a work machine, and is deleted. I2 (each tool's command line as a library) is reviewed, on
  `feedback/i2-cli-library`, on top of I1. **Next: I3.** The user's other checks of what's built so far come after
  phase I; the plan lists them.
  E5b (Calls tab follow-ups) is noted on `feedback/e5b-calls-follow-ups`, for later.
- **Branches:** each step is worked on a branch of its own (`feedback/<step>`), committed a change at a time; the user
  reviews, rebases onto main and pushes.
- **`USER-FEEDBACK-NOTES.md`** (root, untracked): the user's feedback from trying both tools on real workbooks, and the
  source of the feedback plan. It holds details about the user's workplace, so keep them out of the repository.
  **Never commit it.**
- **.NET 10** since step A0. `dotnet test` runs on the Microsoft Testing Platform, so it takes `--project` or
  `--solution`, not a bare path.

## Log

Newest first.

### 2026-11-11: I1 on a work machine, and I2's review

- The user ran the I1 spike on a work machine, and it worked as expected, so I3 goes ahead. The spike is deleted, as
  planned; I3 and I4 can take its code from the history.
- On reviewing I2, the user approved saying in both tools' decisions that the command line is in the `.Cli` library.

### 2026-11-08: Step I2, the command lines as libraries

- `VbaDocumenter.Cli` and `PowerQueryDocumenter.Cli` now hold what was in each console app, which is only its `Program.cs`.
  `CliRunner`, `CliDefinition`, the logging setup and the services are public, for the desktop app; the rest stays internal.
- Checked as C1 was, with the old build run twice so anything random could be told apart: 171 cases and 37,283 outputs,
  with no difference but the build stamp and the source paths in `--verbose`'s stack traces. Both tools also ran in one
  process from a throwaway program that could only see the public API.
- `--help` and `--version` describe whichever program is running, so the desktop app shouldn't send them to `CliRunner`.

### 2026-11-08: Step I1, the desktop window spike

- The user settled the desktop app as Windows Forms, named `xldocui`. `spikes/i1-desktop` is a small window with a file
  picker, a log pane, and a run off the window's thread with a five-answer dialog and Cancel. It's published with the tools'
  own settings, and reports its size, start-up time and anything unpacked. Throwaway, and left out of `publish.ps1`.
- At home: one 110.7 MB file (49.3 MB compressed), about 0.26 s to the window (x64 under emulation on Arm), nothing
  unpacked and nothing loaded from outside Windows. A randomly named file is written to the temporary folder and deleted during start-up, only with visual
  styles on. All of the window's parts were driven through UI Automation and worked.
- The work machine is the real test: whether application control allows the window, and whether NuGet there has the
  Windows desktop runtime pack. The steps are in the spike's README.

### 2026-11-08: Step H4, pqdoc writing into workbooks

- `pqdoc import` writes a new workbook (`--new`, `.xlsx` or `.xlsm`) or a copy of a target (`--into`), with `--in-place` and
  `--on-clash` as vbadoc has them. New queries come as connection only, with their descriptions. Overwrite replaces a
  query's M and description, and keeps where it loads to, its connection and the target's spelling of its name.
- Excel can't be run here, so the output was compared with the spike's P1, P3, P4 and P5, the files the user tried: the M,
  permissions, metadata and bindings match, GUIDs aside. Overwrite is new, and is for the user's check in Excel.
- A test on the real sample workbook caught a fault: Excel stores some queries with LF, and the writer was changing them
  to CRLF. They're now written as stored.
- The Export tab now suggests `--new Shared.xlsx`. `TargetFile`, `TargetComparison` and the wording of Skip and Overwrite
  moved to `shared/`. `spikes/h1-import` is deleted, as planned; its `out/` folder is untracked and still on disk.
- pqdoc 1.21.0. The user's check in Excel is still to come.

### 2026-11-08: A desktop app added to the plan

- The user finds the commands' flags too long to type, and asked for a self-contained desktop app over them, as well as
  the command line. Added as steps I1 to I4, after H4: a spike to see it runs on a work machine, the tools' command lines
  made callable, then a Document tab and an Import tab. It runs the tools' own commands inside the app.
- To settle when H4 is reviewed: what it's built with (Windows Forms recommended) and its name.
- The user's checks of what's built so far wait until after phase I; the plan lists them.

### 2026-11-06: Step H3, vbadoc writing into workbooks

- `vbadoc import` writes a new workbook (`--new`) or a copy of a target (`--into`), with what the choice brings and its
  library references. A name already in the target is asked about before anything is written, and `--on-clash` answers for
  an unattended run. `--in-place` changes the target itself, opt-in, asking first, with `--force` to answer yes.
- What pqdoc will need too is in `shared/`: `OpcPackage`, `BlankWorkbook`, `ClashResolver`, `ImportTargetOptions` and the
  prompts, with Skip All. The spike stays until H4 has what it needs.
- Excel can't be run here, so the tool's output was compared with the spike's R1 and R2, which Excel accepted: `--into`
  matched exactly, and `--new` but for a column width the spike set for itself.
- Real fixtures showed what synthetic ones wouldn't: a control reference's libid names a per-machine cache, so it's now
  known by the library it was made from. The spike's writer had faults with modules called `Name` or `Dir`, now fixed.
- `--drop-signature` is how a signed target goes ahead, which the plan left to be named.
- vbadoc 0.30.0. The user's check in Excel is still to come.

### 2026-11-06: Step H2, the Export tab and bundles

- The page has an Export tab, the shell's, in both tools: pick whole modules, classes or queries, and it shows what comes
  along, what may be wanted too (an interface's implementations), what to check in the target, and the library
  references. Its picks are its own, remembered per workbook, and it lists everything, whatever the views are filtered to.
- A manifest (`<tool>-export.json`) holds the picks, not what they bring, so running it again picks up new dependencies.
- `vbadoc import` and `pqdoc import` take a manifest or names, say what that comes to, and with `--bundle` write the files
  with a README on importing them by hand. A bundle only goes into an empty folder or an earlier bundle.
- The rules are in Core (`ModuleImport`, `QueryImport`) and reach the page as data; a browser test compares the two for
  every module in the sample. vbadoc now reads a project's library references.
- Also agreed: `--in-place`, to change the target itself, opt-in, asking first and with `--force` for unattended runs. That
  and writing into workbooks are for H3 and H4.
- vbadoc 0.29.0, pqdoc 1.20.0. The user's check of a bundle by hand in Excel is still to come.

### 2026-11-06: H1 done; H2 to H4 settled

- A reference to a library that isn't installed stopped the whole project compiling, even code needing no library, so
  the tool will leave such a reference out, finding it by GUID in the registry. The user's own modules, with what they
  depend on, copied across and worked.
- Agreed with the user: an Export tab and a manifest carry the choice, not the views file; whole modules only for now;
  and a name already in the target is asked about, each time: Skip, Overwrite, Skip All or Overwrite All.
- The plan's "After H1" has all of this, with the `import` command, and H2 to H4 are rewritten to match. Individual
  procedures, a Merge choice and UserForms are in its Later section.

### 2026-11-06: H1's findings, and library references to try

- A real workbook saved at work has a metadata-only label. A copy with VBA and queries added kept the label, opened
  and saved without asking, and everything in it worked. So H3 and H4 go ahead, with no template for new workbooks, and
  `.xlsb` as a target. The findings are in both tools' decisions and the plan's "After H1".
- The user's idea of generic (not tied to the workbook's purpose: logging, files, paths) means nothing should try to
  detect it, and that relying on `ThisWorkbook` isn't a reason to leave something out; the plan says so.
- They asked to try library references, with a way to opt out. The spike copies them from a source project (R
  workbooks), and a `copy` command tries it on real modules, following what they use. For the user to check in Excel.
- To settle before H2: an Export tab and a manifest of its own, proposed in place of the views file.

### 2026-11-06: H1 worked in Excel; labelled workbooks next

- The user tried the spike's eleven workbooks at home and at work, and they all worked: modules compiled and ran, and
  queries loaded, in existing and new workbooks.
- Work requires a sensitivity label before editing and saving, and none of those workbooks had one, so the spike gained
  `inspect` (is a workbook's label only metadata, or encrypted?) and `add` (a copy of any workbook with the modules and
  queries added, saying whether the label's parts came through). On a stand-in with made-up ids they come through byte
  for byte; a real label is for the user to try at work.
- Also noted for later, on `feedback/e5b-calls-follow-ups`: step E5b (Ifs that seem to be missing from the Calls tab, and
  keeping the sequence diagram's participants in view). Every call and dependency in such Ifs is found, so importing
  doesn't need it.

### 2026-11-05: G2–G4 parked, H1 next

- The user rebased G1 and G1a onto main, parked G2–G4 to come back to later, and asked for H1 (the spike on writing
  into workbooks) next, on its own branch. Their use case: pick out the generic modules, classes and queries in a
  workbook, with what they depend on, and load them into a new or existing workbook.

### 2026-11-05: Step G1a, follow-ups after G1's review

- A view keeps everything the page shows: the selection and its focus, Focus on click and Focus on favourites, both
  panels (shown, size, lock), the explorer's layout and the widely used share; in vbadoc also the source panel's
  whole module and Dependencies, the graph's direction, Path, legend and settings, and the class diagram's legend.
  Opening a view sets the selection rather than clearing it. Favourites, the theme, the dragging settings and the zoom
  stay out (a view opens fitted).
- Fixed: moved nodes weren't put back after Re-run layout or a focus, as the graph kept the layout on screen and put
  that back instead. Nodes and boxes moved while focused are now kept for that focus, and in views.
- Save over on every other view, and from a new view's taken name, with Undo; Save changes, Delete and Import have
  Undo too. A views file names its workbook, and an import from another workbook, or that mentions missing items,
  says so first, with what it would replace, and can be cancelled.
- vbadoc 0.28.0, pqdoc 1.19.0, on the same branch, a commit per change. Browser tests: 13 views tests in vbadoc (one a
  regression test that failed before the fix), 4 in pqdoc. Screenshots of the menu in both themes.
- Open, as after G1: pqdoc's `Requirements.md` and `DECISIONS.md` don't describe views yet, waiting for the user.

### 2026-11-05: Step G1, saved views

- The Views menu, in the shell's top bar, saves the page's arrangement as a named, described view, opens it, renames,
  describes and deletes it (with Undo), and exports and imports views as `<tool>-views.json`. Views are kept per
  workbook in the browser. A view holds the tab, the filter, hidden items and Show… choices, and each view's own part:
  in vbadoc, the graph's level and the positions at each level where something was moved, the class diagram's Members
  switch and moved boxes, and the Calls tab's starting point, open rows and switches; in pqdoc, the graph's positions
  once something was dragged. Items a view mentions that the workbook doesn't have are listed (menu, page notice,
  console), never an error.
- The class diagram now keeps moves made in a filtered diagram while the page is open (D3a laid it out afresh), so a
  filtered view can keep them.
- vbadoc 0.27.0, pqdoc 1.18.0. Browser tests: 7 in vbadoc, 3 in pqdoc.
- Open: pqdoc's `Requirements.md` and `DECISIONS.md` aren't covered by the plan for G1, so they wait for the user.

### 2026-11-05: A gentler drag in the graph (E5a)

- On the user's suggestion of weakening the pull while dragging, measured first: the pull-in happened during a drag
  (a module's outline at member level pulled the rest in to 85% of its spread on the production workbook), not after.
  Weakening the pull alone blew the module-level graph apart; stirring the layout up less (alpha target 0.1, not 0.3)
  stopped the pull-in, and the rest of the graph moves less while dragging. vbadoc 0.26.1, on its own branch.
- A regression test on the 70-module fixture, which fails with the old heat.

### 2026-11-05: Step E5a, follow-ups after E5's review

- The sequence diagram's lanes and frames widen to fit their labels and guards. Open all below (a row's button, `*`,
  Shift+click in the diagram) opens everything under a procedure or group, so whole entry points can be drawn; that's
  in scope now, the old reasons having been Mermaid's.
- The Show… menu has Show nothing and All / None per section (both tools).
- Every graph setting explains itself on hover. A gentler drag (only neighbours moving, the layout stopping when
  dropped) was tried and rolled back after review: it froze the graph, so nodes could overlap.
- From now on each phase is worked on its own branch, with a commit per change; the user rebases and pushes.
- vbadoc's requirements (Out of scope, the Calls view, the explorer, the graph), both decisions, pqdoc's FR-HTML-17, the
  shared README and vbadoc's README follow. vbadoc 0.26.0, pqdoc 1.17.0.
- Tests: 5 browser tests (explainers, open all below twice, Shift+click and guards, Show nothing). Screenshots.

### 2026-11-05: Step E5, the sequence diagram

- The Calls tab's Tree / Sequence switch draws what's open in the tree as a sequence diagram: modules as lifelines,
  calls as messages in order, late-bound calls to their own lifeline, blocks as frames. It shares what's open with the
  tree, and its ▸ and frame labels open and close in place.
- vbadoc's requirements (the Calls view, and Out of scope), decisions and README follow. vbadoc 0.25.0.
- Tests: 5 browser tests. Screenshots in both themes. On the private workbook, every branch open four deep draws 77
  diagrams (244 messages, 97 frames) in about 230 ms, with no errors.

### 2026-11-05: Step E4, a query's steps

- pqdoc's structural pass keeps the steps of a query's own outermost `let` (at the top, or a function query's body):
  each name, line and the queries it uses, then the `in` expression; a `let` inside a step adds none.
- The details panel lists them above the source, each linking to its line and to the queries it uses.
- pqdoc's FR-HTML-20, decisions and README follow. pqdoc 1.16.0.
- Tests: 6 unit tests with the B1 sample workbook (nested `let`, quoted names, comments, a step over several lines, a
  function query, no `let` of its own) and 3 browser tests. The private folder's one query reads as one step and
  its `in`, in line order.

### 2026-11-05: Step E3, the logic around each call

- The parser follows block statements (If/ElseIf/Else, single-line If, Select Case and Case, loops, With, On Error
  handlers, and #If in a body); each call carries its block, and the Calls tree shows blocks as rows holding their
  calls, leaving out blocks with none. Turned round, a caller says which block its call is in.
- vbadoc's requirements, decisions and README follow. vbadoc 0.24.0.
- Tests: 10 parser tests of blocks and 1 of calls' blocks; 2 new browser tests, and the E2 ones updated for the
  nesting. On the private workbook: 246 blocks in 80 procedures, every keyword block's line names its keyword, 80
  of 213 calls are in a block, no empty block rows, at most 49 ms an open, no warnings or errors.

### 2026-11-05: Step E2, the Calls tab

- vbadoc's Core lists every call in the order of the code (`calls` in the page data): procedures, properties, events,
  Declares, and late-bound calls.
- A Calls tab draws them as a tree from the entry points and favourites, one level at a time, with recursion marked,
  interfaces opening to their implementations and RaiseEvent to its handlers, calls to items not shown counted, and
  each call's line linked. Called by turns it round.
- A click in the tree only selects and Ctrl+click starts the tree there: the exception to Focus on click the user
  agreed, now in the guidelines.
- vbadoc's requirements (a Calls view section), decisions and README follow. vbadoc 0.23.0; pqdoc unchanged.
- Tests: 5 unit tests of the calls (order, recursion, interface, RaiseEvent, no variables); 8 browser tests. On the
  private workbook every one of 213 calls names what it calls, and opening every branch six deep took at most 45 ms an
  open, with no errors.

### 2026-11-05: Step E1, Uses / Used by with references

- Both analysers record each reference's line: vbadoc on every member edge (`lines`), pqdoc beside each edge
  (`ReferenceLines`), both in the page data. A reference on a continued statement is on the line that names its
  target; the production workbook had two such.
- The shell's Uses / Used by list lists each item once, with tags, and opens to its lines with their text; choosing one
  selects what holds the line and marks it in the source, which can now scroll to a line.
- vbadoc's source panel requirements, pqdoc's FR-HTML-19, both decisions and READMEs and the shared README follow.
  vbadoc 0.22.0, pqdoc 1.15.0.
- Tests: 5 vbadoc and 6 pqdoc unit tests of the lines; 4 vbadoc and 3 pqdoc browser tests, from embedded source and a
  loaded folder. On the private workbook all 238 references name their target, and a chosen line is marked.

### 2026-11-05: Step D4, widely used items and predeclared classes

- vbadoc's Core counts how many other modules use each module and member (`usedBy` in the page data), and marks classes
  with `VB_PredeclaredId = True` (`predeclared`). pqdoc's page data already had each query's direct users.
- The shell lists what's used by at least a share of the others in a Widely used group, with Hide all, and marks it
  with a hub in the explorer; the share (20% to begin with) is in the Show… menu. vbadoc nests a module's widely used
  members under it. Predeclared classes are labelled in the explorer, the source panel and the graph's tooltip.
- The default share is 20% because on the production workbook the most used module is used by 8 of the 36 others.
- The guideline that nothing leaves the views silently, agreed after D3b, is in the guidelines.
- vbadoc's explorer requirements, pqdoc's FR-HTML-18, both decisions and READMEs, the shared README and the guidelines
  follow. vbadoc 0.21.0, pqdoc 1.14.0.
- Tests: 6 vbadoc unit tests of the counts and page data; 4 vbadoc and 1 pqdoc browser tests; older explorer tests now
  look at the layout apart from the shell's groups. Screenshots in both themes. On the private workbook: at 20%, one
  module with two of its members; no predeclared classes; no errors.

### 2026-11-05: Step D3c, the Show… menu in the explorer

- One Show… menu, the shell's, in the explorer, chooses what kinds of thing the tree and every view show, replacing
  vbadoc's graph and class diagram menus; pqdoc gains one (queries, functions, queries with no connections). The tree
  drops what's left out. The button and a note in every view's toolbar count the choices, with Show everything.
- vbadoc's graph and class diagram choices merge, each applying wherever it means something; the class diagram's
  Members option is a toolbar switch. Choices remembered by the old menus are ignored.
- Drop-downs closing on a press outside or Esc moved from vbadoc into the shell.
- vbadoc's explorer and class diagram requirements, pqdoc's FR-HTML-17, both decisions and READMEs, the shared README
  and the guidelines follow. vbadoc 0.20.0, pqdoc 1.13.0.
- Tests: vbadoc's 9 tests of the old menus moved to the explorer's (the two-column test became one that the menu fits
  the window); 3 vbadoc and 2 pqdoc Show… tests. Screenshots in both themes and at a narrow width. On the private
  workbook every module kind turned off leaves the tree and graph as expected, with no errors.

### 2026-11-04: Step D3b, hiding and Focus on favourites

- Hiding is the shell's, in both tools: a button beside each row's star and in the side panel's header takes an item
  out of every view, remembered per workbook. It stays in the explorer, struck through, and is listed in a Hidden group
  at the foot with Show all; each view's toolbar counts hidden items and opens that group; the side panel says when the
  selection is hidden or linked to hidden items, and tags them.
- Focus on favourites, from the Favourites group's row, shows only the favourites and what they use, with the focused
  selection's own, and a note in each toolbar with Show all.
- vbadoc: a hidden module takes its members; a hidden member leaves its module's box. The graph redraws only when what
  decides its focus changes. pqdoc: favourites bring their whole upstream chains.
- An explorer row short of room now shortens its detail before its name.
- vbadoc's explorer requirements, pqdoc's FR-HTML-16, both decisions and READMEs, the shared README and the guidelines
  follow. vbadoc 0.19.0, pqdoc 1.12.0.
- Tests: 5 vbadoc and 3 pqdoc hiding tests. Screenshots in both themes. On the private workbook, hiding and Focus on
  favourites work in both views, with no errors.

### 2026-11-04: Step D3a, Expand / Collapse all and the views' filter

- D3 split in three, each reviewed before the next. The user agreed the filter is remembered per workbook.
- The shell's explorer has Expand all and Collapse all, and a checkbox on every row, apart from the name: while
  anything is ticked, every view shows only that, with a bar in the explorer and a note in each view's toolbar, each
  with Clear. Space ticks, Enter chooses; Esc clears the selection, not the filter. `favourites.init` became
  `items.init`, which also says how to describe the filter.
- vbadoc: a ticked module brings its members and a ticked member its module, in the graph (both levels) and the class
  diagram; what's ticked is shown even when unconnected. pqdoc: the graph shows the ticked queries, and focus works
  within them.
- vbadoc's explorer requirements, pqdoc's FR-HTML-15, both decisions and READMEs, the shared README and the guidelines
  follow. vbadoc 0.18.0, pqdoc 1.11.0.
- Tests: 5 vbadoc and 4 pqdoc filter tests. Screenshots in both themes and at a narrow width. On the private workbook
  the filter and Clear work in both views, with no errors.

### 2026-11-04: Step D2, favourites

- Favourites are the shell's, in both tools: starred from an explorer row (which doesn't select) or the side panel's
  header, listed in a Favourites group at the top of the explorer in every layout, starred in the graphs, and
  remembered per workbook. One no longer in the workbook is ignored.
- In vbadoc they replace Home and the trail (a remembered Home is ignored, the plan's simple answer), and Path from
  Home became Path from the nearest favourite, which names the favourite it found.
- vbadoc's explorer and graph requirements, pqdoc's FR-HTML-14, both decisions and READMEs, the shared README and the
  guidelines follow. vbadoc 0.17.0, pqdoc 1.10.0.
- Tests: vbadoc's navigation tests now cover favourites and the path (8, in place of 9 for Home, the trail and the
  path); 2 pqdoc favourites tests. Screenshots in both themes.

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
