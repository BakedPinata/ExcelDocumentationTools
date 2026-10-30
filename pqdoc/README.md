# pqdoc: Power Query Documentation Tool

A .NET console tool that reads a folder of exported Power Query (`.m`) queries, works out which
queries reference which, and exports the dependency graph as:

- **CSV** (default): one row per dependency.
- **PlantUML source** (`.puml`).
- **Rendered diagrams** (`.svg`, `.png`, `.pdf`) via a PlantUML server you configure.
- **Interactive HTML**: a single, self-contained page with a D3 force-directed graph that works
  offline.

It also reports circular dependencies and classifies each query as a root, leaf, isolated or
intermediate query.

## Requirements

- .NET SDK 9.0. The target framework is set once in `Directory.Build.props` (`net9.0`).
- For `svg`, `png` and `pdf` only: access to a PlantUML server (see below).

## Build and run

```
dotnet build
dotnet run --project src/PowerQueryDocumenter -- C:\queries
```

To produce a standalone executable:

```
dotnet publish src/PowerQueryDocumenter -c Release -o publish
publish\pqdoc C:\queries
```

## Usage

```
pqdoc <input_folder> [options]
```

`<input_folder>` is the folder containing the `.m` files, one query per file. The query name is
the file name without `.m`. Only the top level of the folder is scanned.

| Option | Alias | Value | Default | Description |
|--------|-------|-------|---------|-------------|
| `--output-name` | `-n` | name | `powerquery-dependencies` | Base file name for every output. The extension is added per format. Must not contain a folder or an extension. |
| `--output` | `-o` | path | the input folder | Where outputs are written. Created if missing. |
| `--format` | `-f` | `puml`, `html`, `pdf`, `png`, `svg` | none | Extra formats, comma-separated (`-f puml,html`) or repeated (`-f puml -f html`). Case-insensitive. |
| `--no-csv` | | flag | off | Skip the CSV. Needs at least one `--format`. |
| `--plantuml-server` | | URL | none | PlantUML server for `pdf`, `png` and `svg`. |
| `--on-exists` | | `prompt`, `overwrite`, `skip`, `fail` | `prompt` when interactive, otherwise `fail` | What to do when an output file already exists. |
| `--force` | `-y` | flag | off | Same as `--on-exists overwrite`. |
| `--verbose` | `-v` | flag | off | Show debug output, including exception details. |
| `--quiet` | `-q` | flag | off | Show warnings and errors only. |
| `--help` | `-h`, `-?` | | | Show help. |
| `--version` | | | | Show the version and git commit of this build. |

### Examples

```
# CSV only, default name, next to the inputs
pqdoc C:\queries

# CSV plus PlantUML source and interactive HTML, custom name and location
pqdoc C:\queries -n sales-model -o C:\docs -f puml,html

# SVG and PNG only, no CSV, overwriting silently
pqdoc C:\queries --no-csv -f svg,png --plantuml-server https://plantuml.example.com --force
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

- pan and zoom, and drag queries (double-click to release one);
- hover a query to see its name and counts and highlight its direct neighbours;
- click a query to trace everything upstream (blue) and downstream (aqua) of it, with a details
  panel listing what it uses and what uses it; click the background or press Esc to clear;
- search by name (press `/` to jump to the search box, Enter to select the first match);
- resize or hide the details panel;
- reset the view.

It follows the system light or dark theme until you choose one with the switch in the top bar,
which it then remembers. The top bar also shows which build of pqdoc made the page, as
`pqdoc <version> (<commit>)`, linking to the repository. The page blocks all network access with a
Content-Security-Policy, so nothing in it can load anything from the network.

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
| 3 | Input problem: folder missing or unreadable, no `.m` files, or a `.m` file could not be read (outputs are still produced in the last case) |
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
  Analysis/               M tokeniser, reference extraction, analyser, cycle detection
  Model/                  Immutable graph types (DependencyGraph, QueryNode, DependencyEdge)
  Export/                 IExporter and the CSV, PlantUML source, PlantUML render and HTML exporters
  Output/                 Overwrite policy, atomic file writes
  Application/            Run orchestration, exit codes, error messages
  Resources/Page/         The HTML page: template, styles, scripts and favicon (embedded resources)
  Resources/Vendor/       Vendored D3.js (embedded resource)
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
| `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | Tests only |
| `Microsoft.Playwright` | Browser tests only |

The `.env` reader is a small hand-written parser rather than a package. D3.js v7.9.0 is vendored
in `src/PowerQueryDocumenter.Core/Resources/Vendor` (ISC licence, see `d3.LICENSE.txt`) and embedded
into the HTML output.

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
dotnet test tests/PowerQueryDocumenter.Tests --filter "Category!=Integration"
```
