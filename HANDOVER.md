# Handover (temporary)

Written on 2026-11-13 because the session ran out of usage part-way through step L1. **Read this, then delete it and
commit the deletion, before doing anything else.** It isn't part of the repository's documents.

## What was asked

The user reviewed `GENERALISATION-PLAN.md`, answered its section 1, and said: "Please work through steps K0 to L6,
only stopping where absolutely necessary." So don't stop for review between steps. Keep the usual rules otherwise:
a branch per step (`general/<step>`), a commit per change, build and tests passing, the plan's progress table,
`docs/WORKLOG.md` and the tools' documents updated, versions raised, Australian English, and the user rebases and
pushes.

## Where things stand

Branches are stacked, each on the one before, and **none is pushed** (only `main` and `general/plan` are on origin):

| Branch | Step | State |
|---|---|---|
| `general/k0-answers` | K0 | Done: plan revised for the answers (table at the top of its section 1), alpha decision in the guidelines |
| `general/k1-names` | K1 | Done: `CodeDocumentationTools.sln`, `CodeDocumenter.Shared`, `CodeDocumenterShell`, publish folder. vbadoc 0.31.1, pqdoc 1.21.3. All 1,694 tests passed |
| `general/k2-desktop-name` | K2 | Done: `codedocui/`, `CodeDocumenter.Desktop`. codedocui 0.4.0. Built, its 158 tests passed, window started and titled `codedocui` |
| `general/l1-lock-view` | L1 | **In progress**, committed as work in progress with this file. See below |

- **K3 was skipped on purpose:** renaming the repository on GitHub and on disk is the user's to start. Nothing in
  L1–L6 depends on it. Say so in the final summary.
- **L2–L6 are not started.** Their scope is in the plan's sections 1.4, 5.9 and 6.
- An empty folder `shared/tests/ExcelDocumenter.Shared.Tests` may be left on disk (VS Code held it). It isn't in git;
  delete it when VS Code is closed.

## L1 (Lock view): what's written

All in the working tree of `general/l1-lock-view`, syntax-checked with `node --check`, built once:

- **Shell** (`shared/src/CodeDocumenter.Shared/Resources/Shell/shell.js`, `shell.css`): a Lock view button put at
  the start of every `.view-toolbar`; `shell.lock.on()/set(on)`, `onLockChange(fn)`; `shell.filter.alone(id)` and
  `aloneIds()`; `items.init({ shown() })`. Locking ticks what the open view says it shows, keeps the ticks from
  before in `<tool>-lock|<workbook>`, and unlocking puts them back. `focused()` and `focusFavourites()` are false
  while locked. Saved views keep `alone` and `lock`. The filter note reads "Locked: …" while locked. The interface
  comment at the top of `shell.js` is updated.
- **"Alone":** at the graph's member level a module's own node can be shown while its members aren't, and a tick on
  the module would bring every member. So locking keeps such a module "alone" (in the filter by itself, its box
  half ticked; a click drops it). A tick never makes one.
- **vbadoc** (`page.js`, `graph.js`, `classes.js`, `calls.js`): `vbadoc.locked()`, `onLockChange`, `lockSource`,
  `showsItself`. The graph carries the nodes' positions into the next drawing while locked (`carriedPositions`,
  `placeAt`) and doesn't refit; the highlight only outlines the selection; a background click clears only the
  selection; dragging moves only what's dragged; Focus and Path are disabled. The class diagram does the same with
  its boxes. `window.vbadocGraph.sliding()` was added for tests.
- **pqdoc** (`page.js`, `graph.js`): the same interface; locked, the graph doesn't fit, centre, light or dim.
- **Known and accepted:** locking can add lines between the items shown (a focused graph draws only the lines on
  the way to the selection; the locked one draws every line between what's ticked). That's what the user's own
  recipe (focus, then tick everything shown) gives. Say so in the summary and the documents.
- **Tests:** `vbadoc/tests/VbaDocumenter.BrowserTests/LockViewTests.cs`, six tests. First run: 2 passed, 4 failed
  because a legend covered the node clicked (a locked view doesn't move to clear it). A `CloseLegends` helper was
  added to each test and **not yet run**.

## L1: what's left

1. Run the Lock view tests and fix what fails:
   `dotnet build vbadoc/tests/VbaDocumenter.BrowserTests -warnaserror` then
   `dotnet test --project vbadoc/tests/VbaDocumenter.BrowserTests --no-build --filter-class "VbaDocumenter.BrowserTests.LockViewTests"`
2. Add a pqdoc browser test (lock, click a query, nothing dims or moves, unlock puts the ticks back), and a shell
   unit test if `ShellPageTests` checks the shell's markup.
3. Run every test: `dotnet test --solution CodeDocumentationTools.sln` (about 6 minutes; use `set -o pipefail`).
4. Look at screenshots of the locked graph and class diagram in both themes (memory: page screenshots with the
   node.exe Playwright ships).
5. Documents: `docs/GUIDELINES.md` (HTML pages: Lock view), `vbadoc` and `pqdoc` `Requirements.md`,
   `docs/DECISIONS.md` and `README.md` (the plan's section 7 covers these), `shared/README.md`, the plan's progress
   table, `docs/WORKLOG.md`. Versions: vbadoc 0.32.0, pqdoc 1.22.0.
6. Tidy the commits: the work-in-progress commit can be amended or followed, as the branch isn't pushed.

## Useful to know

- VS Code holds the project folders, so `git mv` of a folder fails. Moving the tracked files one at a time works.
- A bare `cd` in the Bash tool moves the session; backslashes in heredocs get mangled, so write scripts to a file.
- For many exact edits to one file, a small Python script that asserts each old text occurs once worked well.
- The Access Version Control add-in (msaccess-vcs-addin) was read for K0; what it gives is in the plan's 5.5.
