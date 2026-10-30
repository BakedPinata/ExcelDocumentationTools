# pqdoc: Requirements

## 1. Purpose and Audience

This document specifies pqdoc version 1.0.0, a .NET console application that works out which Power Query queries reference which and exports the result. It is written for the developers and LLMs who work on the tool. Requirements use the keywords **MUST**, **SHOULD** and **MAY** as defined in RFC 2119. Each requirement has an ID so it can be referenced in review and testing.

## 2. Input and Terminology

The tool reads every `*.m` file in the top level of the input folder (non-recursive). Each file is one Power Query query; the query name is the file name without extension.

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

- Full Power Query M parsing or evaluation. A lightweight tokenizer is sufficient (see FR-ANA-1).
- A GUI.
- Running or validating the queries themselves.

## 4. Functional Requirements

### 4.1 Command-Line Interface

**Synopsis**

```
pqdoc <input_folder> [options]
```

The input folder is a required positional argument.

| ID | Requirement |
|----|-------------|
| FR-CLI-1 | The CLI MUST be implemented with `System.CommandLine` (or an equally capable parser) so that `--help`, `--version`, validation messages and usage text are generated automatically. |
| FR-CLI-2 | Every option MUST have a long name (`--output-name`) and SHOULD have a short alias where one is natural (`-n`). |
| FR-CLI-3 | Invalid or conflicting options MUST produce a clear message naming the problem and the offending option, print brief usage, and exit with code 2 (see section 5.3 for exit codes). |

**Options**

| Option | Alias | Value | Default | Description |
|--------|-------|-------|---------|-------------|
| `--output-name` | `-n` | string (no extension) | `powerquery-dependencies` | Base file name for all outputs. The tool appends the correct extension per format. MUST NOT contain path separators or an extension; if it does, fail validation. |
| `--output-folder` | `-o` | path | Same as the input folder | Folder to write outputs to. Created if it does not exist. |
| `--format` | `-f` | one or more of `puml`, `html`, `pdf`, `png`, `svg` | none (CSV only) | Additional formats to generate. Accepts a comma-separated list (`-f puml,html`) and MAY be repeated (`-f puml -f html`). Values are case-insensitive. |
| `--no-csv` | | flag | off | Skip the CSV export. MUST fail validation if no `--format` value is supplied, with a message explaining that nothing would be produced. |
| `--plantuml-server` | | URL | none | Base URL of the PlantUML server used for `pdf`, `png` and `svg` rendering. See FR-PUML-4. |
| `--on-exists` | | `prompt`, `overwrite`, `skip`, `fail` | `prompt` when interactive, otherwise `fail` | Behaviour when an output file already exists. See FR-OVR. |
| `--force` | `-y` | flag | off | Shorthand for `--on-exists overwrite`. If both are given and disagree, fail validation. |
| `--verbose` | `-v` | flag | off | Show debug-level log output. |
| `--quiet` | `-q` | flag | off | Show warnings and errors only. Mutually exclusive with `--verbose`. |

**Examples**

```
# CSV only, default name, next to the inputs
pqdoc C:\queries

# CSV plus PlantUML source and interactive HTML, custom name and location
pqdoc C:\queries -n sales-model -o C:\docs -f puml,html

# SVG and PNG only, no CSV, overwriting silently
pqdoc C:\queries --no-csv -f svg,png --plantuml-server https://plantuml.example.com --force
```

### 4.2 Dependency Analysis

| ID | Requirement |
|----|-------------|
| FR-ANA-1 | Reference detection SHOULD use a small M-aware tokenizer rather than regular expressions over the whole file. It MUST ignore content inside line comments (`//`), block comments (`/* */`) and string literals (`"..."`, with `""` as an escaped quote), and MUST recognise quoted identifiers (`#"Name with spaces"`, including `""` escapes) and bare identifiers. |
| FR-ANA-2 | The built-in keyword/function filter MUST apply only to bare identifiers that are **not** the name of an existing query. A query that happens to be named `Source`, `Table` or `Value` MUST still be detected when referenced. |
| FR-ANA-3 | Query-name comparison MUST be consistently case-insensitive across the known-query set, reference matching and de-duplication. |
| FR-ANA-4 | Self-references MUST be ignored for edge generation. |
| FR-ANA-5 | The analyser MUST detect circular dependencies and report each cycle as a warning listing the queries involved. Cycles MUST NOT cause crashes or infinite loops, and exports MUST still be produced. |
| FR-ANA-6 | The analyser MUST compute, per query, whether it is a **root** (no upstream queries), a **leaf** (no dependents), or **isolated** (neither), for use by the exporters and a console summary. |
| FR-ANA-7 | The analysis MUST be separated from exporting: it produces an immutable in-memory dependency graph (nodes plus directed edges) that all exporters consume. Exporters MUST NOT re-read files. |
| FR-ANA-8 | Files MUST be read as UTF-8 (with BOM detection). A file that cannot be read MUST be logged as an error and skipped, and the run continues (see FR-ERR). |
| FR-ANA-9 | A `--recursive` option MAY be added later; it is out of scope for version 1.0.0. Scanning is top-level only. |

### 4.3 Export: CSV

| ID | Requirement |
|----|-------------|
| FR-CSV-1 | The header MUST be `Query Name,Referenced By Query Name`. The first column is the upstream query being referenced; the second is the downstream query that references it. Values are quoted, with `""` escaping; there is one row per pair; queries with no dependents are emitted with an empty second column; rows are ordered case-insensitively. The header strings MUST be defined as constants in one place. |
| FR-CSV-2 | File name is `<output-name>.csv`. |
| FR-CSV-3 | CSV export is on by default and disabled only by `--no-csv`. |

### 4.4 Export: PlantUML Source (`puml`)

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

### 4.5 Export: Interactive HTML (`html`)

| ID | Requirement |
|----|-------------|
| FR-HTML-1 | File name is `<output-name>.html`. The file MUST be a single self-contained document (inline CSS, JS and data) that works offline when opened from disk. The D3.js library (v7, minified) MUST be embedded inline rather than loaded from a CDN. Embed it from a vendored copy stored in the repository (for example as an embedded resource) and note its version and licence in a comment. |
| FR-HTML-2 | The graph MUST be a D3 force-directed network with directed edges (arrowheads), edges running upstream to downstream, consistent with the CSV and PlantUML exports. |
| FR-HTML-3 | Interaction: pan and zoom; drag nodes; hovering a node shows its name and counts of upstream and downstream queries and highlights its direct neighbours; clicking a node pins/highlights its full upstream and downstream chains (transitive); clicking the background clears the selection. |
| FR-HTML-4 | A search box MUST filter/highlight nodes by name (case-insensitive substring). A "reset view" control SHOULD be provided. |
| FR-HTML-5 | Nodes SHOULD be coloured by role (root, leaf, isolated, in-cycle) with a legend. Labels MUST be legible; long names SHOULD be truncated with the full name in a tooltip. |
| FR-HTML-6 | The page MUST adapt to light/dark colour scheme via `prefers-color-scheme` and be usable on a laptop-sized viewport. |
| FR-HTML-7 | Graph data MUST be embedded as JSON with proper escaping (including `</script>` sequences in names) so that query names cannot break out of the page or inject script. |
| FR-HTML-8 | The page title and a visible heading SHOULD include the output name and the generation date/time, plus node and edge counts. |
| FR-HTML-9 | The HTML template SHOULD be kept as a separate embedded resource file rather than an inline C# string. |

### 4.6 Overwrite Behaviour

| ID | Requirement |
|----|-------------|
| FR-OVR-1 | Existence is checked **per output file**, before any file is written, so that a refused overwrite does not leave a partial set of outputs. |
| FR-OVR-2 | `prompt`: if stdin/stdout are interactive, ask per file with options *yes / no / all / quit*. If the session is not interactive (redirected input, CI), `prompt` MUST behave as `fail` and the error message MUST say how to proceed (`--force` or `--on-exists`). |
| FR-OVR-3 | `overwrite`: replace silently (logged at information level). `skip`: leave the existing file and log a warning. `fail`: abort before writing anything and exit with code 4. |
| FR-OVR-4 | Writes SHOULD be atomic where practical (write to a temporary file in the target folder, then move over the destination) so that a failure never leaves a truncated file. |

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
| 3 | Input problem (folder missing, no `.m` files) |
| 4 | Output refused (existing file with `fail` behaviour) |
| 5 | One or more exports failed (others may have succeeded) |
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
| NFR-TEST-1 | The xUnit test project MUST cover: the tokenizer (comments, strings, escaped quotes, quoted identifiers, queries named like built-ins), graph building, cycle detection, role classification, CSV output (against a fixture, including the header), PlantUML alias/escaping, PlantUML encoding, HTML JSON escaping, option validation, overwrite behaviour, and exit-code mapping. |
| NFR-TEST-2 | Tests MUST use temporary folders and a fake/stub HTTP handler for the PlantUML server. No test may need real network access. |
| NFR-TEST-3 | Test fixtures MUST include a sample set of `.m` files with a cycle, an isolated query, and awkward names. |

### 5.6 Documentation

| ID | Requirement |
|----|-------------|
| NFR-DOC-1 | `README.md` MUST cover: purpose, installation/build, full option reference, examples, exit codes, PlantUML server configuration (flag, environment variable, `.env`), and a note on the privacy implications of using a remote PlantUML server. |

## 6. Acceptance Criteria (Summary)

1. `pqdoc <folder>` with no other options writes `powerquery-dependencies.csv` next to the inputs, with the header row `Query Name,Referenced By Query Name`.
2. `-f puml,html` produces `.csv`, `.puml` and `.html` side by side. The HTML opens offline and renders an interactive graph. The PUML renders without syntax errors in a PlantUML server.
3. `--no-csv` without `-f` fails with exit code 2 and writes nothing.
4. Existing output files with default settings in a non-interactive session cause exit code 4 and no files are written or modified.
5. Rendering formats without a configured server fail with exit code 2 and a message that names all three configuration routes.
6. A query named `Source` that is referenced by another query appears as a dependency.
7. A query name appearing only inside a comment or string literal creates no dependency.
8. A circular dependency produces a warning and all exports still complete.
9. A render failure for PNG does not prevent SVG, HTML or CSV from being written, and the exit code is 5.

## 7. Decisions Made (Defaults Chosen)

These are deliberate defaults.

- **Default output name** is `powerquery-dependencies`.
- **Overwrite default** is *prompt when interactive, fail when not*, with `--force` / `--on-exists` to override. This is the safest default and still scriptable.
- **No default PlantUML server**, to avoid sending query names to a public service unintentionally.
- **D3 is embedded** in the HTML so the file works offline and can be shared by email or from a file share without external requests.
- **Edge direction** is upstream to downstream across all formats.
- **CSV is always on** unless `--no-csv` is given, and `--format` is additive.
- **CSV header** is `Query Name,Referenced By Query Name`, so each row says plainly which query uses which.

## 8. Out of Scope / Possible Future Work

- Recursive folder scanning (`--recursive`).
- Local PlantUML rendering via a Java/`plantuml.jar` or Docker invocation.
- Additional formats (Mermaid, Graphviz DOT, GraphML, JSON).
- Parsing queries directly from `.pbix` / `.xlsx` files.
- Filtering the graph by query name or depth (for example, "show everything downstream of `X`").
- Configuration file support (`appsettings.json`) for persisting defaults.
