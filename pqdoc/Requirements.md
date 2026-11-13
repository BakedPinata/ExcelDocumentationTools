# pqdoc: Requirements

## 1. Purpose and Audience

This document specifies pqdoc, a .NET console application that works out which Power Query queries reference which and exports the result. It is written for the developers and LLMs who work on the tool. Requirements use the keywords **MUST**, **SHOULD** and **MAY** as defined in RFC 2119. Each requirement has an ID so it can be referenced in review and testing.

## 2. Input and Terminology

The input is either an Excel workbook, whose queries the tool reads straight from the file (see 4.2), or a folder. From a folder, the tool reads every `*.m` file in the top level (non-recursive). Each file is one Power Query query; the query name is the file name without extension, with any escapes pqdoc wrote reversed (FR-QF-2). From a workbook, the tool also writes each query to a `.m` file (see 4.9).

### 2.1 Terminology

In graph terms, an edge runs **from the referenced (upstream) query to the referencing (downstream) query**. This document uses:

- **Upstream query / dependency**: the query being referenced.
- **Downstream query / dependent**: the query doing the referencing.

## 3. Goals and Non-Goals

**Goals**

1. Support several export formats: CSV, PlantUML source, rendered PlantUML (PDF/PNG/SVG), and a self-contained interactive HTML graph.
2. Provide a well-defined CLI with output naming, output location, format selection and overwrite behaviour.
3. Provide clear, readable console output and robust error handling.
4. Keep the internal structure such that formats are pluggable and the analysis logic is testable.

**Non-Goals**

- Full Power Query M parsing or evaluation. A lightweight tokeniser is sufficient (see FR-ANA-1).
- A GUI.
- Running or validating the queries themselves.

## 4. Functional Requirements

### 4.1 Command-Line Interface

**Synopsis**

```
pqdoc <input> [options]
```

The input, a workbook or a folder, is a required positional argument.

| ID | Requirement |
|----|-------------|
| FR-CLI-1 | The CLI MUST be implemented with `System.CommandLine` (or an equally capable parser) so that `--help`, `--version`, validation messages and usage text are generated automatically. |
| FR-CLI-2 | Every option MUST have a long name (`--output-name`) and SHOULD have a short alias where one is natural (`-n`). |
| FR-CLI-3 | Invalid or conflicting options MUST produce a clear message naming the problem and the offending option, print brief usage, and exit with code 2 (see section 5.3 for exit codes). |

**Options**

| Option | Alias | Value | Default | Description |
|--------|-------|-------|---------|-------------|
| `--output-name` | `-n` | string (no extension) | The workbook's or folder's name and `-powerquery` (FR-OUT-1) | Base file name for all outputs. The tool appends the correct extension per format. MUST NOT contain path separators, an extension, `#` or `%`; if it does, fail validation. |
| `--output` | `-o` | path | A `PowerQuery` folder next to a workbook, or the input folder | Folder to write outputs to. Created if it does not exist. |
| `--format` | `-f` | one or more of `html`, `csv`, `puml`, `pdf`, `png`, `svg` | `html` | The formats to generate. Accepts a comma-separated list (`-f html,csv`) and MAY be repeated (`-f html -f csv`). Values are case-insensitive. Giving it replaces the default. |
| `--embed-source` | | flag | off | Put each query's M code inside the page, so it shows without Load folder. See FR-SRC-2. |
| `--plantuml-server` | | URL | none | Base URL of the PlantUML server used for `pdf`, `png` and `svg` rendering. See FR-PUML-4. |
| `--on-exists` | | `prompt`, `overwrite`, `skip`, `fail` | `prompt` when interactive, otherwise `fail` | Behaviour when an output file already exists. See FR-OVR. |
| `--force` | `-y` | flag | off | Shorthand for `--on-exists overwrite`. If both are given and disagree, fail validation. |
| `--verbose` | `-v` | flag | off | Show debug-level log output. |
| `--quiet` | `-q` | flag | off | Show warnings and errors only. Mutually exclusive with `--verbose`. |

**Examples**

```
# Interactive HTML only, default name, next to the inputs
pqdoc C:\queries

# HTML plus CSV and PlantUML source, custom name and location
pqdoc C:\queries -n sales-model -o C:\docs -f html,csv,puml

# SVG and PNG only, overwriting silently
pqdoc C:\queries -f svg,png --plantuml-server https://plantuml.example.com --force
```

### 4.2 Reading Workbooks

| ID | Requirement |
|----|-------------|
| FR-IN-1 | The input MAY be a workbook or a folder. An input that is a file is taken as a workbook. A workbook's outputs go in a `PowerQuery` folder next to it unless `--output` says otherwise. |
| FR-IN-2 | A workbook's queries MUST be read from the file itself, without Excel, following [MS-QDEFF](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-qdeff/27b1dd1e-7de8-45d9-9c84-dfcc7a802e37): the custom XML part whose root is `DataMashup` (found by its root, not its name), then the Package Parts section and its `Formulas/Section1.m`. `.xlsx`, `.xlsm` and `.xlsb` MUST all work. The file MUST be opened for shared reading, so it can be read while open in Excel. The other sections MUST NOT be read beyond checking their lengths. |
| FR-IN-3 | `Section1.m` MUST be split into queries with the M tokeniser (FR-ANA-1), so that text and comments can't split a query. Each query's text is its expression as Excel stores it, without `shared <name> =` or the final `;`. A description (an attribute record before the query) is kept, but isn't part of the expression. A member without `shared` is still a query. |
| FR-IN-4 | Each of these MUST stop the run with exit code 3 and a clear message, before anything is written: a file that isn't a workbook; an encrypted workbook or an older `.xls`; a workbook with no queries; damage (a length longer than the bytes left, invalid base64, a missing `Section1.m`, a section that can't be split); two queries with the same name, ignoring case. |

### 4.3 Dependency Analysis

| ID | Requirement |
|----|-------------|
| FR-ANA-1 | Reference detection SHOULD use a small M-aware tokeniser rather than regular expressions over the whole file. It MUST ignore content inside line comments (`//`), block comments (`/* */`) and string literals (`"..."`, with `""` as an escaped quote), and MUST recognise quoted identifiers (`#"Name with spaces"`, including `""` escapes) and bare identifiers. |
| FR-ANA-2 | The built-in keyword/function filter MUST apply only to bare identifiers that are **not** the name of an existing query. A query that happens to be named `Source`, `Table` or `Value` MUST still be detected when referenced. |
| FR-ANA-3 | Query-name comparison MUST be consistently case-insensitive across the known-query set, reference matching and de-duplication. |
| FR-ANA-4 | Self-references MUST be ignored for edge generation. |
| FR-ANA-5 | The analyser MUST detect circular dependencies and report each cycle as a warning listing the queries involved. Cycles MUST NOT cause crashes or infinite loops, and exports MUST still be produced. |
| FR-ANA-6 | The analyser MUST compute, per query, whether it is a **root** (no upstream queries), a **leaf** (no dependents), or **isolated** (neither), for use by the exporters and a console summary. |
| FR-ANA-7 | The analysis MUST be separated from exporting: it produces an immutable in-memory dependency graph (nodes plus directed edges) that all exporters consume. Exporters MUST NOT re-read files. |
| FR-ANA-8 | Files MUST be read as UTF-8 (with BOM detection). A file that cannot be read MUST be logged as an error and skipped, and the run continues (see FR-ERR). |
| FR-ANA-9 | A `--recursive` option MAY be added later; it is out of scope for version 1.0.0. Scanning is top-level only. |

### 4.4 Export: CSV

| ID | Requirement |
|----|-------------|
| FR-CSV-1 | The header MUST be `Query Name,Referenced By Query Name`. The first column is the upstream query being referenced; the second is the downstream query that references it. Values are quoted, with `""` escaping; there is one row per pair; queries with no dependents are emitted with an empty second column; rows are ordered case-insensitively. The header strings MUST be defined as constants in one place. |
| FR-CSV-2 | File name is `<output-name>.csv`. |
| FR-CSV-3 | CSV export is produced only when `csv` is one of the `--format` values. |

### 4.5 Export: PlantUML Source (`puml`)

| ID | Requirement |
|----|-------------|
| FR-PUML-1 | File name is `<output-name>.puml`, UTF-8 without BOM, wrapped in `@startuml` / `@enduml`. |
| FR-PUML-2 | Every query MUST appear as a node (including isolated ones), with a safe alias. Aliases MUST be generated deterministically (for example `q1`, `q2` in sorted order) with the real name as the display label, so names containing spaces, quotes or special characters do not break the diagram. Display labels MUST be escaped for PlantUML. |
| FR-PUML-3 | Edges MUST point from upstream to downstream (`upstream --> downstream`). The diagram SHOULD use `left to right direction` and distinct styling for roots, leaves and isolated queries. A legend explaining the styling SHOULD be included. Queries in a cycle SHOULD be visually flagged. |
| FR-PUML-4 | **Rendering (`pdf`, `png`, `svg`)**: the tool generates the PlantUML source in memory and sends it to a PlantUML server for rendering. The server URL is resolved in this order: (1) `--plantuml-server`, (2) environment variable `PLANTUML_SERVER_URL`, (3) a `.env` file containing `PLANTUML_SERVER_URL=...` in the current working directory. If none is found and any render format was requested, validation MUST fail with a message explaining the three ways to supply it. There is **no built-in default server**: diagram content (query names) must not be sent to a third-party public service without the user explicitly configuring it. |
| FR-PUML-5 | Rendering MUST use the PlantUML server's standard HTTP API. The diagram text is encoded per the PlantUML text-encoding scheme (raw DEFLATE then PlantUML's base64 variant) and requested from `{server}/{png\|svg\|pdf}/{encoded}`, or the server's POST endpoint if the encoded URL would exceed a safe length (about 2,000 characters). The implementation MUST choose the approach that works for large graphs and document its choice in code comments. |
| FR-PUML-6 | A render request MUST have a timeout (default 30 seconds), and failures (network error, non-2xx, empty body) MUST be reported per format without aborting the other exports. The server URL MUST be validated as an absolute `http(s)` URL. |
| FR-PUML-7 | Requesting a render format does **not** also write the `.puml` file unless `puml` is also requested. |
| FR-PUML-8 | Output files are `<output-name>.pdf`, `<output-name>.png`, `<output-name>.svg`. |

### 4.6 Export: Interactive HTML (`html`)

| ID | Requirement |
|----|-------------|
| FR-HTML-1 | File name is `<output-name>.html`. The file MUST be a single self-contained document (inline CSS, JS and data) that works offline when opened from disk. The D3.js library (v7, minified) MUST be embedded inline rather than loaded from a CDN. Embed it from a vendored copy stored in the repository (for example as an embedded resource) and note its version and licence in a comment. |
| FR-HTML-2 | The graph MUST be a D3 force-directed network with directed edges (arrowheads), edges running upstream to downstream, consistent with the CSV and PlantUML exports. |
| FR-HTML-3 | Interaction: pan and zoom; drag nodes; hovering a node shows its name and counts of upstream and downstream queries and highlights its direct neighbours; clicking a node pins/highlights its full upstream and downstream chains (transitive); clicking the background clears the selection. |
| FR-HTML-4 | A search box MUST filter/highlight nodes by name (case-insensitive substring). A "reset view" control SHOULD be provided. |
| FR-HTML-5 | Nodes SHOULD be coloured by role (root, leaf, isolated, in-cycle) with a legend. Labels MUST be legible; long names SHOULD be truncated with the full name in a tooltip. |
| FR-HTML-6 | The page MUST adapt to light/dark colour scheme via `prefers-color-scheme` and be usable on a laptop-sized viewport. |
| FR-HTML-7 | Graph data MUST be embedded as JSON with proper escaping (including `</script>` sequences in names) so that query names cannot break out of the page or inject script. |
| FR-HTML-8 | The page title and a visible heading SHOULD name what is documented (the workbook's file name or the folder's name), and the title SHOULD include the node and edge counts. The top bar's summary SHOULD give the counts and, for a workbook that records it, when the workbook was last saved, from its own document properties, shown in the viewer's own time. The page MUST NOT say when it was made, so the same input always gives the same page. |
| FR-HTML-9 | The HTML template SHOULD be kept as a separate embedded resource file rather than an inline C# string. |
| FR-HTML-10 | HTML is the default output: it is the only output when `--format` isn't given. |
| FR-HTML-11 | The page MUST have an explorer: a panel on the left listing every query. When the workbook has query groups, it lists them by default as Excel's Queries pane does: nested to any depth, in Excel's order, each group holding its groups and then its queries, drawn as folders, with the other queries under Other queries; a switch shows queries and functions apart instead, and the choice is remembered. Without groups (or for a folder of `.m` files) it lists queries and functions apart. A workbook's metadata says which queries are functions; for a folder, a query counts as one when its code is a function literal, `each …`, or a `let` whose result is one of its steps defined as a function. Metadata that can't be read MUST NOT stop the run: the queries are then simply ungrouped, and the run warns that the groups couldn't be read. A filter box narrows the list; choosing a query selects it, and the explorer follows selections made in the graph. It can be hidden and resized, and is open by default on a wide screen. |
| FR-HTML-12 | The details panel MUST open when a query is selected and close when the selection is cleared, unless locked open with the lock in its corner, which is remembered. Its button at the right end of the top bar still opens and closes it by hand, and closing it by hand also unlocks it. When it opens over the query clicked, the graph moves the query into sight; otherwise the graph keeps still. The explorer's button is at the left end of the top bar; both are icons, with their names in a tooltip. |
| FR-HTML-13 | Focus on click: a Focus button at the start of the graph's toolbar, on by default and remembered, under which selecting a query (in the graph, the explorer, search or the details panel) shows only it and its upstream and downstream chains, fitted to the view; clearing the selection shows every query again. Turning it on focuses on the current selection too. Ctrl+click (Cmd+click on a Mac) does the other thing for that click. Esc clears the selection; an open menu, or a box with text in it, takes Esc first. It works the same as vbadoc's, in every view. |
| FR-HTML-14 | Favourites: any number of queries, starred with the star on an explorer row (which doesn't select it) or beside the name in the details panel, MUST be listed at the top of the explorer in every layout, in the order they were starred, and starred in the graph. They're remembered per workbook (or folder) in the browser; one that's no longer there is ignored. They work as vbadoc's do. |
| FR-HTML-15 | Showing less, from the explorer: Expand all and Collapse all beside its filter box; and a checkbox on every row, apart from the name, so that while anything is ticked the graph MUST show only the ticked queries. Ticking a group ticks the queries in it. Ticking MUST NOT select, nor selecting tick, and Esc clears the selection, not the filter. A bar in the explorer and a note in the graph's toolbar MUST say what's shown ("Filtered: 5 of 14 queries"), each with Clear; unticking takes a query out. Space ticks from the keyboard. The ticks are remembered per workbook (or folder) in the browser. They work as vbadoc's do. |
| FR-HTML-16 | Hiding and Focus on favourites: a query hidden with the button beside the star on its explorer row or in the details panel's header MUST be taken out of the graph with its links, even when ticked. It stays in the explorer, struck through, and is listed in a Hidden group at its foot, to show again one at a time or all at once; the graph's toolbar counts hidden queries; and the details panel says when the selected query is hidden, or how many of the queries it uses or is used by are, tagging them. Remembered per workbook (or folder). Focus on favourites, turned on from the Favourites group, MUST show only the favourites and what they use, as well as the focused query's chains, with a note in the toolbar and Show all. They work as vbadoc's do. |
| FR-HTML-17 | The explorer MUST have a Show… menu, shared with vbadoc, choosing whether the tree and the graph show queries and functions, and whether the graph shows queries with no connections (unless ticked). The button counts the choices in effect, as does a note in the graph's toolbar, which opens the menu, with Show everything; Show nothing clears every box, and each section has All and None. Remembered per browser; everything is shown to begin with. |
| FR-HTML-18 | Widely used queries: those used directly by at least a share of the other queries (20% to begin with, set in the Show… menu and remembered) MUST be listed in a Widely used group near the foot of the explorer, most used first, and marked where they are, with how many queries use them. Hide all hides them all, and shows them again once they're all hidden. Nothing is hidden to begin with. They work as vbadoc's do. |
| FR-HTML-19 | The details panel's Uses and Used by lists MUST list each query once, and opening one MUST list the lines where it's named (in the selected query's source for Uses, in the other query's for Used by): the line number and, when the source is there, the line's text. Going to one (FR-HTML-22) MUST select the query that holds the line, scroll its source to the line and mark it. Without the source, the line numbers still show. The analyser records each reference's line, as vbadoc's does. |
| FR-HTML-20 | The details panel MUST list the selected query's steps above its source: the steps of its own outermost `let` (at the top, or the body of a function query) in order, each with its name, a link to its line, and the queries and functions it uses, then the `in` expression. A `let` inside a step adds no steps, and a query without a `let` of its own has none (its Uses list says what it uses). |
| FR-HTML-21 | Lock view: a button at the start of the graph's toolbar, beside Focus, MUST keep what the graph shows as it is: the queries shown, where they are and the zoom. While it's on, a click on a query or on empty space MUST change only the selection and the details panel; the selected query is outlined, and nothing is focused, lit, dimmed, fitted or centred. Locking ticks every query shown in the explorer, so the explorer and the toolbar's note ("Locked: 7 of 14 queries") say what's kept and a tick still adds or drops a query; unlocking puts the ticks back as they were. Dragging a query still moves it. Remembered per workbook (or folder), and kept in a saved view. It works as vbadoc's does. |
| FR-HTML-22 | Peek: a click on a line in the Uses or Used by list MUST open a preview under it, of the line and 5 lines either side of it in the query that holds it, highlighted, with the line marked, and MUST leave the selection, the graph and the details panel's source as they are. Go to in the preview, or Ctrl+click on the line, goes to the line (FR-HTML-19). A query longer than the preview can be shown whole in it. Any number of previews can be open; a click on its line closes one, and Esc closes the last opened before it clears the selection. Without the source, the preview says so, with Load folder. A step's line (FR-HTML-20) still goes straight to the line. It works as vbadoc's does. |
| FR-HTML-23 | A views file in the output folder MUST be built into the page: pqdoc reads `pqdoc-views.json` from the folder it writes to, when it writes the page, and the page then has the file's views for everyone who opens it, listed first in the Views menu and marked "in the page". A missing file is the usual case. A file that can't be used (not JSON, not a views file, another tool's, or from a newer pqdoc) MUST be a warning and never a failure, with the page made without it. pqdoc MUST NOT write, rename or delete the file, and the same input and file MUST give the same page. In the page, the page's views can't be edited or deleted; saving over one makes the person's own version, kept in the browser and marked "yours", until it's deleted, and Export writes every view listed. It works as vbadoc's does. |
| FR-HTML-24 | Descriptions: the details panel MUST show what a query says about itself, under its role, as text: its description in the workbook (the one Excel's query properties show), otherwise the comments its code starts with (`//` lines and `/* */` blocks before anything else), with lines of decoration left out. A query with neither has none. A description is cut at 1,000 characters, is worked out when the page is made, and is in the page's data. |
| FR-HTML-25 | Printing: a Print menu in the top bar MUST make a document and hand it to the browser's Print, whose Save as PDF makes a PDF: This view (the page as arranged now), or Every saved view, each from a new page, the page opening each in turn and going back to how it was. The document has the title, the page's summary and the build of pqdoc that made the page (never when), then for each view its name, description, what it shows, the graph fitted to the page's width in the light theme, and each query the graph shows with its kind, role, description and what it uses and is used by; and its M code when Include each item's source is ticked and the source is there. It works as vbadoc's does. |

### 4.7 Overwrite Behaviour

| ID | Requirement |
|----|-------------|
| FR-OVR-1 | Existence is checked **per output file**, before any file is written, so that a refused overwrite does not leave a partial set of outputs. A text output (HTML, CSV, PlantUML source) that would be written unchanged has nothing to overwrite, so it isn't asked about; rendered images are always asked about, since they're only made when written. |
| FR-OVR-2 | `prompt`: if stdin/stdout are interactive, ask per file with options *yes / no / all / quit*. If the session is not interactive (redirected input, CI), `prompt` MUST behave as `fail` and the error message MUST say how to proceed (`--force` or `--on-exists`). |
| FR-OVR-3 | `overwrite`: replace silently (logged at information level). `skip`: leave the existing file and log a warning. `fail`: abort before writing anything and exit with code 4. |
| FR-OVR-4 | Writes SHOULD be atomic where practical (write to a temporary file in the target folder, then move over the destination) so that a failure never leaves a truncated file. |

### 4.8 Output Names

| ID | Requirement |
|----|-------------|
| FR-OUT-1 | Outputs are named after what they document, in the same way as vbadoc's page: the workbook's name, or the folder's, followed by `-powerquery` (`Sales.xlsx` gives `Sales-powerquery.html`). `#` and `%` become `_` in the name, because they break or confuse a browser's address; titles still show the real name. A drive's root, which has no name, gives `powerquery`. `--output-name` overrides the name, and MUST NOT contain `#` or `%`. |
| FR-OUT-2 | Outputs pqdoc wrote earlier under another name are found through their page's generator tag, with every output sharing that page's name. In a workbook's output folder, which belongs to pqdoc, they MUST be deleted after this run's outputs are written without failure, so an old page can't pass for the current one. In a folder of `.m` files, which belongs to the user, they MUST only be mentioned. |

### 4.9 Query Files

| ID | Requirement |
|----|-------------|
| FR-QF-1 | With a workbook as input, each query MUST be written to its own `.m` file in the output folder, holding its expression as Excel stores it, without `shared <name> =` or the final `;` (what the Advanced Editor shows and a manual export contains). A folder as input writes no query files. |
| FR-QF-2 | File names MUST be reversible: the characters Windows doesn't allow in a file name, control characters and `%` itself become `%XX` (upper-case hex), and a Windows device name (`CON`, `LPT1`, …) has its first letter escaped. So `Sales/Returns` is `Sales%2FReturns.m` and `CON` is `%43ON.m`. Reading a folder MUST reverse exactly those escapes and nothing else, so a hand-made file with some other `%` in its name keeps its name. |
| FR-QF-3 | Files MUST be UTF-8 without a BOM, with CRLF line endings outside text literals. Line breaks inside a text literal MUST stay as Excel stored them, so the text is unchanged if the query is imported back. |
| FR-QF-4 | The folder MUST be kept in step with the workbook: unchanged files aren't rewritten (so they keep their timestamps), top-level `.m` files whose query is no longer in the workbook are deleted, other files are left alone, and a `.gitattributes` (`*.m -text diff`, `*.html -text diff`) is created if there isn't one, never overwritten. `--on-exists` doesn't apply to query files. They're written after the overwrite check and before the outputs. Names that would share a file stop the run with exit code 3 before anything is written; a failure to write them gives exit code 5. |

### 4.10 Source in the Page

| ID | Requirement |
|----|-------------|
| FR-SRC-1 | Selecting a query in the page MUST show its M code in the details panel, with line numbers and highlighting of comments, text, quoted names (`#"Sales Data"`), keywords (including `#date`-style constructors), numbers and type names. Highlighting MUST stay quick on a very long line, such as an Enter Data query's compressed table. |
| FR-SRC-2 | By default the page MUST NOT contain M code, which can hold server names, file paths and other details. It holds each query's `.m` file name and the first 16 hex digits of the file's SHA-256, and **Load folder** reads the files from a folder the user picks. With `--embed-source` the page holds each query's M code instead, from the folder's files or from the files written from the workbook, HTML-escaped so it can't break out of the page. A query with no readable file has no source, and a page with none offers no source at all. |
| FR-SRC-3 | After Load folder, the page MUST say how many files it loaded, name any that are missing, and warn when a file's hash doesn't match the one taken when the page was made. Files MUST be read as UTF-8 unless they start with a UTF-16 byte order mark. |
| FR-SRC-4 | A button over the M code MUST copy it to the clipboard as plain text, without line numbers, and stay in view as the code scrolls. If the browser won't allow it, the code is selected for Ctrl+C instead. |

### 4.11 Choosing What to Import

| ID | Requirement |
|----|-------------|
| FR-IMP-1 | The page's Export tab MUST list every query, in the explorer's layout, whatever the filter, hiding and Show… choices are doing. Ticking a group picks all that's in it, and it can start from what's ticked in the explorer. The choice MUST be remembered per workbook, apart from the explorer's filter. |
| FR-IMP-2 | As the choice changes, the tab MUST show what was chosen, what comes along because something chosen uses it (functions and parameters are queries, so they come too), and what to check in the target. |
| FR-IMP-3 | What to check MUST list what doesn't come along, and never block anything: a table or named range `Excel.CurrentWorkbook()` reads, and each function that reaches a data source, by its name only, as a query's server names and paths stay out of the page (FR-SRC-2). |
| FR-IMP-4 | The manifest, `pqdoc-export.json`, MUST be downloaded from the tab and loadable back into it. It holds what was picked and the workbook's name. A manifest for another tool, or from a newer pqdoc, MUST be refused, saying why. |
| FR-IMP-5 | `pqdoc import <input>` MUST take what to take from `--manifest <file>` or `--items A,B`, one or the other, from a workbook or a folder of `.m` files, and say what it comes to, as the tab does. A query the input doesn't have MUST stop it with exit code 3; a manifest made from another workbook is only mentioned. |
| FR-IMP-6 | `--bundle <folder>` MUST write the queries' `.m` files with a `README.md` on bringing them in by hand, each query after those it uses. Without it, `--new` or `--into`, nothing is written. A bundle MUST go only into an empty folder or an earlier bundle, and a folder of other files MUST stop it with exit code 4. Replacing an earlier bundle that would change follows `--on-exists` and `--force`. |
| FR-IMP-7 | `--new <file>` MUST write a new workbook with one sheet, holding the queries. The file has to be an `.xlsx` or `.xlsm`; anything else stops with exit code 2. |
| FR-IMP-8 | `--into <file>` MUST take an `.xlsx`, `.xlsm` or `.xlsb` as the target and write a copy beside it, `<target>-imported`, with the target's extension. An encrypted workbook, an `.xls` and anything else MUST stop it with exit code 3. Only one of `--new`, `--into` and `--bundle` can be given. `--on-exists` and `--force` apply to the file written. `--new` can't name the source, and neither can a target changed in place: exit code 2. |
| FR-IMP-9 | Neither workbook MUST be changed, byte for byte, unless `--in-place` is given. Everything in the target MUST stay as it was but what has to change: the queries' text, their metadata and the connections. A sensitivity label that's only metadata stays. |
| FR-IMP-10 | A new query MUST come as connection only, with its description and what it returns, and a connection, except in an `.xlsb`, whose connections are in a binary part. Its query group doesn't come. |
| FR-IMP-11 | A query the target has word for word, ignoring line endings and what ends it, under the same spelling of its name, MUST be left alone without asking, and counted. When every query is, nothing is written. |
| FR-IMP-12 | A name the target has for something else is a clash, ignoring case. Each MUST be asked about before anything is written, saying what's at stake: what else in the target uses it, and what's coming that would use the target's version. The answers are Skip, Overwrite, Skip All, Overwrite All and Quit, which stops with nothing written and exit code 4. A clash can be with a query that came along. `--on-clash overwrite\|skip` answers for every clash. Without it and with nobody to ask, a clash MUST stop the run with exit code 4. `--force` doesn't answer a clash. |
| FR-IMP-13 | Overwrite MUST replace the query's M and description, and keep the target's spelling of its name, where it loads to and its connection. |
| FR-IMP-14 | `--in-place` MUST change the target itself, saying what will change and asking first; a no stops with exit code 4. `--force` answers yes. With nobody to ask and no `--force`, it MUST stop with exit code 4. The target MUST be replaced in one step once the result is finished and read back, keeping its permissions, so a failure leaves it as it was. One that can't be had to the tool alone, as when Excel has it open, MUST stop with exit code 5 before anything is asked. `--on-exists` can't be given with it. |
| FR-IMP-15 | What's written MUST be read back before anything is replaced or created: every query the result should hold has to be there, word for word, or the run stops with exit code 3 and nothing written. |
| FR-IMP-16 | A target set to ignore privacy levels MUST be warned about when its queries change, as Excel may no longer trust that setting. |

## 5. Non-Functional Requirements

### 5.1 Logging (Console UX)

| ID | Requirement |
|----|-------------|
| NFR-LOG-1 | Diagnostics MUST use **Serilog** with a console sink, a coloured theme and a concise output template (timestamp optional, level, message). |
| NFR-LOG-2 | Default level is Information; `--verbose` sets Debug; `--quiet` sets Warning. |
| NFR-LOG-3 | Use structured message templates (`"Wrote {Format} to {Path}"`), not string interpolation. |
| NFR-LOG-4 | A short summary MUST be logged on completion: queries found, dependency edges, roots, leaves, isolated queries, cycles found, and a list of files written/skipped/failed. |
| NFR-LOG-5 | Logs go to stderr; nothing but intended output goes to stdout. Logging MUST be flushed on exit (`Log.CloseAndFlush`). |
| NFR-LOG-6 | Respect `NO_COLOR` and disable colour when output is redirected. |

### 5.2 Error Handling

| ID | Requirement |
|----|-------------|
| FR-ERR-1 | Errors MUST be handled specifically rather than by a single catch-all: argument/validation errors, input folder problems (missing, unreadable), per-file read errors, output errors (permission denied, disk full, invalid path), render/network errors. |
| FR-ERR-2 | Failures are **isolated per export**: if one format fails, the others still run, and the final exit code reflects the failure. |
| FR-ERR-3 | Finding zero `.m` files MUST be treated as an error (exit code 3) with a clear message, not as a successful run. The success message MUST NOT be printed when nothing was produced. |
| FR-ERR-4 | Messages shown to the user MUST be actionable and free of stack traces. Full exception detail is logged at Debug level (`--verbose`). |
| FR-ERR-5 | Unexpected exceptions MUST be caught at the top level, logged, and mapped to exit code 1. |
| FR-ERR-6 | Support cancellation (Ctrl+C) cleanly: stop work, remove temporary files, exit code 130. |

### 5.3 Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All requested outputs produced (skips due to `--on-exists skip` are not failures) |
| 1 | Unexpected error |
| 2 | Invalid arguments or configuration |
| 3 | Input problem (input missing, no `.m` files, a workbook whose queries can't be read: see FR-IN-4; queries that would share a file: see FR-QF-4) |
| 4 | Output refused (existing file with `fail` behaviour) |
| 5 | One or more exports, or the query files, failed (others may have succeeded) |
| 130 | Cancelled by user |

### 5.4 Code Structure and Quality

| ID | Requirement |
|----|-------------|
| NFR-ARCH-1 | Formats MUST sit behind an exporter abstraction, for example `IExporter` with `Format`, `FileExtension` and `ExportAsync(DependencyGraph, ExportContext, CancellationToken)`. Each format is its own class (`CsvExporter`, `PlantUmlSourceExporter`, `PlantUmlRenderExporter`, `HtmlExporter`). Adding a new format MUST NOT require changing the analyser. |
| NFR-ARCH-2 | The model MUST use small, immutable types (`DependencyGraph`, `QueryNode`, `DependencyEdge`) and an options record (`AnalyserOptions`) produced from parsed CLI input. `DependencyAnalyser` MUST NOT hold mutable instance state; analysis returns a result. |
| NFR-ARCH-3 | Use `Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Hosting` only if it keeps the code simpler; otherwise manual composition in `Program.cs` is acceptable. Keep `Program.cs` thin. |
| NFR-ARCH-4 | All file and network I/O MUST be async where the API supports it, and use a single shared `HttpClient`. |
| NFR-ARCH-5 | Nullable reference types MUST be enabled and warnings treated as errors. Code uses 2-space indentation, file-scoped namespaces and collection expressions. |
| NFR-ARCH-6 | Keep third-party dependencies minimal and list each one with its purpose in the README. Expected: Serilog (+ console sink), System.CommandLine, and a `.env` reader (a small hand-written parser is acceptable instead of a package). |

### 5.5 Testing

| ID | Requirement |
|----|-------------|
| NFR-TEST-1 | The xUnit test project MUST cover: the tokeniser (comments, strings, escaped quotes, quoted identifiers, queries named like built-ins), graph building, cycle detection, role classification, CSV output (against a fixture, including the header), PlantUML alias/escaping, PlantUML encoding, HTML JSON escaping, option validation, overwrite behaviour, and exit-code mapping. |
| NFR-TEST-2 | Tests MUST use temporary folders and a fake/stub HTTP handler for the PlantUML server. No test may need real network access, apart from the browser tests' one-off download of Chromium on their first run. |
| NFR-TEST-3 | Test fixtures MUST include a sample set of `.m` files with a cycle, an isolated query, and awkward names, and sample workbooks (`.xlsx` and `.xlsb`) made in Excel with made-up data. Damaged workbooks are built by the tests. |

### 5.6 Documentation

| ID | Requirement |
|----|-------------|
| NFR-DOC-1 | `README.md` MUST cover: purpose, installation/build, full option reference, examples, exit codes, PlantUML server configuration (flag, environment variable, `.env`), and a note on the privacy implications of using a remote PlantUML server and of `--embed-source`. |

## 6. Acceptance Criteria (Summary)

1. `pqdoc <folder>` with no other options writes `<folder name>-powerquery.html` next to the inputs, and nothing else.
2. `-f csv,puml,html` produces `.csv`, `.puml` and `.html` side by side. The CSV has the header row `Query Name,Referenced By Query Name`. The HTML opens offline and renders an interactive graph. The PUML renders without syntax errors in a PlantUML server.
3. `-f csv` writes only the CSV, because `--format` replaces the default.
4. Existing output files with default settings in a non-interactive session cause exit code 4 and no files are written or modified.
5. Rendering formats without a configured server fail with exit code 2 and a message that names all three configuration routes.
6. A query named `Source` that is referenced by another query appears as a dependency.
7. A query name appearing only inside a comment or string literal creates no dependency.
8. A circular dependency produces a warning and all exports still complete.
9. A render failure for PNG does not prevent SVG, HTML or CSV from being written, and the exit code is 5.
10. `pqdoc <workbook>` reads the queries straight from the workbook and writes `<workbook name>-powerquery.html` in a `PowerQuery` folder next to it. The dependencies are the same as for the same queries exported by hand.
11. With a workbook as input, each query is written to its own `.m` file beside the outputs. Running pqdoc on that folder afterwards gives the same query names and dependencies.
12. Without `--embed-source` the page contains no M code, and after Load folder on the folder of `.m` files, selecting a query shows its code, highlighted. With `--embed-source` the code shows without Load folder.

## 7. Decisions Made (Defaults Chosen)

These are deliberate defaults.

- **Default output name** is the workbook's or folder's name and `-powerquery` (FR-OUT-1). Until 1.3.0 it was `powerquery-dependencies`.
- **Overwrite default** is *prompt when interactive, fail when not*, with `--force` / `--on-exists` to override. This is the safest default and still scriptable.
- **No default PlantUML server**, to avoid sending query names to a public service unintentionally.
- **D3 is embedded** in the HTML so the file works offline and can be shared by email or from a file share without external requests.
- **Edge direction** is upstream to downstream across all formats.
- **HTML is the default output**, and `--format` lists exactly what to write. Until 1.1.0 the CSV was the default and
  `--format` added to it, but the page turned out to be the output people use.
- **CSV header** is `Query Name,Referenced By Query Name`, so each row says plainly which query uses which.
- **M code isn't embedded in the page by default** (FR-SRC-2), so a page that's emailed or shared doesn't carry server
  names and file paths unless the user asks for that with `--embed-source`, as vbadoc does with VBA.

## 8. Out of Scope / Possible Future Work

- Recursive folder scanning (`--recursive`).
- Local PlantUML rendering via a Java/`plantuml.jar` or Docker invocation.
- Additional formats (Mermaid, Graphviz DOT, GraphML, JSON).
- Reading queries from Power BI files (`.pbix`, `.pbit`).
- Filtering the graph by query name or depth (for example, "show everything downstream of `X`").
- Configuration file support (`appsettings.json`) for persisting defaults.
