# ExcelDocumentationTools: feedback plan

This plan acts on the first round of feedback from using vbadoc and pqdoc on real workbooks. It replaces the combined
tool plan (`COMBINED-TOOL-PLAN.md`, in the repository's history). pqdoc still learns to read workbooks, and the two
pages still move onto one shared shell. One executable, a combined page and links from VBA to queries move to
[Later](#9-later), because the workbooks in use rarely hold both and never link them. Most of the plan is about
helping someone understand a workbook and leave documentation behind. It is written to be handed to a fresh Claude
Code session (or a developer) to carry out.

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
| A1 | Fix: a click in the graph pulls modules together | Claude | |
| A2 | Fix: the export pauses while writing the page | Claude | |
| A3 | pqdoc writes HTML by default | Claude | |
| B1 | Sample workbooks with Power Query, and confirming the format against them | You in Excel, then Claude | |
| B2 | pqdoc reads queries from a workbook | Claude | |
| B3 | pqdoc writes `.m` files, kept in step with the workbook | Claude | |
| B4 | pqdoc's page shows highlighted M source | Claude | |
| C1 | Shared library: `shared/`, with the C# code both tools now duplicate | Claude | |
| C2 | Shared page shell: both pages rebuilt on it, with no visible change | Claude | |
| D1 | Explorer: a tree of everything in the workbook, beside the view | Claude | |
| D2 | Favourites, replacing Home and the trail | Claude | |
| D3 | Hiding items, and flagging the ones used almost everywhere | Claude | |
| E1 | Uses / Used by: each item once, with links to the exact lines | Claude | |
| E2 | Calls tab: the procedures an entry point calls, in order | Claude | |
| E3 | Calls tab: the Ifs, loops and Select Cases around each call | Claude | |
| E4 | pqdoc: a query's steps, in order | Claude | |
| E5 | Optional: a sequence diagram of the expanded calls | Claude | |
| F1 | Class diagram drawn by the page, with boxes you can move; Mermaid removed | Claude | |
| G1 | Saved views: named, exported to a file and imported again | Claude | |
| G2 | A views file in the output folder is built into the page | Claude | |
| G3 | Printing a view or every view, with descriptions | Claude, then you on a work machine | |
| G4 | Optional: copy a view as text, to paste into a chat | Claude | |

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
| The export seems slow, and pauses while writing the page | A2, and F1 (3.6 MB less to write) |
| pqdoc should write HTML by default | A3 |
| pqdoc should read queries from the workbook and show their source, as vbadoc does | B1–B4 |
| Search isn't always the best way to find things; a tree, perhaps in a sidebar | D1 |
| The VBA editor can't organise modules into folders | D1 groups modules by `'@Folder` comments |
| The trail is less useful than expected; favourites would help | D2 |
| Too much on screen; some items are used almost everywhere (logging, settings, predeclared classes, Power Query functions) | D3 |
| Uses / Used by lists an item twice; a count isn't useful; links to the exact place would be | E1 |
| What I most want to know: the path execution takes | E2–E4 |
| A sequence diagram, if it can be narrowed to one flow | E5 (optional) |
| The class diagram isn't interactive enough; boxes can't be rearranged | F1 |
| Arrange a view, export it with a printed version, regenerate it after changes | G1–G3 |
| Documentation for future developers and users | G2, G3 |
| AI help is a chat window with no access to the project | G4 (optional) |
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

**Non-goals**

- Running code, or fully parsing VBA or M. The call tree is built from the code as written, so calls made through
  `Application.Run`, a button's `OnAction` text or late binding can't be followed. The page says so.
- Writing anything back into a workbook.
- Anything that needs the network, an install or admin rights, on either the developer's machine or a reader's.
- Power BI files (`.pbix`, `.pbit`).
- Re-indenting M, or reading Power Query's Permissions and Permission Bindings sections.

## 4. What it will look like

### The page

Both tools' pages share this layout. Panels can be hidden and resized, as the source panel can now.

```
+------------------------------------------------------------------------------------------+
| Title   [Dependency graph] [Class diagram] [Calls]   Find...   Views v  Print  Theme  v0.x |
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

vbadoc's command doesn't change. pqdoc's takes a workbook as well as a folder, and `--format` lists what to write:

```
pqdoc <workbook or folder> [--output <folder>] [--output-name <name>] [--format <formats>] [--embed-source]
                           [--plantuml-server <url>] [--on-exists <behaviour>] [--force] [--verbose | --quiet]
```

### Output

| Command | Default folder | Contents |
|---|---|---|
| `vbadoc book.xlsm` | `VBAProject`, next to the input | As now: `index.html`, the module files, `.gitattributes` |
| `pqdoc book.xlsx` | `PowerQuery`, next to the input | `.m` files, `powerquery-dependencies.html`, `.gitattributes` |
| `pqdoc <folder>` | The input folder | `powerquery-dependencies.html` |

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
- **pqdoc reads workbooks before the shared library exists.** Steps B2–B4 copy vbadoc's approach in parallel code with
  the same names, and C1 then extracts what has really turned out to be duplicated. This follows the guidelines' rule
  of extracting only real duplication.
- **The shell comes before the new page features,** because almost all of them (explorer, favourites, hiding, views,
  printing) belong in both pages. The splitter bug, fixed twice, showed the cost of two copies.
- **Finding your way (D) comes before the execution path (E),** because the call tree relies on hiding items and on
  the explorer's list of entry points. If the call tree is wanted sooner, E2 could move before C2 as a vbadoc-only step,
  at the cost of moving it again in C2.
- **vbadoc's stage 6 (UserForms) moves to Later.** Its stage 7 (polish) comes after this plan.

### Reading queries

- **A hand-written reader in `PowerQueryDocumenter.Core/Extraction/`**, named in parallel with vbadoc's:
  `WorkbookReader` and `ExtractionException`. No new package: base64, `System.IO.Compression` and `System.Xml` are
  enough.
- **The format** ([MS-QDEFF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-qdeff/27b1dd1e-7de8-45d9-9c84-dfcc7a802e37))
  was checked against a real workbook on 2026-10-31. Queries are in a `customXml/item<n>.xml` part holding a base64
  `DataMashup` element. Decoded, it's a 4-byte version, then four length-prefixed sections. The first is a zip whose
  `Formulas/Section1.m` holds every query as `shared <name> = <expression>;`, in UTF-8 with mixed line endings. That
  workbook had one query, so B1 confirms the rest.
- **The part is found by what it is, not where it is:** any `customXml/item*.xml` whose root is `DataMashup` in the
  `http://schemas.microsoft.com/DataMashup` namespace. The XML reader takes the encoding from the byte order mark, with
  DTD processing off.
- **Every length is checked against the bytes left.** Anything that doesn't fit is reported as a corrupt workbook, exit
  code 3, and nothing is read past the end.
- **`Section1.m` is split with pqdoc's tokeniser, not a regular expression,** because a text literal can hold a `;` or a
  line starting `shared`. A member without `shared` is still kept as a query.
- **A section that can't be split stops the run,** with exit code 3, because carrying on would delete that query's
  `.m` file. This is vbadoc's rule for an unreadable module.
- **The file is opened for shared reading,** so it works while the workbook is open in Excel.
- **Permissions, Permission Bindings and embedded content are skipped, never read or written out.**

### `.m` files

- **Each file holds the expression as Excel stores it,** without `shared <name> =` or the final `;`, which is what a
  manual export contains, so pqdoc reads both the same way.
- **CRLF line endings and UTF-8 with no byte order mark,** matching vbadoc's output.
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
- **The first picture is always the same:** the layout is computed before drawing, in layers (interfaces above their
  implementations), then tidied with d3's force layout using each box's size. No new library unless the step shows d3
  can't produce a readable layout; any library must be MIT-compatible and vendored.
- **Mermaid is removed,** along with its 150-dependency limit and its escaping rules. The C# `ClassDiagram` model stays.

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

### Step D1: Explorer

- Add the explorer to the shell, and fill it from each tool, as section 5 describes, including `'@Folder` grouping and
  query groups.
- Selecting in the explorer selects in every view, and the explorer follows selections made elsewhere.
- Browser tests in both pages, including a workbook with `'@Folder` comments and one without. Look at screenshots.

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
- Browser tests: hiding an item removes it and its edges, persists across reloads, and comes back from the Hidden group.

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

### Step F1: Class diagram

- Rebuild `classes.js` on d3, as section 5 describes, with dragging, remembered positions, hiding, favourites and focus.
- Remove Mermaid from `Resources/Vendor/`, the page and the guidelines' list of dependencies.
- Acceptance: for the fixtures and the 300-module test project, every box, member and relationship the Mermaid version
  showed is still shown, and clicks select as before. Look at screenshots in both themes.

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
| `docs/GUIDELINES.md` | HTML pages: the shell, the explorer, views files | C2, G2 |
| `docs/GUIDELINES.md` | Dependencies: Mermaid removed | F1 |
| Both `README.md`s | Usage and features | A3, B2–G4 |

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
