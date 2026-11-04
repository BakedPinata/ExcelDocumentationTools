# ExcelDocumentationTools: feedback plan

This plan acts on the first round of feedback from using vbadoc and pqdoc on real workbooks. It replaces the combined
tool plan (`COMBINED-TOOL-PLAN.md`, in the repository's history). pqdoc still learns to read workbooks, and the two
pages still move onto one shared shell. One executable, a combined page and links from VBA to queries move to
[Later](#9-later), because the workbooks in use rarely hold both and never link them. Most of the plan is about
helping someone understand a workbook and leave documentation behind. Its last phase imports chosen modules or queries,
with what they depend on, into a new or existing workbook. It is written to be handed to a fresh Claude Code session
(or a developer) to carry out.

**Status: agreed on 2026-11-03.** The progress table shows how far it has got. Section 1 records the answers to the
questions the draft raised.

## How to use this plan

- Read the whole file before starting. Sections 1–5 are the background and the decisions; section 6 has the steps.
- Work **one step at a time**. At the end of each step:
  1. `dotnet build ExcelDocumentationTools.sln -warnaserror` and the tests of everything touched pass;
  2. update the progress table below and `docs/WORKLOG.md`;
  3. raise the version of each tool the step changed;
  4. commit;
  5. summarise what changed and how to check it, then **stop and wait for review**;
  6. push only after the review.
- Ask before doing anything that goes against this plan.
- `docs/GUIDELINES.md` and each tool's `CLAUDE.md` still apply, apart from the changes listed in section 7.
- Use Australian English in everything we write, as the guidelines set out.
- This machine can run short of memory with every browser test project going at once. If it does, run one test project
  at a time. The GitHub build runs everything.

## Progress

| Step | Scope | Who | Status |
|---|---|---|---|
| A0 | Move to .NET 10, with command-line instructions | Claude, then you on a work machine | Done; work machine check to come |
| A1 | Fix: a click in the graph pulls modules together | Claude | Done |
| A2 | Fix: the export pauses while writing the page | Claude | Done: timings added; the cause, antivirus scanning Mermaid, is fixed by F1 |
| A3 | pqdoc writes HTML by default | Claude | Done |
| F1 | Class diagram drawn by the page, with boxes you can move; Mermaid removed (moved up after A2) | Claude | Done |
| B1 | Sample workbooks with Power Query, and confirming the format against them | You in Excel, then Claude | Done |
| B2 | pqdoc reads queries from a workbook | Claude | Done |
| A4 | Documentation files named after the workbook, the same way in both tools (added after B2) | Claude | Done |
| B3 | pqdoc writes `.m` files, kept in step with the workbook | Claude | Done |
| B4 | pqdoc's page shows highlighted M source | Claude | Done |
| C1 | Shared library: `shared/`, with the C# code both tools now duplicate | Claude | Done |
| C2 | Shared page shell: both pages rebuilt on it, with no visible change | Claude | Done |
| A5 | vbadoc asks before overwriting its page, with `--on-exists` and `--force` as pqdoc has them (added after C1) | Claude | Done |
| D1 | Explorer: a tree of everything in the workbook, beside the view | Claude | Done |
| D1a | Panel controls: the toggles as icons at the two ends of the top bar; the side panel follows the selection unless locked (added after D1) | Claude | |
| D1b | Explorer layouts: folders or kinds, with folders drawn as folders; pqdoc reads nested query groups (added after D1) | You in Excel, then Claude | |
| D1c | View controls: Ctrl+click to focus, Transitive into Settings, the class diagram's legend as the graph's, vbadoc's page summary (added after D1) | Claude | |
| D1d | Speed: large projects stay smooth to pan and scroll (added after D1) | Claude, then you on a work machine | |
| D2 | Favourites, replacing Home and the trail | Claude | |
| D3 | Hiding items, flagging the ones used almost everywhere, and the Show menus moving into the explorer | Claude | |
| E1 | Uses / Used by: each item once, with links to the exact lines | Claude | |
| E2 | Calls tab: the procedures an entry point calls, in order | Claude | |
| E3 | Calls tab: the Ifs, loops and Select Cases around each call | Claude | |
| E4 | pqdoc: a query's steps, in order | Claude | |
| E5 | Optional: a sequence diagram of the expanded calls | Claude | |
| G1 | Saved views: named, exported to a file and imported again | Claude | |
| G2 | A views file in the output folder is built into the page | Claude | |
| G3 | Printing a view or every view, with descriptions | Claude, then you on a work machine | |
| G4 | Optional: copy a view as text, to paste into a chat | Claude | |
| H1 | Spike: can the tools write modules and queries into a workbook that Excel accepts? | Claude, then you in Excel | |
| H2 | Choosing what to import, with its dependencies, and writing it out as a bundle folder | Claude | |
| H3 | vbadoc imports into a new workbook or a copy of an existing one, if H1 shows it works | Claude | |
| H4 | pqdoc imports into a new workbook or a copy of an existing one, if H1 shows it works | Claude | |

## 1. Questions settled

Agreed on 2026-11-03. The rest of this plan follows these answers.

1. **One executable, the combined page and the VBA-to-query links wait,** in Later. No workbook in use links VBA to
   queries, and few hold both, so they'd cost a lot for little. The shared library and page shell stay, because they
   make every page feature in this plan a single piece of work, and they keep the combined tool cheap if it's wanted
   later. The program's name (`xldoc`) waits with it.
2. **The class diagram is drawn by the page with d3, and Mermaid is removed.** Mermaid lays out the diagram itself, so
   boxes can't be moved; it takes about 20 seconds to draw 300 modules and drops dependencies over 150; and it adds
   3.6 MB to every page. The graph already has dragging, focus and selection to build on.
3. **The execution path is a call tree, not a sequence diagram.** A tree only draws what you open, so it stays readable
   at any size, and it can show the Ifs and loops around each call. A sequence diagram drawn from the open part of the
   tree is an optional extra step (E5). vbadoc's requirements list sequence diagrams as out of scope, so that line
   changes if E5 goes ahead.
4. **Favourites replace Home and the trail.** Any number of favourites, listed at the top of the explorer. The trail
   goes, since it wasn't as useful as expected. Browser Back and Forward stay. Path from Home becomes the path from the
   nearest favourite.
5. **pqdoc's `--format` lists the formats to write, and defaults to `html`,** so `-f csv,html` writes both. `--no-csv`
   goes. pqdoc 1.0.0 was never tagged, so no released behaviour changes.
6. **The tools move to .NET 10 now, in step A0.** .NET 9 support ends on 2026-11-10, and the work machine now has the
   .NET 10 SDK.

## 2. What the feedback says

The notes behind this plan are kept out of the repository. This section is what the plan needs from them.

### Objectives

1. **Understand a workbook** that's new, or not worked on for a while. What matters most is the path execution takes
   for one activity, not everything at once.
2. **Leave documentation** for whoever works on the workbook next. For VBA that's mainly developers, and occasionally
   the workbook's users. For Power Query it's the authors and users of the queries.

### Constraints

- **Developers run the tools on locked-down work machines.** Application control limits where and by whom
  executables can run, Excel add-ins can't be installed, and the only AI help is a chat window with no access to the
  project.
- **Readers only open the page.** It must keep working with nothing installed, no network and no special settings, as it
  does now.
- **Typical size:** tens of modules (some hundreds of lines long) and hundreds of members; tens of queries and
  functions. Pages are responsive. Opening one from a network drive is slow, which is acceptable.
- **Exported source goes into git.** Pages are generated with `--embed-source` while developing and without it for
  committing.
- **The work machine has the .NET 10 SDK but only Visual Studio 2022,** which doesn't support .NET 10. Building,
  testing, running and publishing must all work from the `dotnet` command line, and the READMEs must say how.

### Where each point is answered

| Feedback | Answered by |
|---|---|
| The page is the most useful output; the dependency graph works well; pqdoc's depth columns work really well | Kept as they are |
| A click inside a module, with members showing, pulls the modules together | A1 |
| The export seems slow, and pauses while writing the page | A2 found the cause; F1 fixes it |
| pqdoc should write HTML by default | A3 |
| pqdoc should read queries from the workbook and show their source, as vbadoc does | B1–B4 |
| The two tools' documentation files should be named the same way (raised after B2) | A4 |
| vbadoc should have pqdoc's `--on-exists` and `--force`, and ask before overwriting in the same way (raised after C1) | A5 |
| Search isn't always the best way to find things; a tree, perhaps in a sidebar | D1 |
| The VBA editor can't organise modules into folders | D1 groups modules by `'@Folder` comments |
| The trail is less useful than expected; favourites would help | D2 |
| Too much on screen; some items are used almost everywhere (logging, settings, predeclared classes, Power Query functions) | D3 |
| The panel toggles are far off at the top right, and the explorer's is on the wrong side of the screen (after D1) | D1a |
| The source panel should close when clicking off a node, with a lock to keep it open (after D1) | D1a |
| Switch the explorer between folders and kinds, with folders looking different (after D1) | D1b |
| A workbook with 140 queries in 20 groups, up to about 4 deep, showed no groups (after D1) | D1b |
| Focus gets lost among the toolbar's buttons, Transitive is seldom used, and Ctrl+click could focus (after D1) | D1c |
| The class diagram's legend should pick things out as the graph's does (after D1) | D1c |
| vbadoc's page has no summary like pqdoc's (after D1) | D1c |
| 70 modules with 320 module and 1,540 member dependencies are laggy to scroll and pan, about 5 out of 10 (after D1) | D1d |
| The Show menus belong in the explorer, for every view (after D1) | D3 |
| Uses / Used by lists an item twice; a count isn't useful; links to the exact place would be | E1 |
| What I most want to know: the path execution takes | E2–E4 |
| A sequence diagram, if it can be narrowed to one flow | E5 (optional) |
| The class diagram isn't interactive enough; boxes can't be rearranged | F1 |
| Arrange a view, export it with a printed version, regenerate it after changes | G1–G3 |
| Documentation for future developers and users | G2, G3 |
| AI help is a chat window with no access to the project | G4 (optional) |
| Import chosen files into a new or existing workbook, with what they depend on (raised after the notes) | H1–H4 |
| draw.io? | Later: export a view as a `.drawio` file |
| The VBA editor shows every module in one window | Later: open a module's source in its own window |
| Shared code and conventions drift between workbooks | Later: a third tool, with its own plan |
| UserForms are seldom used | vbadoc's stage 6 moves to Later |
| No workbook links VBA and queries | One executable, combined page and links move to Later |

## 3. Goals and non-goals

**Goals**

1. Move to .NET 10 before .NET 9's support ends, and fix the two problems found: the graph click and the slow export.
2. pqdoc reads queries from a workbook, writes them as `.m` files, and shows their source.
3. Both pages share one shell, so the features below are built once.
4. Find things through a tree, mark favourites, and hide what's used almost everywhere.
5. See the path execution takes: calls in order, with the logic around them, and a query's steps.
6. A class diagram whose boxes can be moved.
7. Save arrangements as named views, keep them in git beside the source, and print them as documentation.
8. Import chosen modules or queries into a new or existing workbook, bringing what they depend on, and saying what
   couldn't come.

**Non-goals**

- Running code, or fully parsing VBA or M. The call tree is built from the code as written, so calls made through
  `Application.Run`, a button's `OnAction` text or late binding can't be followed. The page says so.
- Changing a workbook in place. Importing writes a new workbook, or a changed copy of an existing one, and never
  touches the workbook it reads from.
- Driving Excel itself. Importing works on the files, not through Excel's automation.
- Anything that needs the network, an install or admin rights, on either the developer's machine or a reader's.
- Power BI files (`.pbix`, `.pbit`).
- Re-indenting M, or reading Power Query's Permissions and Permission Bindings sections.

## 4. What it will look like

### The page

Both tools' pages share this layout. Panels can be hidden and resized, as the source panel can now.

```
+------------------------------------------------------------------------------------------+
| ≡ Title [Dependency graph] [Class diagram] [Calls] Find... Views v Print Theme v0.x  </> |
+----------------+--------------------------------------------------+----------------------+
| Filter...      |                                                  | Order.Save           |
| * Favourites   |                                                  | Saves the order...   |
|   Main.Run     |            the selected view                     | Uses / Used by       |
| > Import       |                                                  |   Logger.Write  x3   |
| > Reports      |                                                  |     line 42 ...      |
| v Classes      |                                                  | Source               |
|   > Order      |                                                  |   40  Public Sub ... |
| Widely used (3)|                                                  |                      |
| Hidden (2)     |                                                  |                      |
+----------------+--------------------------------------------------+----------------------+
  explorer                                                            source or details panel
```

- **vbadoc's tabs:** Dependency graph, Class diagram, and the new Calls.
- **pqdoc's tab:** Dependency graph. A query's details gain its source and its steps.
- **≡ at the left end of the top bar shows or hides the explorer, and `</>` at the right end the side panel** (added
  after D1), each beside the panel it opens.

### The Calls tab

Pick an entry point, such as a macro or an event handler, and open the calls you want to follow. Each line links to
the procedure and to the line it's called from.

```
Main.RunImport                                  macro
  Logger.Start                                  line 12
  If Not Settings.IsReady Then                  line 14
    Setup.ShowError                             line 15
  For Each f In files                           line 18
    Import.ReadFile(f)                          line 19   >
    Import.Validate                             line 20   >
  Report.Refresh                                line 24   >
  + 4 calls to hidden items
```

A Calls / Called by switch turns the tree round, to show everything that leads to a procedure.

### Commands

vbadoc gains `--output-name` (A4), and `--on-exists` and `--force` as pqdoc has them (A5). pqdoc's command takes a
workbook as well as a folder, and `--format` lists what to write:

```
vbadoc <workbook> [--output <folder>] [--output-name <name>] [--embed-source] [--on-exists <behaviour>] [--force]
                  [--verbose | --quiet]
pqdoc <workbook or folder> [--output <folder>] [--output-name <name>] [--format <formats>] [--embed-source]
                           [--plantuml-server <url>] [--on-exists <behaviour>] [--force] [--verbose | --quiet]
```

### Output

| Command | Default folder | Contents |
|---|---|---|
| `vbadoc book.xlsm` | `VBAProject`, next to the input | `book-vba.html` (from A4; `index.html` before), the module files, `.gitattributes` |
| `pqdoc book.xlsx` | `PowerQuery`, next to the input | `.m` files, `book-powerquery.html` (from A4), `.gitattributes` |
| `pqdoc <folder>` | The input folder | `<folder>-powerquery.html` (from A4) |

A views file saved in that folder (`vbadoc-views.json` or `pqdoc-views.json`) is built into the page on every run, and
goes into git with the source.

### Repository

```
shared/
  src/ExcelDocumenter.Shared/       workbook access, exit codes, version, source folder writer, page shell, d3
  tests/ExcelDocumenter.Shared.Tests/
vbadoc/                              as now, using ExcelDocumenter.Shared
pqdoc/                               as now, using ExcelDocumenter.Shared
```

The tool libraries use `ExcelDocumenter.Shared` and never each other. `ExcelDocumenter` follows the guidelines' rule that
the long name is the code's identity.

## 5. Decisions

### Order of the work

- **.NET 10 first,** so every later step is built and tested on the runtime it will ship with.
- **Fixes next,** because they're small and independent.
- **The class diagram (F1) comes straight after the fixes,** keeping its letter. A2 found that removing Mermaid also
  fixes the export's pause and the page's slow opening. Favourites and hiding (D2, D3) then apply to the new diagram as
  well as the graph.
- **pqdoc reads workbooks before the shared library exists.** Steps B2–B4 copy vbadoc's approach in parallel code with
  the same names, and C1 then extracts what has really turned out to be duplicated. This follows the guidelines' rule
  of extracting only real duplication.
- **The shell comes before the new page features,** because almost all of them (explorer, favourites, hiding, views,
  printing) belong in both pages. The splitter bug, fixed twice, showed the cost of two copies.
- **vbadoc's overwrite options (A5) come straight after the shell.** They need no page work, and move pqdoc's overwrite
  code into `shared/` while C1's library is fresh. No existing step fits them: C2 allows no new features, and D to H
  are about the page or importing.
- **D1's follow-ups (D1a to D1d) come before favourites (D2),** so favourites and hiding build on the panels and the
  explorer as the user wants them. D1b needs the sample workbook first; speed (D1d) comes last of them, so it's
  measured with their changes in.
- **Finding your way (D) comes before the execution path (E),** because the call tree relies on hiding items and on
  the explorer's list of entry points. If the call tree is wanted sooner, E2 could move before C2 as a vbadoc-only step,
  at the cost of moving it again in C2.
- **Importing (H) comes last,** because it chooses what to import with the explorer (D1) and saved views (G1), and
  needs pqdoc to read workbooks (B).
- **vbadoc's stage 6 (UserForms) moves to Later.** Its stage 7 (polish) comes after this plan.

### Reading queries

- **A hand-written reader in `PowerQueryDocumenter.Core/Extraction/`**, named in parallel with vbadoc's:
  `WorkbookReader` and `ExtractionException`. No new package: base64, `System.IO.Compression` and `System.Xml` are
  enough.
- **The format** ([MS-QDEFF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-qdeff/27b1dd1e-7de8-45d9-9c84-dfcc7a802e37))
  was checked against a real workbook on 2026-10-31. Queries are in a `customXml/item<n>.xml` part holding a base64
  `DataMashup` element. Decoded, it's a 4-byte version, then four length-prefixed sections. The first is a zip whose
  `Formulas/Section1.m` holds every query as `shared <name> = <expression>;`, in UTF-8 with mixed line endings. B1
  confirmed this against the sample workbooks, `.xlsb` included; pqdoc's `DECISIONS.md` has the details.
- **The part is found by what it is, not where it is:** any `customXml/item*.xml` whose root is `DataMashup` in the
  `http://schemas.microsoft.com/DataMashup` namespace. The XML reader takes the encoding from the byte order mark, with
  DTD processing off.
- **Every length is checked against the bytes left.** Anything that doesn't fit is reported as a corrupt workbook, exit
  code 3, and nothing is read past the end.
- **`Section1.m` is split with pqdoc's tokeniser, not a regular expression,** because a text literal can hold a `;` or a
  line starting `shared`. A member without `shared` is still kept as a query.
- **A description is an attribute record before the member** (`[ Description = "…" ] shared Name = …`), not
  metadata, as B1 found. The splitter takes it off the expression and keeps it for the page.
- **Query groups come from the metadata's `QueryGroups` entry,** whose binary layout B1 worked out from the samples.
  A list that doesn't parse means no groups, not an error.
- **A section that can't be split stops the run,** with exit code 3, because carrying on would delete that query's
  `.m` file. This is vbadoc's rule for an unreadable module.
- **The file is opened for shared reading,** so it works while the workbook is open in Excel.
- **Permissions, Permission Bindings and embedded content are skipped, never read or written out.**

### `.m` files

- **Each file holds the expression as Excel stores it,** without `shared <name> =` or the final `;`, which is what a
  manual export contains, so pqdoc reads both the same way.
- **CRLF line endings outside text literals, and UTF-8 with no byte order mark,** matching vbadoc's output. Line
  breaks inside a text literal stay as Excel stored them, so the text is unchanged if the query is imported back
  (agreed after B1).
- **File names can be turned back into query names.** Query names can contain `/ \ : * ? < > |`. Those characters and
  `%` itself become `%XX`, so `Sales/Region` is `Sales%2FRegion.m`, and a reserved name such as `CON` is written
  `%43ON.m`. Reading a folder reverses exactly those escapes.
- **Kept in step like vbadoc's module files:** unchanged files aren't rewritten, top-level `.m` files no longer in the
  workbook are deleted, other files are left alone, and a `.gitattributes` is created if there isn't one. So
  `--on-exists` doesn't apply to `.m` files.
- **Names that clash stop the run** with exit code 3. Query names are case-insensitive.

### Privacy

- **M source is more sensitive than query names.** It can hold server names and file paths. Source isn't embedded by
  default, the README says so, and source never goes to a PlantUML server.
- **Test workbooks hold made-up data only.** `vbadoc/private/` is never committed, and nothing from it (names, code or
  the workbook's name) goes into a test, a fixture, a doc or a commit message.
- **Copying a view as text (G4) only puts it on the clipboard.** Where it goes from there is the user's choice.

### Shared library and shell

- **`shared/` holds only what is duplicated or needed by both.** It changes both tools' build stamps, like the root build
  files.
- **One shell, used by both pages.** It owns the top bar, tabs, theme, settings storage, explorer, side panel, splitters,
  search, Load folder, source view, favourites, hiding, views and printing. A tool adds its views and fills the
  explorer.
- **Each tool keeps its own global, data block and storage keys** (`window.vbadoc`, `vbadoc-data`, `vbadoc-…`, and the
  same for pqdoc), so existing views barely change.
- **The shell and views talk through a small interface,** written down at the top of the shell's script, as `page.js`
  does today.

### Finding your way

- **The explorer is a panel on the left.** It can be hidden and resized, and it's open by default on wide screens. Its
  filter box narrows the tree. Search stays for jumping straight to a name.
  - **vbadoc:** modules grouped by `'@Folder("Parent.Child")` comments if any module has one (the convention Rubberduck
    uses, which works as a plain comment), otherwise by kind: standard modules, classes, interfaces, forms and document
    modules. Members are listed under their module in source order, with their kind, role and unreferenced marker.
  - **pqdoc:** queries grouped by their Excel query group when the workbook has groups, otherwise into queries and
    functions.
- **Favourites** are starred from the explorer or the panel header, and listed at the top of the explorer. They're
  remembered per workbook in the browser, as Home is now. A favourite that no longer exists is ignored. They replace
  Home; the trail goes; Back and Forward stay; Path from Home becomes the path from the nearest favourite.
- **Hiding** takes an item and its edges out of every diagram and the call tree. A Hidden group in the explorer lists
  them, to bring them back. Nothing disappears silently: the panel says when the selection uses hidden items, and the
  call tree counts them.
- **Widely used items** are worked out in Core: those used by at least a set share of the other modules or queries,
  with the share in the page's settings. They're marked in the explorer and listed in a Widely used group with a Hide
  all button. **Nothing is hidden by default**, so the first picture is complete until the user chooses otherwise.
- **Classes with `VB_PredeclaredId = True` are labelled "predeclared",** since they're used like standard modules.
- **Focus can take the favourites as well as the selection,** showing them and what they use.

### After D1's review

The user tried D1 on real workbooks; these follow from what they asked for.

- **The panel toggles are icons at the two ends of the top bar,** each beside the panel it opens: three bars at the
  left for the explorer, `</>` at the right for the side panel. Their names show in a tooltip and are their
  `aria-label`s. *To settle in review:* the user suggested Font Awesome's `fa-bars` and `fa-code`. Its free icons are
  CC BY 4.0, a licence the guidelines don't list, which also asks for attribution in every page; drawing two such simple
  icons as inline SVG avoids both, so that's the recommendation.
- **The side panel follows the selection unless locked:** it opens when something is selected and closes when the
  selection is cleared, so clicking off a node gives the view the room. A lock in its header keeps it open, remembered
  per tool. The toggle still opens and closes it by hand.
- **The explorer has two layouts, folders or kinds,** chosen with a switch at its top and remembered per tool: in
  vbadoc, `'@Folder` folders or standard modules, classes, interfaces, forms and document modules; in pqdoc, Excel's
  query groups or queries and functions. Folders are the default when there are any. Folders and groups have a folder
  icon, so the two layouts look different at a glance.
- **pqdoc reads nested query groups to any depth.** D1 rejected a group list in a layout it didn't know, so a workbook
  with nested groups showed none. Working out the nested layout needs a sample workbook with nested groups. A list that
  still can't be read leaves the queries ungrouped, as now, but the run now says so in a warning.
- **The Show menus move into the explorer** (in D3, with hiding): one set of filters, applied to every view (vbadoc's
  graph and class diagram, pqdoc's graph) and to the tree, so what's shown is chosen in one place. The views' own Show
  buttons go.
- **Ctrl+click on a node or box focuses on it** (Cmd+click on a Mac): it selects it and shows only it and what it's
  connected to, as Focus does. Focus moves to the start of the toolbar, where it isn't lost among the other buttons.
  *To settle in review:* pqdoc's graph has no Focus; the recommendation is to give it one, showing the query's
  upstream and downstream chains, so Ctrl+click works the same in every view.
- **Direct / Transitive moves into the graph's Settings,** since it's seldom changed.
- **The class diagram's legend picks things out as the graph's does:** clicking a kind or a relationship lights up the
  boxes and lines that match and fades the rest; items add to each other, and Clear turns them off.
- **vbadoc's page gets a summary like pqdoc's** in the top bar, as a shell slot both tools use: modules, members, and
  module and member dependencies. *To settle in review:* the user asked for when the page was generated too, but
  vbadoc's page must be deterministic (its requirements say no timestamps, so git diffs show only real changes), and a
  generation time would also make A5 ask before overwriting on every run. The recommendation is to show when the
  workbook was last saved instead, from its own document properties when it has them, which changes only when the
  workbook does.
- **Large projects stay smooth to pan and scroll.** The user's example (70 modules, 320 module and 1,540 member
  dependencies) is about 5 out of 10. The causes are measured, not guessed, on a generated project of that size, in
  headless Chromium with performance traces: frame times while panning the graph at both levels, scrolling the
  explorer and the source panel, and moving the pointer over the graph. Candidates to check: the number of SVG elements
  and labels at member level, work done on every zoom, tick or hover, CSS that makes the browser repaint large areas,
  and the explorer listing every member. A browser test keeps a budget, so later steps don't slow it again.

### References and the execution path

- **Core records every reference with its line** (from, to, kind, line), not only a count per edge. Edge counts are
  worked out from these. pqdoc does the same with the tokeniser's positions.
- **Uses / Used by lists each item once,** with its kinds as tags (Implements, Uses ×3, WithEvents). Opening an item
  lists each place it's referenced: the line number and, when the source is available, the line's text. Clicking one
  selects the member, scrolls the source panel to that line and highlights it. Without source, line numbers still show.
- **The Calls tab (vbadoc)** starts from a list of entry points (the existing roles: macros, event handlers, worksheet
  functions), plus the favourites.
  - The tree holds calls to procedures and properties in the order they appear in the code. Uses of variables and
    constants stay in Uses / Used by.
  - Only opened nodes are drawn, so size doesn't matter.
  - A procedure already open higher up the same branch shows as recursion (↻) and doesn't open again.
  - Hidden items are left out and counted. Late-bound calls show as leaves (`CreateObject(…).Run`), so the gaps are
    visible, and a note says the tree can't see `Application.Run` or `OnAction`.
  - Calls / Called by turns the tree round.
- **The logic around each call (E3)** comes from the parser's statement list. Block statements become rows in the tree:
  `If`/`ElseIf`/`Else` with the condition, `Select Case` and each `Case`, `For`/`For Each`/`Do`/`While` loops, `With`,
  and `On Error` handlers. Condition text is cut to about 60 characters. A block with no calls in it isn't shown.
- **A query's steps (E4, pqdoc)** are its `let` steps in order, each with the queries and functions it uses and a link
  to its line, then the `in` expression. A query without `let` shows only what it uses. The structural pass already
  finds step names; it now keeps them.
- **The optional sequence diagram (E5)** is drawn by the page from whatever is open in the Calls tree: modules as
  lifelines, calls as messages in order, and E3's blocks as frames. Narrowing the tree narrows the diagram, which is what
  the earlier attempt lacked. It uses d3, not Mermaid.

### Class diagram

- **Drawn by the page with d3**, using the same box contents, relationships and notation as now. Boxes can be dragged
  and their positions are remembered per workbook and saved in views. Hiding, favourites, focus and selection work as
  in the graph.
- **The first picture is always the same:** the layout is computed before drawing, in layers. As built in F1, users
  sit above what they use (so interfaces sit below their implementations, keeping every arrow pointing down), modules
  in a cycle share a layer, and rows are tidied by moving boxes towards their neighbours rather than with d3's force
  layout. No new library was needed. vbadoc's `DECISIONS.md` has the details.
- **Mermaid is removed,** along with its 150-dependency limit and its escaping rules. The C# `ClassDiagram` model stays.

### Documentation file names (A4)

- **Each documentation file is named after its workbook, with a suffix for what it documents:** `<workbook>-vba.html`
  from vbadoc, and `<workbook>-powerquery.html` (and `.csv`, `.puml`, `.svg`, `.png`, `.pdf`) from pqdoc. For a folder
  of `.m` files, the folder's name stands in for the workbook's. A file that's emailed, attached, downloaded or printed
  loses its folder, so its own name has to say what it is. The suffix stops the two tools' pages for one workbook
  clashing when they travel together.
- **`--output-name` overrides the name in both tools.** vbadoc gains it; pqdoc keeps its own.
- **A renamed workbook doesn't leave its old page behind.** Each tool deletes its own older pages from a folder it
  owns (vbadoc's output folder, pqdoc's `PowerQuery` folder), recognising them by their generator tag, and says so.
  In a folder of `.m` files, which belongs to the user, pqdoc only mentions them. This also clears away the old
  `index.html` and `powerquery-dependencies.*` names on the first run.
- **`#` and `%` become `_` in the file name only,** because they break or confuse a browser's address, and vbadoc uses
  the address's `#` part for Back and Forward.
- **Settings files keep fixed names** (`vbadoc-views.json`, `pqdoc-views.json`), so renaming a workbook doesn't lose
  its saved views.

### Views and printing

- **A view** has a name, a description written by the user, and: the tab, the focus set, the hidden items, the Show…
  choices, the positions of moved nodes and boxes, and the Calls tree's starting point and open nodes. Favourites stay
  personal and aren't part of a view.
- **The page keeps the current arrangement in the browser,** as now. The Views menu saves it as a view, and switches,
  renames and deletes views.
- **Export** downloads the views as `vbadoc-views.json` or `pqdoc-views.json`, and **Import** reads one back with a file
  picker. Both work offline from a page opened from disk.
- **The views file in the output folder is built into the page** on every run, so readers get the views just by opening
  the page, and the file is versioned in git with the source. Views that mention items no longer in the workbook still
  load. The missing items are listed on the console and in the page, not treated as errors.
- **The file is JSON with a format version,** uses the page's ids (names), and is written with sorted keys and 2-space
  indentation, so diffs are small.
- **Printing** uses a print stylesheet. Print gives the current view or every view as a document: the title, workbook,
  build stamp and date, then for each view its description, its diagram fitted to the page, and its items with kind,
  signature, description and Uses / Used by. Source is optional. The browser's Print, then Save as PDF, makes the PDF,
  so no other software is needed.
- **Descriptions** come from a member's `VB_Description` attribute if it has one, otherwise from the comments directly
  above it (already part of its source), and from a module's comments at the top. For queries, from the description in
  the workbook if B1 finds one is stored, otherwise from comments at the top of the query. They show in the panel too.

### Importing into a workbook

H1 is a spike, so these are the starting position. H1's findings confirm or change them before H2 begins.

- **What to import is chosen in the page,** by ticking items in the explorer or saving them as a view, and the views
  file carries the choice to the command line. The command also takes names directly.
- **Dependencies come along automatically.** Core follows what the chosen items use, transitively: Uses, Implements and
  Events edges for VBA, and query references for Power Query (functions and parameters are queries, so they come too).
  Before anything is written, the page and the command show the full list: what was chosen, and what it brings.
- **What can't come along is listed, never dropped silently:**
  - VBA: document modules (`ThisWorkbook` and sheets) and code that relies on particular sheets or their controls;
    library references (Tools > References); anything reached only through strings (`Application.Run`, `OnAction`) or
    late binding; UserForms, which can't be imported from text because the export has no `.frx`.
  - Power Query: data sources and their credentials and privacy levels, and tables or named ranges that
    `Excel.CurrentWorkbook()` reads.
- **A bundle folder always works.** It holds the chosen files and their dependencies, with a `README.md` listing what
  the target also needs and how to import by hand: File > Import in the VBA editor, or a blank query per `.m` file,
  named after it, in dependency order. It needs nothing installed or switched on.
- **Writing straight into a workbook happens only if H1 shows it's reliable:** VBA by rewriting `vbaProject.bin` with
  source only and no compiled code, so Excel compiles it on opening; Power Query by rewriting the `DataMashup` part and
  whatever else Excel needs. Not through Excel's automation, which needs "Trust access to the VBA project object
  model", often turned off on locked-down machines.
- **The workbook read is never changed.** Importing into an existing workbook writes a new file. A new workbook starts
  from a blank template made in Excel and embedded in the tool.
- **Name clashes:** an item already in the target with identical text is skipped. One that differs stops the run with
  exit code 4, unless the user says to replace it.
- **Signed VBA projects:** a change breaks the signature, so the tool refuses unless told to go ahead.
- **Macro security:** a workbook written by the tool is unsigned. Machines that only run signed macros, or macros from
  trusted locations, block it until it's signed or moved. The README says so.

## 6. Steps

### Before step A0

- In `vbadoc/docs/PLAN.md`, move stage 6 to a Later section and note that stage 7 waits for this plan. In
  `pqdoc/docs/PLAN.md`, replace the Later section with a pointer here. Update `docs/WORKLOG.md`.

### Step A0: .NET 10

- Change the target framework in the root `Directory.Build.props` and the SDK in `global.json`. Update the build
  workflow's path to the Chromium installer, which names the framework, and Dependabot's comment about the SDK.
- Fix any new warnings from the .NET 10 SDK, such as analysers or NuGet's audit of indirect packages.
- Give the root `README.md` command-line instructions for building, testing, running and publishing, and say that
  Visual Studio 2022 can't build .NET 10 projects. Update the SDK version in every README, the guidelines, vbadoc's
  requirements and both tools' decisions.
- Acceptance: the solution builds and every test passes. A published executable of each tool runs from a folder of its
  own. Pages and files generated from the fixtures before and after are byte-identical apart from the build stamp.
  You then build, test and publish on a work machine from the command line.

### Step A1: Graph click

- The likely cause: d3's drag `start` event fires as soon as the button goes down, and its handler pins the nodes and
  reheats the simulation (`alphaTarget(0.3)`) before the pointer has moved, so every click runs the forces briefly.
  Move the pinning and reheating to the first `drag` event past the click distance.
- Check pqdoc's graph for the same pattern.
- Browser test: clicking a module outline, and a node, with members showing leaves every node where it was.

### Step A2: Export speed

- Time each phase (reading, parsing, analysing, building the page, writing) and show the times with `--verbose`.
  Measure the private workbook from a local disk and from a network drive.
- Fix what's slow. The page currently carries 3.6 MB of Mermaid. If that's the cost, record it and leave it to F1.
- Record the timings before and after in vbadoc's `DECISIONS.md`. The output must not change.

### Step A3: pqdoc writes HTML by default

- `--format` lists the formats to write and defaults to `html`. `--no-csv` goes.
- Update the tests, README and requirements.

### Step F1: Class diagram

Moved up from after E5, keeping its letter, because it also fixes the export's pause (see A2).

- Rebuild `classes.js` on d3, as section 5 describes, with dragging, remembered positions, focus and selection.
  Favourites and hiding come to it in D2 and D3, with the graph.
- Remove Mermaid from `Resources/Vendor/`, the page and the guidelines' list of dependencies. A2 found this is also
  the fix for the export's pause, and for the page opening slowly: antivirus scanning of the page took about 3 s with
  Mermaid and 0.2 s without.
- Acceptance: for the fixtures and the 300-module test project, every box, member and relationship the Mermaid version
  showed is still shown, and clicks select as before. Look at screenshots in both themes. Re-measure the export with
  `--verbose`, and the first read of a newly written page, and record both in vbadoc's `DECISIONS.md`.

### Step B1: Sample workbooks

Excel has to make these, so this step needs you. They go in a public repository, so use made-up data only. Save them in
`pqdoc/tests/PowerQueryDocumenter.Tests/Fixtures/Workbooks/`:

| File | What it needs |
|---|---|
| `Queries.xlsx` | About eight queries: a chain (A feeds B feeds C); a name with spaces (`Sales Data`); a name with `/` or `:` and one with `%`, if Excel accepts them; a function used by several queries; a parameter; an Enter Data table; a query with a text value containing `;` and a line starting `shared`; one with `//` and `/* */` comments, including on its last line. Load one to a sheet, one to the data model, and leave one connection only. Put some in two query groups, and give one a description |
| `Queries.xlsb` | One or two queries, to confirm binary workbooks store them the same way |

Then Claude checks each with a throwaway script, and records in `pqdoc/docs/DECISIONS.md` what's confirmed: the part's
encoding, which characters Excel accepts in a name, whether any member lacks `shared`, that `.xlsb` matches, and where
query groups and descriptions are stored. Anything that disagrees with section 5 is raised before B2. Add a `README.md`
to the fixture folder saying what each file is for.

### Step B2: pqdoc reads a workbook

- Add `Extraction/` to `PowerQueryDocumenter.Core`: open the file, find the part, decode it, take `Section1.m` and split
  it into the existing `QuerySource` records. The analyser doesn't change.
- `<input_folder>` becomes `<input>`, a workbook or a folder. A workbook's default output folder is `PowerQuery` beside it.
- Clear messages, each with exit code 3, for: no Power Query in the workbook; an encrypted workbook; a corrupt part; a
  file that isn't a workbook.
- Tests use the samples, plus a small helper that builds `DataMashup` bytes, for damaged lengths and bad base64.
- Acceptance: the graph from `Queries.xlsx` matches the graph from the same queries exported by hand. The private
  workbook gives the same result as its hand-exported query (checked by hand, not a test).

### Step A4: Documentation file names

Added after B2, and done before B3 so the `PowerQuery` folder gets its final names straight away.

- Name the documentation files as section 5 describes, in both tools. vbadoc gains `--output-name`, and the
  `.gitattributes` it creates covers `*.html` rather than `index.html`.
- Delete each tool's older pages from a folder it owns, and only mention them in a folder of `.m` files.
- Tests: names from a workbook and from a folder, `#` and `%`, `--output-name`, an old `index.html` and an older
  workbook-named page cleared away, a page from the other tool or a hand-written `.html` left alone.
- Update both tools' requirements, decisions and READMEs, and note the change for anyone who bookmarked
  `index.html`.

### Step B3: pqdoc writes `.m` files

- Add `Output/QueryFileExporter`, parallel to vbadoc's `ProjectExporter`, following section 5. The folder reader decodes
  file names, so the round trip works.
- Tests: names needing escapes, reserved names, case clashes, stale files, unchanged files keeping their timestamps, an
  existing `.gitattributes`, and a run stopped by a bad section leaving existing files alone.
- Acceptance: run pqdoc on `Queries.xlsx`, then on the folder it wrote, and get the same graph with the same names.

### Step B4: pqdoc's page shows source

- Add `m-highlight.js`, parallel to `vba-highlight.js`: comments, text, `#"quoted names"`, keywords, numbers,
  `#date`-style constructors and type names. Enter Data queries have very long lines, so test that one doesn't slow the
  page down.
- Add a Source section to the details panel, with line numbers.
- Add Load folder and `--embed-source`, as vbadoc does, with a hash of each file so the page warns when a loaded folder
  no longer matches. With a folder as input, `--embed-source` uses the files in that folder.
- Reuse vbadoc's syntax colour tokens, and check them with the palette validator.
- Browser tests, including that no M code is in the page without `--embed-source`. Look at screenshots in both themes.

### Step C1: Shared library

- Create `shared/` as in section 4, and move in one copy of each of:
  - opening a workbook, telling zip from compound file, and spotting encryption, from vbadoc's `WorkbookReader`;
  - `ExitCodes` and `ToolVersion`;
  - `EmbeddedResources` and the page-building helpers (filling placeholders, inlining scripts, escaping);
  - the source folder writer that `ProjectExporter` and `QueryFileExporter` now both contain;
  - `AtomicFileWriter`, which vbadoc's writes then use too;
  - `d3.min.js` with its licence.
- pqdoc picks up OpenMcdf through `shared/` (MPL-2.0, unmodified, so allowed).
- Add `shared/` to the stamp paths in `Directory.Build.targets`, and update the guidelines' "no shared code project"
  rule.
- Acceptance: pages and files generated before and after are byte-identical apart from the build stamp. Save reference
  output from the fixtures before starting.

### Step C2: Shared page shell

This is the largest step and the likeliest to drift, so keep to it strictly: no new features.

- Move the parts the shell owns today (top bar, tabs, theme, settings storage, side panel, splitter, search, Load
  folder, source view) into `shared/`'s page resources. Each tool's own files keep only its views.
- Merge the CSS tokens. Identical ones move as they are; go through the rest one at a time, and re-run the palette
  validator for any colour that changes.
- Write the shell's interface at the top of its script.
- Acceptance: both pages look and work as before, and every browser test passes, changed only where an id was renamed.
  Compare screenshots in both themes.

### Step A5: vbadoc asks before overwriting its page

Added after C1, at the user's request, to bring vbadoc into line with pqdoc. Done after C2.

- vbadoc gains `--on-exists prompt|overwrite|skip|fail` and `--force` (`-y`), with pqdoc's rules: the default asks
  when the session is interactive and fails when it isn't; `--force` means `--on-exists overwrite` and conflicts with
  any other `--on-exists`; the answers are yes, no, all and quit, asked on standard error; a refusal exits with code 4
  and writes nothing.
- It applies to the page, the only output vbadoc writes that the user might have changed. As with pqdoc's `.m` files,
  the module files and `.gitattributes` follow the workbook and are never asked about, and are written only after the
  check, so quitting at the prompt changes nothing on disk. Older pages vbadoc deletes from its own folder aren't asked
  about either.
- A page that would be written byte-for-byte unchanged isn't asked about, as today it isn't rewritten. Re-running
  vbadoc on an unchanged workbook therefore never asks.
- Move pqdoc's overwrite code (`OnExistsBehaviour`, `OverwriteResolver`, `IUserInteraction`, the console prompt, and
  parsing and checking the two options) into `shared/`, and use it in both tools. pqdoc's behaviour doesn't change.
- Tests, mirroring pqdoc's: each behaviour, a non-interactive run refusing with exit code 4 and writing nothing, the
  prompt's answers, quitting, `--force` conflicting with `--on-exists`, and an unchanged page not asked about.
- Update vbadoc's requirements, decisions and README, including a note that a script that runs vbadoc again needs
  `--force`. The guidelines list both options among the shared ones.

### Step D1: Explorer

- Add the explorer to the shell, and fill it from each tool, as section 5 describes, including `'@Folder` grouping and
  query groups.
- Selecting in the explorer selects in every view, and the explorer follows selections made elsewhere.
- Browser tests in both pages, including a workbook with `'@Folder` comments and one without. Look at screenshots.

### Step D1a: Panel controls

Added after D1, from the user's review, as are D1b to D1d.

- Move the explorer's toggle to the left end of the top bar and the side panel's to the right end, as icons with
  tooltips and `aria-label`s (section 5), without their text.
- The side panel opens on a selection and closes when it's cleared, unless locked. Add the lock to its header, remembered
  per tool. A page opened with nothing selected starts with the panel closed, unless locked.
- Update the browser tests that open, close or measure the panel, and add ones for following the selection and the
  lock. Screenshots in both themes.

### Step D1b: Explorer layouts and nested query groups

- **You in Excel:** add nested query groups to a copy of `Queries.xlsx` (for example Staging > Raw > Deep, a group with a
  description, and an empty group), and save it as a new sample with made-up data, as in B1.
- Work out how nested groups are stored, record it in pqdoc's decisions, and read groups to any depth. When they can't
  be read, warn and carry on without them.
- Add the layout switch to the explorer in both tools, and the folder icon.
- Tests: nested groups from the new sample; the switch in both pages. You check the workbook with 20 groups on a work
  machine.

### Step D1c: View controls and vbadoc's summary

- Ctrl+click (Cmd+click) focuses on a node in vbadoc's graph, a box in its class diagram and, with its new Focus, a query
  in pqdoc's graph. Focus first in each toolbar; Direct / Transitive into the graph's Settings.
- The class diagram's legend items light up what they stand for, like the graph's.
- vbadoc's summary in the top bar, in a shell slot both tools use (section 5 for what it shows).
- Browser tests for each, and screenshots.

### Step D1d: Speed on large projects

- Generate a project the size of the user's, and measure as section 5 describes, before and after each change.
- Fix what the measurements show, keeping the pages' behaviour.
- Add the budget test. You compare the production workbook's page on a work machine.

### Step D2: Favourites

- Add favourites to the shell: the star in the explorer and the panel header, and the Favourites group.
- Remove Home and the trail from vbadoc. Path from Home becomes the path from the nearest favourite. Ask before
  migrating a remembered Home into a favourite; the simple answer is to ignore it.
- Update the browser tests for Home, the trail and Path from Home.

### Step D3: Hiding and widely used items

- Core works out widely used items and predeclared classes, and puts them in the page data. Unit tests with the
  fixtures.
- Add hiding to the shell, applied by every view: graph, class diagram and (later) the Calls tab.
- Add the Widely used and Hidden groups, the share setting, the panel note about hidden items, and Focus on favourites.
- Move the Show menus into the explorer (added after D1): one set of filters for every view and the tree, replacing
  the views' Show buttons.
- Browser tests: hiding an item removes it and its edges, persists across reloads, and comes back from the Hidden group;
  a filter set in the explorer applies to every view.

### Step E1: Uses / Used by with references

- Record each reference's line in vbadoc's `DependencyAnalyser` and pqdoc's analyser, and put them in the page data.
  Unit tests assert the lines for the fixtures.
- Rebuild the Uses / Used by list as section 5 describes, in the shell, for both tools.
- The source view gains scrolling to a line and highlighting it.
- Browser tests: an item referenced in two ways appears once; clicking a reference highlights the right line, from
  embedded source and from a loaded folder.

### Step E2: Calls tab

- Add the Calls tab to vbadoc, as section 5 describes.
- Build the call tree's data in Core from E1's references, in source order. Unit tests with SampleVba, including
  recursion and calls through an interface.
- Browser tests: opening nodes, recursion, hidden items counted, Calls / Called by, links to procedures and call lines.

### Step E3: The logic around each call

- Teach the parser to track block statements across a procedure's statement list (including single-line `If`), and
  record each call's enclosing blocks.
- Show them as rows in the Calls tree.
- Unit tests for nesting, `ElseIf` chains, `Select Case`, single-line `If`, and blocks under `#If`. Add cases to
  `SampleVbaExtras` where needed.

### Step E4: A query's steps

- Keep the `let` steps the structural pass already finds, with their lines and the queries each uses.
- Show them in pqdoc's details panel, above the source.
- Unit tests with the B1 samples, including nested `let`s, which show only the outer steps.

### Step E5: Sequence diagram (optional)

This can be dropped without affecting anything after it.

- Draw the open part of the Calls tree as a sequence diagram, as section 5 describes, in a switch on the Calls tab.
- Browser tests, and screenshots in both themes.

### Step G1: Saved views

- Add the Views menu, Export and Import to the shell, as section 5 describes. Each view records what its tool's views
  need through the shell's interface.
- Browser tests: save, switch, rename, delete, export and import a view, and import a file with missing items.

### Step G2: Views file built into the page

- Both tools read `vbadoc-views.json` or `pqdoc-views.json` from the output folder, check its format version, and build
  it into the page. A missing file is normal; an unreadable one is a warning, not a failure.
- vbadoc's exporter already leaves files other than module files alone. Check that pqdoc's `.m` exporter does too.
- Tests: a views file with missing items gives console warnings and a page that still loads.

### Step G3: Printing and descriptions

- Add descriptions to the page data, in Core, as section 5 describes, and show them in the panel.
- Add the print stylesheet and the Print menu (current view, every view, with or without source).
- Browser tests that print to PDF in headless Chromium and check the PDF has a page per view.
- You check printing to PDF and exporting views in the browser on a work machine.

### Step G4: Copy a view as text (optional)

This can be dropped without affecting anything else.

- A Copy button puts the current view on the clipboard as Markdown: its items with signatures and descriptions, the open
  Calls tree, and optionally the source. It warns when the text is long. If the browser refuses the clipboard, the text
  appears in a box to copy by hand.

### Step H1: Spike on writing into a workbook

Throwaway code, kept out of the tools. The findings go in each tool's `DECISIONS.md`.

- **VBA:** write a module, as source only with no compiled code, into the `vbaProject.bin` of a copy of a sample
  workbook. Check that Excel opens it, compiles and runs the module, and keeps it after saving. Do the same with a new
  workbook made from a blank template.
- **Power Query:** add two queries, one using the other, to the `DataMashup` part of a copy of `Queries.xlsx`. Check
  that Excel lists them, refreshes them and can load them. Find which other parts (connections, Permission Bindings)
  have to change, and whether a new workbook works the same way.
- You open the results in Excel at home and on a work machine, since macro settings there may differ.
- Recommend, for each tool, whether H3 and H4 write straight into workbooks or stop at bundles. Settle the command's
  shape (for example an `import` subcommand), how the new file is named, and whether `.xlsb` is supported.

### Step H2: Choosing what to import, and bundles

- Core works out the full set for a choice of items, as section 5 describes, with what can't come along and why. Unit
  tests on the fixtures: interfaces, events, Enums and Types in standard modules, cycles, Power Query functions and
  parameters.
- The page shows the full set for what's ticked in the explorer or saved in a view, before anything is exported.
- The command writes a bundle folder for a view or a list of names, with its `README.md`.
- Acceptance: importing a bundle by hand into a blank workbook gives a project that compiles, and queries that refresh,
  for the fixtures.

### Steps H3 and H4: Importing into a workbook

Only if H1 recommends it. Otherwise the bundle from H2 is the import, and these steps are dropped.

- H3 (vbadoc) and H4 (pqdoc) write H2's set into a new workbook, or into a new copy of an existing one, following
  section 5 and H1's findings.
- Tests: a new workbook, a copy of an existing one, identical and differing name clashes, a signed project refused,
  and the workbook read left byte-for-byte unchanged.
- You check the results in Excel at home and on a work machine.

## 7. Changes to existing documents

The rule is to ask before changing a tool's `Requirements.md` or `DECISIONS.md`, or going against the guidelines.
Approving this plan covers the changes below. Anything else still needs asking.

| Document | Change | Steps |
|---|---|---|
| `vbadoc/docs/PLAN.md`, `pqdoc/docs/PLAN.md` | Stage 6 to Later; pqdoc's Later section replaced by a pointer here | Before A0 |
| `docs/GUIDELINES.md`, `vbadoc/Requirements.md`, both `DECISIONS.md`s, every `README.md` | .NET 10 in place of .NET 9 | A0 |
| `pqdoc/Requirements.md` | `--format` and `--no-csv`; workbook input, `<input>`, `--embed-source`, new requirement ids for reading workbooks and writing `.m` files; remove `.xlsx` from "Parsing queries directly from…" in section 8; steps | A3, B2–B4, E4 |
| `pqdoc/docs/DECISIONS.md` | Reading, `.m` files and source; reword "the query name is the file name" for escapes | B1–B4 |
| `vbadoc/Requirements.md` | Explorer, favourites in place of Home and the trail, hiding, references, the Calls tab, views and printing. The class diagram section without Mermaid. Sequence diagrams out of the Out of scope list if E5 goes ahead | D1–G3 |
| `vbadoc/docs/DECISIONS.md` | Home and trail decisions replaced; class diagram decisions replaced; new decisions for each step | A2, D1–G3 |
| `docs/GUIDELINES.md` | Structure: `shared/` and what belongs there | C1 |
| `vbadoc/Requirements.md`, `vbadoc/docs/DECISIONS.md`, `vbadoc/README.md`, `docs/GUIDELINES.md` | `--on-exists` and `--force`; asking before overwriting the page; exit code 4 | A5 |
| `docs/GUIDELINES.md` | HTML pages: the shell, the explorer, views files | C2, G2 |
| `docs/GUIDELINES.md` | Dependencies: Mermaid removed | F1 |
| Both `Requirements.md`s, `DECISIONS.md`s and `README.md`s | Documentation file names; vbadoc's `--output-name` | A4 |
| Both `Requirements.md`s and `DECISIONS.md`s | Importing: what's chosen, dependencies, bundles, writing workbooks | H1–H4 |
| Both `README.md`s | Usage and features | A3, B2–H4 |
| Both `Requirements.md`s (pqdoc's FR-HTML-11 and new ids), both `DECISIONS.md`s and `README.md`s, `docs/GUIDELINES.md` (HTML pages) | Panel controls, the side panel following the selection, explorer layouts, nested query groups, the views' controls, vbadoc's summary, speed, the Show menus in the explorer | D1a–D1d, D3 |

## 8. When the plan is finished

After the last step is reviewed, check that every decision in section 5 is in `docs/GUIDELINES.md` or a tool's
`docs/DECISIONS.md`, move section 9 into the relevant `docs/PLAN.md`, then **delete this file** in its own commit. It stays
in the repository's history. vbadoc's stage 7 is next after that.

## 9. Later

Left out to keep this plan to what the feedback asked for:

- **One executable** (`xldoc vba`, `xldoc pq`), a root command that documents a whole workbook on one page, and links
  from VBA to the queries it refreshes. Worth revisiting if workbooks holding both become common. The combined tool plan
  in history has the design.
- **vbadoc's stage 6:** UserForms.
- **Export a view as a `.drawio` file,** if draw.io turns out to be available at work.
- **Open a module's source in its own window,** since the VBA editor keeps every module in one.
- **A third tool for VBA shared between workbooks:** show where shared modules have drifted apart between exported
  folders, and check code conventions across one or more workbooks. It needs its own plan. The case to design for is
  working on one workbook at a time, sometimes changing a shared module's interface, and coming back later to fix (or
  leave) the other workbooks.
- **Show where a query loads to** (sheet, table, data model or connection only). Leave out values that change on refresh,
  such as `FillLastUpdated` and `FillCount`, or every refresh changes the page.
- **Make names inside the source view links,** in both tools.
- **Read Power BI files.**
