# pqdoc: Power Query Documentation Tool

A .NET console tool that reads the Power Query queries in an Excel workbook, or in a folder of
exported `.m` files, works out which queries reference which, and exports the dependency graph as:

- **Interactive HTML** (default): a single, self-contained page with a D3 force-directed graph that
  works offline.
- **CSV**: one row per dependency.
- **PlantUML source** (`.puml`).
- **Rendered diagrams** (`.svg`, `.png`, `.pdf`) via a PlantUML server you configure.

It also reports circular dependencies and classifies each query as a root, leaf, isolated or
intermediate query.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The target framework is set once in the root
  `Directory.Build.props` (`net10.0`). Visual Studio 2022 doesn't support .NET 10, so build there from the command line
  (see the [root README](../README.md#building-and-testing)).
- For `svg`, `png` and `pdf` only: access to a PlantUML server (see below).

## Build and run

From the `pqdoc` folder:

```
dotnet build
dotnet run --project src/PowerQueryDocumenter -- C:\queries
```

To publish pqdoc as a single self-contained executable, into the folder shared by every tool:

```
dotnet publish src/PowerQueryDocumenter -c Release -p:PublishProfile=FolderProfile
```

`publish.ps1` does this for every tool; see the [root README](../README.md#publishing).

## Usage

```
pqdoc <input> [options]
```

`<input>` is either:

- **an Excel workbook** (`.xlsx`, `.xlsm` or `.xlsb`). pqdoc reads the queries straight from the
  file, with no export needed. It doesn't need Excel installed, and works while the workbook is open in
  Excel. Outputs go in a `PowerQuery` folder next to the workbook, with each query in its own `.m`
  file (see [Query files](#query-files)); or
- **a folder of exported `.m` files**, one query per file. The query name is the file name without
  `.m`, and only the top level of the folder is scanned. Outputs go in the same folder.

An encrypted workbook (password, IRM or sensitivity label) can't be read: save a copy without
encryption and run pqdoc on that.

Outputs are named after what they document: `Sales.xlsx` gives `Sales-powerquery.html` (and `.csv`,
`.puml` and so on), and a folder called `queries` gives `queries-powerquery.html`. `#` and `%` in the
name become `_`, because they confuse a browser's address. Beside a workbook, pqdoc deletes outputs it
wrote earlier under another name, such as `powerquery-dependencies.html` from before 1.3.0 or a
renamed workbook's page. In a folder of `.m` files it only mentions them.

| Option | Alias | Value | Default | Description |
|--------|-------|-------|---------|-------------|
| `--output-name` | `-n` | name | the workbook's or folder's name and `-powerquery` | Base file name for every output. The extension is added per format. Must not contain a folder, an extension, `#` or `%`. |
| `--output` | `-o` | path | a `PowerQuery` folder next to a workbook, or the input folder | Where outputs are written. Created if missing. |
| `--format` | `-f` | `html`, `csv`, `puml`, `pdf`, `png`, `svg` | `html` | The formats to write, comma-separated (`-f html,csv`) or repeated (`-f html -f csv`). Case-insensitive. Replaces the default, so include `html` to keep the page. |
| `--embed-source` | | flag | off | Put each query's M code inside the page, so it shows without Load folder. See [the privacy note](#source-in-the-page). |
| `--plantuml-server` | | URL | none | PlantUML server for `pdf`, `png` and `svg`. |
| `--on-exists` | | `prompt`, `overwrite`, `skip`, `fail` | `prompt` when interactive, otherwise `fail` | What to do when an output file already exists. |
| `--force` | `-y` | flag | off | Same as `--on-exists overwrite`. |
| `--verbose` | `-v` | flag | off | Show debug output, including exception details. |
| `--quiet` | `-q` | flag | off | Show warnings and errors only. |
| `--help` | `-h`, `-?` | | | Show help. |
| `--version` | | | | Show the version and git commit of this build. |

### Examples

```
# The interactive page for a workbook's queries: C:\books\PowerQuery\Sales-powerquery.html
pqdoc C:\books\Sales.xlsx

# The interactive page for a folder of .m files: C:\queries\queries-powerquery.html
pqdoc C:\queries

# The page with each query's M code inside it, so it shows without Load folder
pqdoc C:\books\Sales.xlsx --embed-source

# The page plus CSV and PlantUML source, custom name and location
pqdoc C:\queries -n sales-model -o C:\docs -f html,csv,puml

# SVG and PNG only, overwriting silently
pqdoc C:\queries -f svg,png --plantuml-server https://plantuml.example.com --force
```

## Outputs

**CSV** (`<name>.csv`): UTF-8 with a byte order mark (so Excel opens it correctly), every value
quoted, one row per (query, dependent) pair, and queries with no dependents listed with an empty
second column. The columns are `Query Name` (the query being referenced, upstream) and
`Referenced By Query Name` (the query that references it, downstream). Rows are sorted by query
name, case-insensitively.

**PlantUML source** (`<name>.puml`): every query is a node with a short alias (`q1`, `q2`, ... in
name order) and its real name as the label, escaped so that quotes and PlantUML markup in names
are shown literally. Arrows run from the referenced query to the query that uses it, left to
right. Roots, leaves, isolated queries and queries in a circular dependency are styled
differently, with a legend.

**Rendered diagrams** (`<name>.svg`, `.png`, `.pdf`): the same diagram, rendered by your PlantUML
server. Requesting these does not write the `.puml` file unless you also ask for `puml`.

**Interactive HTML** (`<name>.html`): one file with everything inlined (styles, script, data and
D3.js), so it works offline and can be emailed or shared from a file share. Queries are laid out
left to right by depth (how many queries feed them). You can:

- find a query in the explorer on the left, which lists the queries in their Excel query groups, nested as Excel
  shows them, or as queries and functions (switch with **Groups** and **Types** at its top; a folder of `.m` files has
  only queries and functions), with a filter box;
- pan and zoom, and drag queries (double-click to release one);
- hover a query to see its name and counts and highlight its direct neighbours;
- click a query to trace everything upstream (blue) and downstream (aqua) of it, with a details
  panel listing what it uses and what uses it, and its M code (see below); click the background or
  press Esc to clear. With **Focus** on (as it is to begin with) only those queries stay shown; Ctrl+click
  (Cmd+click on a Mac) does the other thing for that click;
- search by name (press `/` to jump to the search box, Enter to select the first match);
- resize or hide the explorer and the details panel, with the buttons at the two ends of the top bar; the details
  panel opens when you select a query and closes when you clear it, unless you lock it open with its padlock;
- copy a query's M code with the button over it;
- read a query's steps in the details panel, above its M code, each with what it uses and a link to its line;
- open a query in the details panel's Uses or Used by list to see the lines that name it, and jump to one;
- star queries as favourites, from the explorer or the details panel, to list them at the top of the explorer;
- tick queries or groups in the explorer to show only those in the graph (Clear in the note it shows puts them all
  back), and expand or collapse every group at once;
- choose with **Show…** in the explorer whether the explorer and the graph show queries, functions, and queries with
  no connections;
- find queries used by many others, such as user-defined functions, in the explorer's Widely used group, and hide
  them all at once;
- hide queries from the graph with the eye beside their star, and show them again from the Hidden group at the bottom
  of the explorer; **Focus** on the Favourites group shows only the favourites and what they use;
- save how the page is arranged (what's selected and focused, what's ticked, hidden and chosen in Show…, the panels,
  and where you've dragged queries) as a named view with **Views** in the top bar, open it again later, save over
  any view, and **Export** your views as `pqdoc-views.json` or **Import** them, to keep with the source or share;
  a file from another workbook says so before it's imported;
- reset the view.

The top bar says how many queries and dependencies there are and, for a workbook, when it was last saved; it never
says when the page was made, so running pqdoc again on an unchanged workbook gives the same page, and pqdoc doesn't
ask before overwriting it.

It follows the system light or dark theme until you choose one with the switch in the top bar,
which it then remembers. The top bar also shows which build of pqdoc made the page, as
`pqdoc <version> (<commit>)`, linking to the repository. The page blocks all network access with a
Content-Security-Policy, so nothing in it can load anything from the network.

### Source in the page

The details panel shows the selected query's M code, with line numbers and highlighting.

- By default the page doesn't contain the code. Click **Load folder** and choose the folder of `.m`
  files once per session: the page's own folder for a workbook, or the folder you ran pqdoc on.
  Browsers don't let a page read files next to it on their own. If a file has changed since the page
  was made, the top bar says so: run pqdoc again.
- With `--embed-source` the code is inside the page instead, so it shows straight away.

**Privacy.** M code can hold server and database names, file paths and other details of where the
data comes from. That's why it's left out unless you ask for it: before using `--embed-source`, think
about where the page will go, because anyone who opens it can read every query.

## Query files

With a workbook as input, pqdoc also writes each query to its own `.m` file next to the outputs, so
the queries can be kept in git and read without Excel. Each file holds the query as Excel stores it,
which is what the Advanced Editor shows, in UTF-8 with CRLF line endings. Line breaks inside text
values are kept exactly as Excel stored them.

A query name can hold characters Windows doesn't allow in a file name. Those characters, and `%`
itself, become `%` and two hex digits, so `Sales/Returns` is written as `Sales%2FReturns.m`, and a
name Windows keeps for devices, such as `CON`, as `%43ON.m`. Running pqdoc on the folder reverses
those escapes, so the names come back exactly.

The folder is kept in step with the workbook, as vbadoc keeps its module files:

- unchanged files aren't rewritten, so they keep their timestamps;
- **a top-level `.m` file whose query is no longer in the workbook is deleted**, so don't keep
  `.m` files of your own in that folder;
- other files, and anything in subfolders, are left alone;
- a `.gitattributes` that stops git converting the files is created, unless the folder already has one.

`--on-exists` doesn't apply to these files: they always follow the workbook.

## How references are detected

Each file is split into tokens by a small M-aware tokeniser that skips `//` and `/* */` comments
and text literals (including `""` escapes), and recognises quoted identifiers (`#"Sales Data"`)
and dotted names (`Table.SelectRows` is one name). A light structural pass then ignores:

- `let` step names, record field names and function parameters (including `each`'s `_`), and any
  use of them within their scope;
- field access and projection such as `[Amount]`, `Source{0}[Data]` and `[[A], [B]]`.

Every remaining identifier that matches a query name (case-insensitively) is a reference. Lower-case
M type names in type positions (`as table`, `type [A = text]`) only match a query spelled exactly
the same way. Self-references are ignored.

This is a heuristic, not a full M parser: unusual code can still produce a missed or extra
dependency. `--verbose` logs the references found in each query.

## PlantUML server

Rendering `pdf`, `png` or `svg` needs a PlantUML server. There is deliberately **no default
server**: the diagram includes your query names, and they should never be sent to a third-party
service unless you choose to. The URL is taken from the first of these that is set:

1. the `--plantuml-server` option;
2. the `PLANTUML_SERVER_URL` environment variable;
3. a `.env` file in the current working directory containing `PLANTUML_SERVER_URL=<url>`
   (see `.env.example`).

The URL must be an absolute `http://` or `https://` address, for example
`http://localhost:8080` or `https://plantuml.example.com/plantuml`.

**Privacy.** The full diagram text, including every query name, is sent to the server. Use a
server you control or trust. The public server at plantuml.com is not appropriate for
confidential models.

**Running your own server.** The official image is the simplest option:

```
docker run -d --name plantuml -p 8080:8080 plantuml/plantuml-server:jetty
```

Then use `http://localhost:8080`. The lightweight server built into `plantuml.jar`
(`java -jar plantuml.jar -picoweb`) renders `svg` and `png` for small and medium graphs, but it
does not support `pdf` or POST requests, so use the full server for those.

**How requests are made.** The diagram is compressed and encoded with PlantUML's text encoding and
requested with `GET {server}/{format}/{encoded}`. If that URL would be longer than 2,000
characters (large graphs), the plain diagram text is sent with `POST {server}/{format}` instead.
Each request times out after 30 seconds. If the server reports a diagram error, returns an error
status, returns nothing, or returns something that is not the requested format, that format fails
with a message and the other outputs are still produced.

## Taking queries out of a workbook

To reuse queries that aren't tied to one workbook, choose them on the page's **Export** tab. It shows what they bring
with them and what to check in the target, such as a table a query reads or a data source. **Download manifest** saves
the choice as `pqdoc-export.json`, for `pqdoc import`:

```bash
pqdoc import Sales.xlsx --manifest pqdoc-export.json                  # says what would come
pqdoc import Sales.xlsx --manifest pqdoc-export.json --bundle Shared  # writes them to a folder
pqdoc import Sales.xlsx --items "Sales Report,fnClean"                # by name, without a manifest
```

A bundle holds each query's `.m` file and a `README.md` on bringing them in by hand, each after the queries it uses. It
only goes into an empty folder or an earlier bundle, so it can't delete your own files. The manifest lists what you
picked and not what that brings, so running it again later picks up any new dependencies.

## Existing output files

Every output path is checked before anything is written, so a refused overwrite never leaves a
partial set of outputs.

- `prompt` (default when run interactively) asks per file: **y**es, **n**o (skip this file),
  **a**ll (overwrite the rest without asking), **q**uit (write nothing, exit code 4).
- In a non-interactive session (redirected input or output, scheduled tasks, CI), `prompt` acts
  as `fail` and the message explains how to proceed.
- `overwrite` replaces files, `skip` keeps them (with a warning), `fail` stops before writing
  anything.

Files are written to a temporary file in the output folder and then moved into place, so a
failure or Ctrl+C never leaves a truncated file.

## Logging

Messages go to standard error with the level and message; `--verbose` adds timestamps and debug
detail (including exception details), `--quiet` shows warnings and errors only. Colour is used when
standard error is a terminal and the `NO_COLOR` environment variable is not set. A summary of
queries, dependencies, roots, leaves, isolated queries, circular dependencies and the files
written, skipped or failed is logged at the end.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | All requested outputs produced (files skipped by `--on-exists skip` are not failures) |
| 1 | Unexpected error |
| 2 | Invalid arguments or configuration |
| 3 | Input problem: the input is missing or unreadable; a file isn't a workbook, is encrypted, has no queries or is damaged; a folder has no `.m` files; or a `.m` file could not be read (outputs are still produced in the last case) |
| 4 | Output refused: an output file exists and overwriting was refused |
| 5 | One or more exports failed (others may have succeeded) |
| 130 | Cancelled with Ctrl+C |

## Project structure

```
src/PowerQueryDocumenter/          The console app
  Program.cs              Composition root: shared HttpClient, Ctrl+C handling, log flushing
  Cli/                    System.CommandLine definition, option binding, top-level runner, console prompts
  Logging/                Serilog console configuration
src/PowerQueryDocumenter.Core/     All the logic
  Configuration/          Options record, formats, .env reader, PlantUML server resolution
  Extraction/             Reading queries straight from a workbook (MS-QDEFF), and splitting Section1.m
  Analysis/               M tokeniser, reference extraction, analyser, cycle detection
  Model/                  Immutable graph types (DependencyGraph, QueryNode, DependencyEdge)
  Export/                 IExporter and the CSV, PlantUML source, PlantUML render and HTML exporters
  Output/                 Query files, and the outputs a run plans
  Application/            Run orchestration, error messages
  Resources/Page/         pqdoc's part of the page: its view, search box, styles, scripts and favicon (embedded
                          resources), set into the page shell from ../shared
../shared/                Code shared with vbadoc: opening workbooks, exit codes, writing files, the page shell, D3.js
tests/PowerQueryDocumenter.Tests/
  Fixtures/Queries/       Sample .m files: a cycle, an isolated query, awkward names
  ...                     Unit tests per area, end-to-end tests, optional integration tests
tests/PowerQueryDocumenter.BrowserTests/
  ...                     Playwright tests of the generated page in headless Chromium
```

Adding a format means writing an `IExporter` and registering it in `ExporterRegistry`; the
analyser does not change.

## Dependencies

| Package | Purpose |
|---------|---------|
| `System.CommandLine` | Argument parsing, validation, help and version output |
| `Serilog` | Structured logging |
| `Serilog.Sinks.Console` | Coloured console output for Serilog |
| `OpenMcdf` | Through the shared library, which vbadoc uses to read compound files (MPL-2.0, unmodified) |
| `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | Tests only |
| `Microsoft.Playwright` | Browser tests only |

The `.env` reader is a small hand-written parser rather than a package. D3.js v7.9.0 is vendored
once for both tools in `shared/src/ExcelDocumenter.Shared/Resources/Vendor` (ISC licence, see
`d3.LICENSE.txt`) and embedded into the HTML output.

## Tests

```
dotnet test
```

The unit tests never use the network. PlantUML rendering is tested against an in-process stub HTTP
handler that checks the request (GET with the encoded diagram, or POST for large diagrams) and
simulates server errors, timeouts, bad content and cancellation. End-to-end tests run the whole
tool in-process with temporary folders and cover the acceptance criteria and every exit code.

The browser tests check the generated page in headless Chromium. On their first run they download
Playwright's Chromium (about 150 MB) into the user's cache folder.

A few **integration tests** render the sample diagram on a real PlantUML server. They are skipped
unless a server is configured, either way:

- set the `PLANTUML_SERVER_URL` environment variable, or
- create a `.env` file in the `pqdoc` folder (copy `.env.example`); it is git-ignored.

Environment variable or `.env` were chosen over .NET user secrets because the tool itself already
reads both, they work the same way locally and in a CI pipeline, and they need no extra packages.

To run only the unit tests, with no download:

```
dotnet test --project tests/PowerQueryDocumenter.Tests --filter "Category!=Integration"
```
