# Design decisions

Decisions made while building the tool that aren't spelled out in [Requirements.md](../Requirements.md),
or that change it. Add to this file whenever a new decision is agreed. Conventions shared with the other tools are in
[GUIDELINES.md](../../docs/GUIDELINES.md).

## Platform and libraries

- **The first version is 1.0.0.** The tool's earlier form came from a Claude chat and isn't counted, so the docs
  describe the tool as it is, not how it differs from that.
- **.NET 10**, the same as vbadoc (moved from .NET 9 on 2026-11-03). The target framework is in the root
  `Directory.Build.props`.
- **Central Package Management.** Every NuGet version is in the root `Directory.Packages.props`.
- **Shared project settings** are in the root `Directory.Build.props`. `pqdoc/Directory.Build.props` imports it and sets
  `Version`, `Product` and `ToolRoot`.
- **The logic is in `PowerQueryDocumenter.Core`**, including `Application/`: `AnalyserApp` runs the analysis and the
  exports, so it and the exit codes it returns belong with the logic. The console app holds `Program.cs`, the command
  line, logging setup, the console prompts and `DefaultServices`, which puts the real services together. Its types are
  internal, and the tests reach them through `InternalsVisibleTo`.
- **Core uses Serilog's `ILogger`**, which needs only the `Serilog` package. The console sink stays in the console app.
- **`--version` shows the build stamp**, `<version>+<commit>[-dirty]`, the same as vbadoc. The commit is the last one
  that touched `pqdoc/` or the root build files.
- **The output option is `--output`/`-o`**, shared with vbadoc. It replaced `--output-folder`, with no alias for the old
  name, before any release. The version stays 1.0.0, because none has been tagged yet.
- **The `.env` reader is hand-written** rather than a package.
- **An environment variable or `.env` file, not .NET user secrets**, configures the integration tests. The tool already
  reads both, they work the same way locally and in a CI pipeline, and they need no extra packages.
- **The PlantUML user agent is `pqdoc/<version>`**, with the version read from the assembly, so it can't drift.
- **d3 7.9.0** is vendored byte-for-byte, once for both tools, in `shared/src/ExcelDocumenter.Shared/Resources/Vendor/`
  since step C1. Until then pqdoc had its own copy, the same file as vbadoc's.
- **The shared library** (1.5.1, step C1 of the feedback plan). Opening a workbook, exit codes, `ToolVersion`,
  `ExtractionException`, `AtomicFileWriter`, the folder writer behind `QueryFileExporter`, the page-building helpers and
  d3 moved to `shared/`, as one copy with vbadoc's (see [shared/README.md](../../shared/README.md)). pqdoc's messages and
  outputs are unchanged: every fixture's output was byte-identical apart from the build stamp and generation time. The
  page now escapes text as vbadoc's does (`&`, `<`, `>`, `"`), where `WebUtility.HtmlEncode` also wrote `'` and
  non-ASCII characters as numeric references; a browser shows both the same. pqdoc now builds against OpenMcdf through
  the shared library (MPL-2.0, unmodified), though it doesn't look inside compound files itself.
- **The version stays 1.0.0 through stage 3d**, because none has been tagged yet.

## Analysis

- **A tokeniser and a light structural pass, not a full M parser.** The tokeniser separates identifiers from comments
  and text literals. The structural pass then ignores `let` step names, record field names, function parameters
  (including `each`'s `_`) and any use of them within their scope, and field access and projection. This is a
  heuristic: unusual code can still produce a missed or extra dependency. `--verbose` logs the references found in
  each query.
- **Malformed M never throws.** An unterminated comment, text literal or quoted identifier runs to the end of the input.
- **Only identifiers that match a known query count as references**, so built-in functions and keywords can't produce a
  false dependency and no filter of built-in names is needed. That is what lets a query be named `Source` or `Table`.
- **Dotted names are one identifier.** `Table.SelectRows` doesn't match a query named `Table`.
- **Lower-case M type names in type positions** (`as table`, `type [A = text]`) only match a query spelled exactly the
  same way.
- **Query names are case-insensitive**, so a file whose name differs from another's only by case is ignored with a
  warning.
- **A file that can't be read doesn't stop the run.** Its query stays in the graph, because other queries may reference
  it, but its own references are missing. Outputs are still produced, and the exit code is 3.

## Reading workbooks: what step B1 confirmed

Checked on 2026-11-03 against `Queries.xlsx` and `Queries.xlsb` (see the README beside them), following
[MS-QDEFF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-qdeff/27b1dd1e-7de8-45d9-9c84-dfcc7a802e37).
Reading them is step B2 of the feedback plan.

- **The part:** `customXml/item1.xml`, UTF-16 LE with a byte order mark, whose root is `DataMashup` in the
  `http://schemas.microsoft.com/DataMashup` namespace (with an `sqmid` attribute), holding base64 text.
- **`.xlsb` stores it the same way.** Its custom XML part is still XML, and the decoded bytes are identical to the
  `.xlsx`'s, so binary workbooks need nothing extra.
- **Layout:** a 4-byte version (0), then Package Parts, Permissions, Metadata and Permission Bindings, each after a
  4-byte length. The lengths add up exactly to the end of the data.
- **Package Parts** is a zip of `Config/Package.xml`, `[Content_Types].xml` and `Formulas/Section1.m` only. An Enter
  Data table is stored in the M itself, compressed and in base64, so there are no `Content/` parts.
- **`Section1.m`** is UTF-8 with no byte order mark, starting `section Section1;`. Every member is `shared`. Line endings
  differ by query, following how each was written: some queries' code uses CRLF and others LF. The multi-line text
  literal uses CRLF.
- **Names:** Excel accepts spaces, `/` and `%` (`Sales Data`, `Sales/Returns`, `Discount%`). `Section1.m` writes
  them as quoted identifiers (`#"Sales/Returns"`). The metadata's item paths URL-encode them
  (`Section1/Sales%2FReturns`, `Section1/Discount%25`).
- **A description isn't metadata.** Excel writes it into `Section1.m` as an attribute record before the member:
  `[ Description = "…" ] shared Chain_E = …`. Splitting the section takes the record off, and keeps the description.
- **Query groups** are a `QueryGroups` entry on the metadata's `AllFormulas` item: base64 of a small binary list. It
  holds a count, then for each group an int32, a GUID, its name and description as length-prefixed strings, and 5
  more bytes (apparently a flag and its order). A query in a group has a `QueryGroupID` entry with that GUID. The
  layout was worked out from these samples, so reading it fails safe: a list that doesn't parse means no groups, not an
  error. Nested groups weren't tested. (D1b found the flag says whether a parent group's GUID follows; see Explorer
  layouts and nested groups.)
- **Other metadata** is per query, at item path `Section1/<name>`: `ResultType` (`Function` for a function; a parameter
  is marked in its M with `meta [IsParameterQuery = true]`), and where it loads: `FillObjectType` (`Table` or
  `ConnectionOnly`), `FillToDataModelEnabled`, `AddedToDataModel` and `FillTarget` (the table's name). Values carry a
  type prefix: `s` text, `l` number or true/false (`l1`), `d` date. `FillLastUpdated`, `FillCount`, `FillStatus` and
  `FillErrorCount` change on refresh. Each query step has an item too (`Section1/<name>/<step>`), with no entries.
- **The Metadata section** is a 4-byte version, the metadata XML after its 4-byte length, then a 4-byte length and an
  empty zip.
- **`.m` files will use CRLF outside text literals** (agreed with the user after B1, replacing "CRLF throughout"). Line
  breaks inside a text literal stay as Excel stored them, so the text is unchanged if a query is imported back into a
  workbook.

## Reading workbooks: how it's done (1.2.0, step B2)

- **`Extraction/` in Core**, named in parallel with vbadoc's: `WorkbookReader` and `ExtractionException` (shared with
  vbadoc since C1), plus `SectionSplitter`. No new package: base64, `System.IO.Compression` and `System.Xml` are enough.
- **An input that is a file is a workbook**; anything else is a folder of `.m` files, which the run reports if it's
  missing. The extension isn't checked, so a renamed workbook still works and a stray file gets a clear message.
- **A workbook's outputs go in a `PowerQuery` folder beside it**, so they don't mix with other files next to the
  workbook and the folder can go into git. `--output` still wins. A folder's outputs stay next to its `.m` files.
- **The first bytes decide what the file is:** a zip is read; a compound file is reported as encrypted or an older
  `.xls` without looking inside, since neither has readable queries (pqdoc doesn't need OpenMcdf for that); anything
  else isn't a workbook.
- **The DataMashup is the first `customXml/item<n>.xml` whose root is `DataMashup` in its namespace.** Custom XML that
  isn't well-formed belongs to something else and is passed over. The reader takes each part's encoding from its byte
  order mark, with DTD processing off.
- **Every length is checked against the bytes left,** in all four sections, and a version other than 0 is refused.
  Anything that doesn't fit is reported as damage, and nothing is read past the end.
- **`Section1.m` is split on tokens:** the section declaration, then for each member an optional attribute record, an
  optional `shared`, the name, `=`, and everything up to the next `;` token, trimmed. The description is a top-level
  `Description` text field of the attribute record. A section that can't be split stops the run, saying which line,
  because a query left out would later delete its `.m` file.
- **Two queries with the same name, ignoring case, stop the run**, since every other part of pqdoc treats names as
  case-insensitive.
- **Checked by hand on the private workbook:** its one query reads exactly as the user's hand export, line endings
  included, and the CSVs are identical.

## Query files (1.4.0, step B3)

- **`Output/QueryFileExporter`, parallel to vbadoc's `ProjectExporter`,** with `QueryFileNames` for the escapes and
  `QueryFileText` for line endings. It writes through `AtomicFileWriter`. Since C1 both are shared with vbadoc:
  `QueryFileExporter` works out the files and hands them to the shared `SourceFolderWriter`.
- **Each file holds the expression as Excel stores it**, without `shared <name> =` or the final `;`, which is what a
  manual export contains, so pqdoc reads both the same way. A description isn't written: the file stays the query.
- **File names escape with `%XX`, not vbadoc's `_`.** For VBA, `_` is a safety net that never runs, because module
  names are identifiers; query names often need it, and `_` could make two names the same. The escaped characters are
  those Windows forbids, control characters and `%`; a device name (`CON`, `LPT1`) has its first letter escaped.
  Decoding reverses only those escapes, written in upper-case hex, so `Sales%20Data.m` made by hand stays
  `Sales%20Data`, and every name survives the round trip.
- **CRLF outside text literals; literal line breaks as Excel stored them** (agreed after B1). The walker follows the
  tokeniser's rules for comments, text and `""` escapes. Line breaks in block comments become CRLF too.
- **Kept in step like vbadoc's module files, with no manifest:** every top-level `.m` file in the folder is pqdoc's.
  Unchanged files aren't rewritten; files whose query has gone are deleted (names compared exactly, so a rename by case
  takes effect on Windows); other files and subfolders are left alone. The README warns against hand-written `.m` files
  there.
- **`--on-exists` doesn't apply to query files**, because they follow the workbook as vbadoc's module files do. They're
  written after the overwrite check, so quitting at a prompt still writes nothing, and before the outputs.
- **The created `.gitattributes`** marks `*.m` and `*.html` as `-text diff`. `-text` matters more than for VBA: a
  literal's LF line break would otherwise be converted by git.
- **Failures:** names that would share a file (impossible once the reader rejects duplicates, but checked anyway) stop
  the run with exit code 3 before anything is written. A file that can't be written stops the query files, gives exit
  code 5, and the outputs are still written; older outputs aren't cleared in that case.
- **Checked by hand on the private workbook:** its query's file has the same name and text as the user's hand export,
  apart from line endings, with no byte order mark.

## Source in the page (1.5.0, step B4)

- **Copied from vbadoc's source panel, under the same names**, so step C2 can merge them: `--embed-source`, hidden
  `<pre>` blocks (`pre.pqdoc-source[data-query]`), Load folder with a top-level-only file list, the first 16 hex digits
  of each file's SHA-256 to spot a folder that no longer matches, the status text in the top bar, and the
  `.code .line .ln .lt` markup with `tok-*` classes.
- **The source is a section of the details panel**, below Uses and Used by, not a panel of its own: a query's code is
  usually short, and the panel already belongs to the selected query. Only the code scrolls sideways, so an Enter Data
  query's long line doesn't push the lists out of view.
- **M code isn't embedded by default**, as with vbadoc's VBA: it can hold server names and file paths, and pages get
  emailed. A page with no source files at all (`sourceMode` "none") hides Load folder.
- **The page's files are the ones beside it.** From a workbook, the hash is taken over the bytes written to each `.m`
  file; from a folder, over the files the analysis read, read again. A hand export can be UTF-16 with a byte order
  mark, so the page and `QueryFile` both decode UTF-16 when a BOM says so, and hash the raw bytes.
- **`m-highlight.js` scans each line once**, carrying open text, quoted names and block comments to the next line, and
  finds the end of text with `indexOf`. A 1 MB line takes about 0.1 s to show. `..` is the range operator, so the `3`
  in `{1..3}` isn't read as `.3`. Dotted names ending in `.Type` (`Int64.Type`) count as type names.
- **The syntax colours are vbadoc's, checked as text**, against the panel's background: every token is at least 4.5:1
  in both themes (comments the lowest, at 4.8:1, and italic too). They aren't a categorical palette, and fail that
  check, because a token's kind also shows in its text (quotes, `//`, digits, `#`). In the light theme numbers and type
  names are close in colour, in vbadoc too; left for C2, which gives both pages one set of tokens.

## Explorer (1.6.0, step D1 of the feedback plan)

- **Groups come from the metadata** (`Extraction/QueryMetadata`): the `QueryGroups` list and each query's
  `QueryGroupID`, as B1 found them, and `ResultType` for functions. The list's layout, checked again on the samples, is
  a count, then per group an int32 (0 in both samples), the GUID, the name and description as .NET length-prefixed
  UTF-8 strings, a byte and the group's order as an int32. A list that doesn't parse to exactly its length, or
  metadata that isn't readable XML, means no groups and no result types, never a failed run. Nested groups still
  aren't known, so each group is listed at the top level. (D1b replaced this; see below.)
- **Ungrouped queries go under Other queries,** as Excel's Queries pane shows them, after the groups in Excel's order.
- **For a folder, functions are found in the code** (`Analysis/FunctionDetector`): a function literal, `each`, or a
  `let` whose result is one of its own steps defined as a function, which is what Excel's Create Function writes.
  Anything else counts as a query; a parameter does too, since it isn't a function.
- **The page data** gains `kind` and `group` per query and `groups` in order (FR-HTML-11).

## Speed (1.9.1, step D1d of the feedback plan)

- **The tooltip is hidden by visibility, not display,** as vbadoc's is now: taking it out of the layout and back made
  every hover lay out the whole graph again, which vbadoc's measurements found on a large project. See vbadoc's
  decisions for the measurements.

## View controls and the summary (1.9.0, step D1c of the feedback plan)

- **The graph gains Focus, following the page's focus on click** (FR-HTML-13), as vbadoc's views do: a focused query
  shows only it and its upstream and downstream chains, the rest left out (not faded), and the view fits them;
  clearing the selection shows every query and fits them all. Clicking the selected query still clears the selection,
  as before, so with focus on it's also the way back; a Ctrl+click selects without focusing, even the selected query.
- **Esc is the shell's now,** for both tools; pqdoc's own Escape handler went.
- **No generation time** (FR-HTML-8): the summary gives the counts and when the workbook was last saved, from
  `docProps/core.xml` (`WorkbookFile.LastSaved`, shared with vbadoc), written in UTC and shown in the viewer's own time.
  A folder of `.m` files has no saved time. The page's title has the counts alone. `ExportContext.GeneratedAt` became
  `WorkbookSaved`, and the app's `TimeProvider` went, as nothing else needed a clock.
- **Outputs that would be written unchanged aren't asked about** (FR-OVR-1): the text outputs (page, CSV, PlantUML
  source) are made before the overwrite check, from the same queries and sources as before (a workbook's `.m` files
  come from its queries in memory), and an existing file that already holds the same bytes counts as not there. The
  check is still before anything is written, so refusing still changes nothing. Images from the PlantUML server are
  only made when written, so they're always asked about.

## Panel controls (1.8.0, step D1a of the feedback plan)

- **The details panel follows the selection unless locked,** as vbadoc's source panel does, all in the shell (see
  `shared/README.md` and vbadoc's decisions). Whether it's shown is no longer remembered, only the lock; the old
  `pqdoc-details-hidden` setting is ignored.
- **The graph keeps still when the panel opens or closes with the selection.** Before, it refitted itself to the new
  size until the person moved it, so every click and every clear zoomed it. A query clicked where the panel opens is
  centred. Resizing the panel by hand, or the window, still refits or keeps the middle as before (FR-HTML-12).
- **The copy button copies the query as its file has it, apart from the last line break,** which the code view doesn't
  show either (FR-SRC-4).

## Explorer layouts and nested groups (1.7.0, step D1b of the feedback plan)

- **Nested groups, from the user's updated samples:** after a group's description comes a byte saying whether it's
  inside another group (0 or 1), then, when it is, that group's GUID, then its order among the groups beside it, from
  0. The samples list each group after the group it's in, but the reader doesn't rely on that. D1 read the flag and
  the order as five fixed bytes, so a single nested group made the whole list fail to parse, which is why a
  production workbook showed no groups. A flag other than 0 or 1, or bytes left over, still means no groups. A parent
  that isn't in the list, or one that would make a loop, leaves the group at the top level rather than losing it.
- **When the groups can't be read, the run warns** ("Could not read the workbook's query groups …"), where D1 was
  silent: the page is still right, but the user would otherwise wonder where the groups went. An empty Metadata
  section has nothing to read, so it gives no warning.
- **A group with no queries under it isn't shown** (the samples' Empty Folder), as the explorer would have nothing to
  list in it. A group holds its groups first, in Excel's order, then its queries by name, and counts every query under
  it. Its description is its tooltip.
- **The page data's `groups` became a list of `{ name, parent, description }`,** each group before the groups inside
  it. A query's `group` and a group's `parent` are places in that list rather than names, so two groups with the same
  name in different places stay apart.
- **Two layouts, Groups and Types** (queries and functions), chosen with the explorer's switch (the shell's), which is
  remembered as `pqdoc-explorer-layout`. Groups come first when the workbook has any; otherwise there's no switch.
  Groups are drawn as folders; Other queries isn't one, so it isn't.

## Output

- **HTML is the default output** (1.1.0). Feedback from real use was that the page is what people use, and that CSV
  and PlantUML only suit very simple workbooks. `--format` lists exactly what to write, so `-f csv` writes only the CSV,
  and `--no-csv` went. The default is applied when binding options, because an empty list is how System.CommandLine
  says the option wasn't given, and its help can't show a list's default.
- **Outputs are named after what they document** (1.3.0): the workbook's name, or the folder's, and `-powerquery`,
  the same rule as vbadoc's `-vba` page (step A4 of the feedback plan). A file that's emailed, attached or printed loses
  its folder, so its own name has to say what it is, and the suffix keeps it apart from vbadoc's page for the same
  workbook. `#` and `%` become `_` in the name only. It replaced `powerquery-dependencies`, which said what the file
  contains but not what it's about.
- **Titles name what's documented, not the file:** the page's title and heading, and the PlantUML title, show the
  workbook's file name or the folder's name (`ExportContext.Title`), as vbadoc's page shows the workbook's. With
  `--output-name` the files change name but the titles don't.
- **Older outputs under another name are found through their page's generator tag**, with every output sharing its
  name. They're deleted in a workbook's output folder, which belongs to pqdoc, and only after this run wrote its outputs
  without failure, so there's always a page. In a folder of `.m` files, which belongs to the user, they're only
  mentioned. The first run after 1.3.0 clears away `powerquery-dependencies.*` beside a workbook this way.
- **`--output-name` is checked against the characters Windows forbids, and `#` and `%`, on every platform**, so the same name works
  everywhere.
- **The CSV is UTF-8 with a byte order mark**, which Excel relies on.
- **PlantUML aliases are `q1`, `q2`, … in name order**, with the real name as the label, so a name can't break the diagram.
- **There is deliberately no default PlantUML server.** The diagram includes query names, which must never reach a
  third-party service unless the user chooses that.
- **Rendering uses `GET` with the encoded diagram**, or `POST` with the plain text if the URL would be longer than 2,000
  characters. Each request times out after 30 seconds.
- **A rendered response gets a light signature check**, so an HTML error page served with status 200 isn't saved as a
  diagram.
- **Every output path is checked before anything is written**, so a refused overwrite never leaves a partial set of
  outputs.
- **Files are written to a temporary file and then moved into place**, so a failure or Ctrl+C never leaves a truncated file.
- **Overwrite prompts go to standard error**, so standard output stays clean.
- **The overwrite code is shared with vbadoc** (1.5.3, step A5 of the feedback plan): `OnExistsBehaviour`,
  `OverwriteResolver`, the console prompt and the two options moved to `shared/` unchanged in behaviour and wording,
  when vbadoc took them up. `PlannedOutput` stays in pqdoc, carrying its format, and names itself by the format in
  messages, as before.

## Page

- **Queries sit in columns by depth**, the longest chain of queries that feed them, so data flows left to right. d3's
  force layout runs within those columns. Isolated queries sit in their own column on the far left, and the queries
  of a circular dependency share a column, which keeps the depth calculation finite.
- **Placeholders in the template are filled in a single pass**, so text inserted for one is never scanned for another.
- **The page is built like vbadoc's**: the same top bar, tabs, tokens, theme switch, fonts and Content-Security-Policy.
  Its view talks to the page through `window.pqdoc`, shaped like `window.vbadoc`, so a later combined page can host both
  tools' views.
- **On the same shell as vbadoc's** (1.5.2, step C2; see [shared/README.md](../../shared/README.md)). The top bar, tabs,
  theme, details panel and splitter, Load folder and source view are the shell's; pqdoc's page keeps its view, search,
  summary and what the details panel shows. `page.html` became two fragments (`search.html`, `views.html`) that
  `HtmlExporter` gives to `ShellPage`, which keeps FR-HTML-9's template as an embedded resource, now the shell's. The
  page looks and works as before, checked pixel by pixel. The panel's ids are now `side-panel-…`; its stored size and
  visibility keep their keys, and its content keeps its `details-…` ids.
- **A query's id in the page is its name**, as a module's is in vbadoc, so `pqdoc.select` takes a name.
- **The details panel is part of the layout**, like vbadoc's source panel, not a drawer over the graph.
- **Colours were validated, not eyeballed.** Root, leaf and cycle use vbadoc's three hues, the only ones that pass with
  every pair on screen. A query in a cycle is always intermediate, so intermediate queries are neutral and orange means
  only "cycle".
- **The first picture is always the same:** the layout is computed from fixed positions before anything is drawn.
- **A circular dependency's whole group shares a column**, so the page gets its members, not its path, which is only one
  loop through the group.
- **A query clicked in the graph isn't centred; one picked anywhere else is**, so it can be found.
- **The legend starts closed in a small view**, where it would cover the graph.

## Tests

- **Integration tests need a PlantUML server** and skip themselves unless `PLANTUML_SERVER_URL` is set in the
  environment or in a `.env` file in `pqdoc/`. They are in the `Integration` category.
- **Two tests skip on this kind of machine:** one needs a case-sensitive file system, and one needs permission to
  create a symbolic link.
- **Browser tests are a separate project** (`tests/PowerQueryDocumenter.BrowserTests`), as in vbadoc. It downloads
  Playwright's Chromium on first run.
