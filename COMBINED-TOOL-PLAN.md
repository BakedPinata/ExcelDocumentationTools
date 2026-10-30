# ExcelDocumentationTools: combined tool plan

This plan makes pqdoc read Power Query straight from a workbook, then brings pqdoc and vbadoc together as one program
that documents everything in a workbook on a single page. It picks up step 4 of the finished monorepo plan and adds to
it. It is written to be handed to a fresh Claude Code session (or a developer) to carry out.

**Status: draft for review, 2026-10-31. Nothing here has been started.** Section 1 lists the questions to settle first.

## How to use this plan

- Read the whole file before starting. Sections 1–5 are the background and the decisions; section 6 has the steps.
- Work **one step at a time**. At the end of each step:
  1. `dotnet build ExcelDocumentationTools.sln -warnaserror` and the tests of everything touched pass;
  2. update the progress table below;
  3. commit;
  4. summarise what changed and how to check it, then **stop and wait for review**;
  5. push only after the review.
- Ask before doing anything that goes against this plan.
- `docs/GUIDELINES.md` and each tool's `CLAUDE.md` still apply, apart from the changes listed in section 7.
- Use Australian English in everything we write, as the guidelines set out.
- This machine can run short of memory with every browser test project going at once. If it does, run one test project
  at a time. The GitHub build runs everything.

## Progress

| Step | Scope | Who | Status |
|---|---|---|---|
| 4a | Sample workbooks with Power Query, and confirming the format against them | You in Excel, then Claude | |
| 4b | pqdoc reads queries from a workbook | Claude | |
| 4c | pqdoc writes `.m` files, kept in step with the workbook as vbadoc does | Claude | |
| 4d | pqdoc's page shows highlighted M source, from Load folder or `--embed-source` | Claude | |
| 4e | Shared library: `shared/`, with the C# code both tools now duplicate | Claude | |
| 4f | Shared page shell: both pages rebuilt on it, with no visible change | Claude | |
| 4g | One executable: `xldoc vba` and `xldoc pq`; `vbadoc.exe` and `pqdoc.exe` retired | Claude | |
| 4h | The root command: `xldoc <workbook>`, one folder, one page | Claude | |
| 4i | Links between VBA and the queries it refreshes | Claude | |

## 1. Questions to settle before starting

Each has a recommended answer, and the rest of this plan assumes it.

1. **What is the program called?** Recommended: `xldoc`, with `ExcelDocumenter` as the long name for projects and
   namespaces, following the naming rule in the guidelines. Changing this later means renaming a folder and projects, so
   it's worth settling now.
2. **Do `vbadoc.exe` and `pqdoc.exe` go away?** Recommended: yes, in step 4g, replaced by `xldoc vba` and `xldoc pq`.
   Keeping them would mean three programs to build and document. The `vbadoc/` and `pqdoc/` folders stay, holding each
   tool's library and tests.
3. **Where do the subcommands write?** Recommended: each keeps its own flat folder (`VBAProject`, `PowerQuery`), and only
   the root command makes `ExcelDocumentation`. That keeps any `VBAProject` folder already in git working, and each
   subcommand's page stays beside its own files. The alternative is one layout everywhere, with `xldoc vba` filling only
   the `vba` subfolder, but then both subcommands would want the `index.html` above it.
4. **What version does `xldoc` start at?** Recommended: 0.7.0, carrying on from vbadoc's 0.6.0, reaching 1.0.0 when
   vbadoc's stages 6 and 7 are done. pqdoc's 1.0.0 has never been tagged, so nothing is lost when it stops being
   separate.

## 2. Background

**Where things stand.** vbadoc reads VBA directly from a workbook and writes source files and a page. pqdoc needs each
query exported by hand to a `.m` file, and its page shows the graph but not the code. Both were built with a combined
tool in mind: they share System.CommandLine, Serilog and exit codes, keep their logic in Core libraries, and pqdoc's
`window.pqdoc` is shaped like vbadoc's `window.vbadoc`.

**The workbook format.** Power Query is stored as described in
[MS-QDEFF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-qdeff/27b1dd1e-7de8-45d9-9c84-dfcc7a802e37).
This was checked on 2026-10-31 against the real workbook in `vbadoc/private/`, not just read from the spec:

- It is in a custom XML part, `customXml/item<n>.xml` (UTF-16 with a byte order mark in this workbook), with one
  `DataMashup` element in the `http://schemas.microsoft.com/DataMashup` namespace whose content is base64.
- The decoded bytes are a 4-byte version (0), then four sections each preceded by a 4-byte length: Package Parts,
  Permissions, Metadata and Permission Bindings. The lengths added up exactly to the end of the data.
- Package Parts is a zip holding `Config/Package.xml`, `[Content_Types].xml` and `Formulas/Section1.m`. The spec allows
  `Content/<guid>` parts as well, which hold binary data this tool has no use for.
- `Section1.m` is UTF-8 with no byte order mark. It starts `section Section1;`, followed by `shared <name> = <expression>;`
  for each query. Its line endings are mixed, with CRLF and LF in the same file.
- With line endings normalised, that query's expression is identical to the `.m` file exported by hand.

That workbook has a single query, so this proves the layout but not splitting many queries, or awkward names. That's
what step 4a is for. Everything needed is in .NET already (base64, `System.IO.Compression`, `System.Xml`), so **no new
package is needed**.

**The spec settles two details.** A query name can't contain `.`, `"`, tabs, line breaks or leading or trailing spaces,
but it **can** contain `/ \ : * ? < > |`, which Windows forbids in file names. Each query also has metadata such as
whether it's a function, and where it loads to, mixed with values that change on every refresh, such as `FillLastUpdated`
and `FillCount`.

**The two pages.** They can't simply be put in one document yet. They share 10 element ids (`graph-svg`, `tab-graph`,
`view-graph`, `search` and others) and 34 CSS class names, and only about a quarter of their colour and size tokens are
identical. Their two `d3.min.js` files are identical (280 KB), and vbadoc's Mermaid is 3.6 MB.

**The tests.** None of the public test workbooks contains Power Query, so samples have to be made. About 1,100 lines of
command-line tests sit against the two console apps and will move to `xldoc`. The 141 browser tests guard the page work.

## 3. Goals and non-goals

**Goals**

1. pqdoc reads queries directly from a workbook, with no manual export.
2. It writes them to a folder of `.m` files, kept in step with the workbook as vbadoc keeps its module files.
3. Clicking a query in the graph shows its M source with syntax highlighting, from Load folder or embedded in the page.
4. A shared library holds what the tools would otherwise duplicate.
5. One executable, with a subcommand for each tool.
6. A root command documents a whole workbook: one `ExcelDocumentation` folder and one HTML page covering both.
7. The page links VBA to the queries it refreshes.

**Non-goals**

- Power BI files (`.pbix`, `.pbit`).
- Running, validating or fully parsing M. The tokeniser and light structural pass stay.
- Re-indenting M. "Formatted" here means syntax highlighted, as vbadoc's source view is. Re-indenting would need a full
  M parser, and the lines would no longer match the `.m` file.
- Writing anything back into a workbook.
- Reading the Permissions or Permission Bindings sections.
- Moving to .NET 10. .NET 9 support ends on 2026-11-10 and the guidelines already cover that decision; this plan doesn't
  change or depend on it.

## 4. What it will look like

### Commands

```
xldoc <workbook>        [--output <folder>] [--embed-source] [--verbose | --quiet]

xldoc vba <workbook>    [--output <folder>] [--embed-source] [--verbose | --quiet]

xldoc pq <workbook or folder>
                        [--output <folder>] [--output-name <name>] [--format <formats>] [--no-csv]
                        [--embed-source] [--plantuml-server <url>] [--on-exists <behaviour>] [--force]
                        [--verbose | --quiet]

xldoc --help | --version
```

A subcommand name wins over a file name, so a workbook file called just `vba`, with no extension, needs `.\vba`.

### Output

| Command | Default folder, next to the input | Contents |
|---|---|---|
| `xldoc book.xlsm` | `ExcelDocumentation` | `index.html`, `vba/`, `m/`, `.gitattributes` |
| `xldoc vba book.xlsm` | `VBAProject` | As vbadoc today: `index.html` and the module files, flat |
| `xldoc pq book.xlsm` | `PowerQuery` | `.m` files, flat, with `powerquery-dependencies.html` and `.csv`, and `.gitattributes` |
| `xldoc pq <folder>` | The input folder | As pqdoc today |

```
ExcelDocumentation/
  index.html            the combined page
  .gitattributes
  vba/                  .bas, .cls and .frm files
  m/                    one .m file per query
```

### Repository

```
ExcelDocumentationTools.sln       every project
publish.ps1                       unchanged: it finds xldoc's publish profile by itself
docs/GUIDELINES.md
shared/
  src/ExcelDocumenter.Shared/     workbook access, exit codes, version, source folder writer, page shell, d3
  tests/ExcelDocumenter.Shared.Tests/
vbadoc/
  src/VbaDocumenter.Core/         VBA extraction, parsing and analysis, and the VBA views
  tests/
pqdoc/
  src/PowerQueryDocumenter.Core/  query reading, M analysis, exporters, and the query view
  tests/
xldoc/
  src/ExcelDocumenter/            the one console app: command line, logging, exit codes
  src/ExcelDocumenter.Core/       the combined run, the combined page, the VBA-to-query links
  tests/
```

`ExcelDocumenter` uses `ExcelDocumenter.Core`, which uses `VbaDocumenter.Core` and `PowerQueryDocumenter.Core`, which
each use `ExcelDocumenter.Shared`. The tool libraries never use each other.

## 5. Decisions

### Order of the work

- **pqdoc gets its features first, and the shared library comes after.** Steps 4b–4d copy vbadoc's approach in parallel
  code, with the same names. Step 4e then extracts what has really turned out to be duplicated. This follows the
  guidelines' rule of extracting only real duplication, keeps each step small, and means the extraction has working code
  and tests to protect it. The cost is a few dozen lines copied for a short time.
- **vbadoc's stages 6 and 7 wait until this plan is finished**, then land in the new structure. Stage 6 changes the source
  panel, which step 4f moves, so doing it first would only add to what has to move.

### Reading queries

- **A hand-written reader, in `PowerQueryDocumenter.Core/Extraction/`,** named in parallel with vbadoc's: `WorkbookReader`
  and `ExtractionException`.
- **The part is found by what it is, not where it is:** any `customXml/item*.xml` whose root is `DataMashup` in the right
  namespace. The XML reader takes the encoding from the byte order mark, with DTD processing off.
- **Every length is checked against the bytes left.** Anything that doesn't fit is reported as a corrupt workbook, exit
  code 3, and nothing is read past the end.
- **`Section1.m` is split with pqdoc's tokeniser, not a regular expression,** because a text literal can hold a `;` or a
  line starting `shared`. The spec says members *should* be shared, so a member without `shared` is kept as a query.
- **A section that can't be split stops the run,** with exit code 3. This is vbadoc's rule for an unreadable module:
  carrying on would delete that query's `.m` file.
- **The file is opened for shared reading,** so it works while the workbook is open in Excel, as vbadoc does.
- **The Permissions, Permission Bindings and embedded content sections are skipped, never read or written out.**

### `.m` files

- **Each file holds the expression as Excel stores it,** without `shared <name> =` or the final `;`. That's what the
  Advanced Editor shows and what a manual export contains, so pqdoc reads both the same way.
- **CRLF line endings and UTF-8 with no byte order mark.** CRLF matches vbadoc's output, and normalising is needed anyway
  because `Section1.m` mixes them.
- **File names can be turned back into query names.** The characters Windows forbids and `%` itself become `%XX`, so
  `Sales/Region` is `Sales%2FRegion.m`, and a reserved name such as `CON` is written `%43ON.m`. Reading a folder reverses
  exactly those escapes, so a file made by hand with some other `%` in its name keeps its name. vbadoc's `_` substitution
  doesn't suit this: for VBA it's a safety net that should never run, but real query names can need it, and two names
  could end up the same.
- **Kept in step like vbadoc's module files:** unchanged files aren't rewritten, top-level `.m` files no longer in the
  workbook are deleted, other files are left alone, and a `.gitattributes` is created if there isn't one. So the README
  needs vbadoc's warning about not keeping hand-written files there, and `--on-exists` doesn't apply to `.m` files.
- **Names that clash stop the run,** with exit code 3, like vbadoc's module names. Query names are case-insensitive here.
- **With a workbook as input, `xldoc pq` writes HTML by default** as well as the CSV, so it behaves like `xldoc vba`.
  `--format` still adds to that. With a folder as input, the defaults stay as they are.

### Privacy

- **M source is more sensitive than query names.** It can hold server names, file paths and anything typed into a query.
  So source isn't embedded by default, the README says so, and the page still blocks all network access.
- **Source never goes to a PlantUML server.** Diagram text stays as it is today: query names only, and only to a server
  the user configured.
- **Test workbooks hold made-up data only.** `vbadoc/private/` is never committed, and nothing from it (names, code or the
  workbook's name) goes into a test, a fixture, a doc or a commit message.

### Shared library

- **`shared/` holds only what is duplicated or needed by both,** listed in step 4e. Command-line and logging code doesn't
  go there: once there is one console app, there's nothing to share it with.
- **`shared/` changes both tools' build stamps,** like the root build files.

### Page shell

- **One shell, used by all three pages.** The shell owns the top bar, tabs, theme, settings storage, side panel, splitter,
  search box, Load folder and source view. A tool adds views to it. vbadoc's page is the shell plus VBA views, pqdoc's is
  the shell plus the query view, and the combined page is the shell plus both. The alternative, a third page beside the
  other two, would mean fixing every shell bug three times, as the splitter showed with two.
- **Each tool keeps its own global, data block and selection.** `window.vbadoc` and `window.pqdoc` keep their shape, and
  `vbadoc-data` and `pqdoc-data` their content, so the views barely change. In the combined page the panel follows
  whichever tool's tab is active.
- **Whatever still clashes after the shell takes its own ids and classes gets a `pq-` prefix on pqdoc's side.** The
  shell's own, such as `layout` and `theme-toggle`, just merge. Others, such as `graph-svg`, stay with vbadoc because it
  has more of them and is the reference design.
- **Storage keys stay `vbadoc-…` and `pqdoc-…`** in those pages. The combined page uses `xldoc-…` for what the shell owns.

### One executable

- **One version and one stamp.** Every page and `--version` show `xldoc <version>`. The stamp is the last commit touching
  `vbadoc/`, `pqdoc/`, `shared/`, `xldoc/` or the root build files.
- **Options keep their names and behaviour,** and exit codes stay as they are. Only the program's name changes.
- **Release tags become `xldoc-v<version>`.** None have been made yet, so nothing needs renaming.
- **`vbadoc/` and `pqdoc/` keep their solutions, docs and tests,** so work on one tool still starts in its folder.

### Root command

- **Both parts are read and analysed before anything is written.** If either fails, nothing is written and the exit code
  is 3. vbadoc already builds its page before writing, for the same reason.
- **A workbook with only VBA or only queries gets just that part:** one subfolder, and only those tabs, with a message
  saying so. With neither, the exit code is 3. `.xlsx` works for queries; `.xls` and `.xla` have only VBA.
- **The folder is always brought in line with the workbook, with no prompts,** as in vbadoc. Stale files are deleted only
  inside `vba/` and `m/`. Exit code 4 stays unused here.
- **Two workbooks in one directory would share `ExcelDocumentation`,** so one needs `--output`. That's the same rule as
  `VBAProject` today.

## 6. Steps

### Before step 4a

- In `pqdoc/docs/PLAN.md`, replace the Later section with a pointer here. In `vbadoc/docs/PLAN.md`, note that stage 6 waits
  for this plan. Add this plan to the Start here list in the root `CLAUDE.md`.

### Step 4a: Sample workbooks

Excel has to make these, so this step needs you. They go in a public repository, so use made-up data only. Save them in
`pqdoc/tests/PowerQueryDocumenter.Tests/Fixtures/Workbooks/`:

| File | What it needs |
|---|---|
| `Queries.xlsx` | About eight queries: a chain (A feeds B feeds C); a name with spaces (`Sales Data`); a name with `/` or `:` and one with `%`, if Excel accepts them; a function query; a parameter; an Enter Data table; a query with a text value containing `;` and a line starting `shared`; one with `//` and `/* */` comments, including on its last line. Load one to a sheet, one to the data model, and leave one connection only. Put two in a query group |
| `QueriesAndMacros.xlsm` | Three or four queries, and VBA that uses them: `ThisWorkbook.Queries("Sales Data")`, `ThisWorkbook.Connections("Query - Sales Data").Refresh`, `ThisWorkbook.RefreshAll`, and `ListObject.QueryTable.Refresh` on a loaded table |
| `Queries.xlsb` | One or two queries, to confirm binary workbooks store them the same way |

Then Claude checks each one with a throwaway script and records in `pqdoc/docs/DECISIONS.md` what is confirmed: the
part's encoding, which characters Excel really accepts in a name, whether any member lacks `shared`, and that `.xlsb`
matches. Anything that disagrees with section 5 is raised before step 4b. Add a `README.md` to the fixture folder saying
what each file is for.

### Step 4b: pqdoc reads a workbook

- Add `Extraction/` to `PowerQueryDocumenter.Core`: open the file, find the part, decode it, take `Section1.m` and split
  it into the existing `QuerySource` records. The analyser doesn't change.
- The `<input_folder>` argument becomes `<input>`, a workbook or a folder. A workbook's default output folder is
  `PowerQuery` beside it.
- Clear messages, each exit code 3, for: no Power Query in the workbook; an encrypted workbook; a corrupt part; a file
  that isn't a workbook. For a compound file, say it's encrypted or an older `.xls`, neither of which has readable queries.
- Tests use the samples, plus a small helper that builds `DataMashup` bytes, for damaged lengths and bad base64.
- Acceptance: the graph from `Queries.xlsx` matches the graph from the same queries exported by hand. The private
  workbook gives the same CSV as its hand-exported query (checked by hand; it isn't a test).

### Step 4c: pqdoc writes `.m` files

- Add `Output/QueryFileExporter`, parallel to vbadoc's `ProjectExporter`, following section 5.
- The folder reader decodes file names, so the round trip works.
- Tests: names needing escapes, reserved names, case clashes, stale files, unchanged files keeping their timestamps, an
  existing `.gitattributes`, and a run stopped by a bad section leaving existing files alone.
- Acceptance: run pqdoc on `Queries.xlsx`, run it again on the folder it wrote, and get the same graph with the same
  names. The private workbook's `.m` file matches the hand-exported one, line endings aside.

### Step 4d: pqdoc's page shows source

- Add `m-highlight.js`, parallel to `vba-highlight.js`: comments, text, `#"quoted names"`, keywords, numbers, `#date`-style
  constructors and type names. Enter Data queries have very long lines, so test that one doesn't slow the page down.
- Add a Source section to the details panel, with line numbers, below what the query uses and what uses it.
- Add Load folder and `--embed-source`, as vbadoc does: hidden `<pre>` elements, and a hash of each file so the page warns
  when a loaded folder no longer matches.
- With a folder as input, `--embed-source` uses the files in that folder.
- Reuse vbadoc's syntax colour tokens, and check them with the palette validator.
- Browser tests, including that no M code is in the page without `--embed-source`. Look at screenshots in both themes.

### Step 4e: Shared library

- Create `shared/` as in section 4, and move in, with one copy of each:
  - opening a workbook, telling zip from compound file, and spotting encryption, from vbadoc's `WorkbookReader`;
  - `ExitCodes` and `ToolVersion`;
  - `EmbeddedResources` and the page-building helpers (filling placeholders, inlining scripts, escaping);
  - the source folder writer that `ProjectExporter` and `QueryFileExporter` now both contain;
  - `AtomicFileWriter`, which vbadoc's writes then use too;
  - `d3.min.js` with its licence.
- pqdoc picks up OpenMcdf through `shared/` (MPL-2.0, unmodified, so allowed).
- Add `shared/` to the stamp paths in `Directory.Build.targets`.
- Update the "no shared code project" rule in the guidelines.
- Acceptance: pages and output files generated before and after are byte-identical apart from the build stamp. Save
  reference output from the fixtures before starting.

### Step 4f: Shared page shell

This is the largest step and the likeliest to drift, so keep to it strictly: no new features.

- Move the parts listed in section 5 into `shared/`'s page resources. Each tool's own files keep only its views.
- Merge the CSS tokens. The identical ones move as they are. Go through the rest one at a time, and re-run the palette
  validator for any colour that changes.
- Apply the `pq-` prefix, and give each tool's styles a scope so they can't reach the other's views.
- Agree the small interface between shell and views, and write it down at the top of the shell's script, as `page.js` does
  today.
- Acceptance: both pages look and work as before. All 141 browser tests pass, changed only where an id was renamed.
  Compare screenshots in both themes.

### Step 4g: One executable

- Create `xldoc/` and merge the two console apps into `ExcelDocumenter`, with `vba` and `pq` subcommands. Merge
  `LoggingSetup` and `AppServices` as well.
- Move the command-line tests to `ExcelDocumenter.Tests`, keeping the requirement ids they mention.
- Delete `vbadoc/src/VbaDocumenter` and `pqdoc/src/PowerQueryDocumenter`, with their publish profiles.
- Give `xldoc/` the version and stamp from section 5, and change the top bar in every page.
- Rewrite the root `README.md` around `xldoc`, add `xldoc/README.md`, `Requirements.md`, `CLAUDE.md` and `docs/`, and make
  the changes in section 7.
- `publish.ps1` and the build workflow need no change. The script never deletes, so `vbadoc.exe` and `pqdoc.exe` stay in
  the publish folder until you remove them by hand.
- Acceptance: `xldoc vba` and `xldoc pq` output is byte-identical to the old tools' apart from the stamp, and the exit
  code tests still pass. A published `xldoc.exe` runs from the publish folder with nothing else installed.

### Step 4h: The root command

- Add `ExcelDocumenter.Core`, with the combined run from section 5 and the folder layout from section 4. It reuses both
  tools' exporters, pointed at `vba/` and `m/`.
- Build the combined page: the shell, both tools' views and data blocks, one d3, and one Mermaid.
  - Tabs: Dependency graph, Class diagram and Power Query, showing only those with something in them.
  - Search covers modules, members and queries, grouped so it's clear which is which.
  - vbadoc's trail shows on its tabs and hides on the Power Query tab. Fragments in the page address say which tool they
    belong to, so browser Back and Forward work across tabs.
  - Load folder takes the `ExcelDocumentation` folder and finds both subfolders. Picking one subfolder also works.
- Add `ExcelDocumenter.BrowserTests`. Cover both tools working in one document, switching tabs with a selection in each,
  theme changes reaching every view, and pages with only one part.
- Acceptance: `QueriesAndMacros.xlsm`, an `.xlsx`, an `.xls` and a workbook with neither all behave as section 5 says. Check
  the private workbook by hand, and look at screenshots in both themes.

### Step 4i: Links between VBA and queries

This can be dropped without affecting anything before it.

- In `ExcelDocumenter.Core`, look through each VBA member's text literals for `.Queries("<name>")` and
  `.Connections("Query - <name>")`. For `ListObjects("<table>").QueryTable`, the `FillTarget` metadata entry gives the
  table's query. Note `RefreshAll` on the member as refreshing every query.
- Show links both ways: a member's panel lists its queries, and a query's panel lists the VBA that uses it. Clicking one
  switches tab and selects it.
- Only literal names can be found, as with vbadoc's late-bound calls, so say on the page that the list may be incomplete.
- Tests use `QueriesAndMacros.xlsm`, including a name built at run time that must not produce a link.

## 7. Changes to existing documents

The rule is to ask before changing a tool's `Requirements.md` or `DECISIONS.md`, or going against the guidelines.
Approving this plan covers the changes below. Anything else still needs asking.

| Document | Change | Step |
|---|---|---|
| `pqdoc/Requirements.md` | Section 2 input, the `<input>` argument and `--embed-source`, new requirement ids for reading workbooks and writing `.m` files. Remove "Parsing queries directly from `.pbix` / `.xlsx` files" from section 8, keeping `.pbix` | 4b–4d |
| `pqdoc/docs/DECISIONS.md` | New decisions on reading, `.m` files and source. Reword "the query name is the file name" for escapes | 4a–4d |
| `docs/GUIDELINES.md` | Structure: `shared/` exists and what belongs there | 4e |
| `docs/GUIDELINES.md` | HTML pages: the shell | 4f |
| `docs/GUIDELINES.md` | Naming table, versions and the stamp, publishing, release tags, Claude Code sessions | 4g |
| `vbadoc/Requirements.md`, both `README.md`s | The command name and usage | 4g |
| `vbadoc/docs/DECISIONS.md` | "The code is parallel, not shared" no longer holds. Per-tool version and stamp | 4e, 4g |
| `vbadoc/CLAUDE.md`, `pqdoc/CLAUDE.md`, root `CLAUDE.md` | Commands, and where work starts | 4g |

## 8. When the plan is finished

After the last step is reviewed, check that every decision in section 5 is in `docs/GUIDELINES.md` or a tool's
`docs/DECISIONS.md`, move section 9 into the relevant `docs/PLAN.md`, then **delete this file** in its own commit. It stays
in the repository's history. vbadoc's stage 6 is next after that.

## 9. Later

Left out to keep this plan to what was asked for:

- Show where a query loads to (sheet, table, data model or connection only) and its query group. The metadata has both.
  Leave out the values that change on refresh, such as `FillLastUpdated` and `FillCount`, or every refresh changes the page.
- Make query names inside M source into links.
- Read Power BI files.
