<img src="src/VbaDocumenter.Core/Resources/Page/favicon.svg" alt="" width="80" align="right">

# vbadoc: VBA Export & Documentation Tool

A .NET console app that exports the VBA from a macro-enabled Excel workbook into text files,
so it can be kept in git, and (in progress) generates a single self-contained HTML page that
shows how the project fits together:

- an interactive **dependency graph** (d3.js), at module and member level, and
- a **class diagram** (Mermaid).

It reads the workbook file directly. It does not need Excel installed, doesn't use COM, and doesn't need
the "Trust access to the VBA project object model" setting. It also works while the workbook is open in Excel.

See [Requirements.md](Requirements.md) for the full specification.

## Status

Work in progress. Exporting the source files, analysing dependencies, the page's source browser and the dependency
graph work. The class diagram is still to come.
See [docs/PLAN.md](docs/PLAN.md) for the stages and progress, and [docs/DECISIONS.md](docs/DECISIONS.md)
for the design decisions behind them.

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Build and test

```bash
dotnet build
dotnet test                              # unit tests and browser tests
dotnet test tests/VbaDocumenter.Tests    # unit tests only
```

The browser tests check the generated page in headless Chromium. On their first run they download Playwright's
Chromium (about 150 MB) into the user's cache folder.

## Usage

```bash
dotnet run --project src/VbaDocumenter -- <workbook> [--output <folder>] [--embed-source] [--verbose]
```

| Argument | Alias | |
|---|---|---|
| `<workbook>` | | `.xlsm`, `.xlsb`, `.xlam`, `.xls` or `.xla` |
| `--output <folder>` | `-o` | Output folder. Default: a `VBAProject` folder next to the workbook |
| `--embed-source` | | Put the module source inside the page, so it works without Load folder |
| `--verbose` | `-v` | List every member found in each module, with its line range, and show debug-level log output |
| `--quiet` | `-q` | Show warnings and errors only |
| `--help` | `-h`, `-?` | Show help |
| `--version` | | Show the version and git commit of this build of vbadoc, and nothing else |

Progress and results are logged to standard error, in colour when it's a terminal and `NO_COLOR` isn't set. Standard
output is only used by `--help` and `--version`.

| Exit code | Meaning |
|---|---|
| 0 | Success |
| 1 | Unexpected error (`--verbose` shows the details) |
| 2 | Invalid arguments |
| 3 | A problem with the workbook, which the message explains: missing, unreadable, corrupt or without a VBA project |
| 5 | The source files or page could not be written |
| 130 | Cancelled with Ctrl+C |

The codes are shared with pqdoc. Code 4 means an output was refused, which never happens here: vbadoc always brings
the output folder in line with the workbook.

### Changes in 0.6.0

These break anything that runs vbadoc from a script:

- `--out` is now `--output` or `-o`, and the old name isn't accepted.
- Exit codes changed: invalid arguments went from 1 to 2, workbook problems from 2 to 3, and unexpected errors from
  3 to 1.
- The report now goes to standard error, with each line starting with its level, such as `[INF]`.
- `--version` now prints `<version>+<commit>`, with `-dirty` if the build had uncommitted changes.

### What it writes

- One file per module: `.bas` (standard module), `.cls` (class, ThisWorkbook and sheets), `.frm` (UserForm).
  Files use the project's code page and CRLF line endings, as the VBA editor's own Export does.
  Classes get the VBA editor's `VERSION 1.0 CLASS` header so they import back as classes.
- A `.gitattributes` that stops git converting those files, unless the folder already has one.
- `index.html`: a single self-contained page that documents the project. Open it from disk.
  - By default it doesn't contain the source. Click **Load folder** and choose the output folder once per session;
    browsers don't let a page read files next to it on their own.
  - With `--embed-source` the source is inside the page instead. The page is larger, and code changes appear in its
    git diff as well.
  - The top bar shows which build of vbadoc made the page, such as `vbadoc 0.6.0 (b7c0ebe)`. That's the version and
    the commit it was built from, with `-dirty` added if the build had uncommitted changes. If that line changes in a
    diff, the tool changed too.

**Keeping tool changes apart from workbook changes.** After updating vbadoc, run it once on the workbook as it is
already committed, and commit that on its own (for example "Regenerate with vbadoc 0.6.0"). Everything that changes
in that commit comes from the tool, so any later diff comes from the workbook alone.

On each run the folder is brought in line with the workbook. Module files (`.bas`, `.cls`, `.frm`) at the top level
of the folder that are no longer in the workbook are deleted, so **don't keep hand-written module files there**.
Other files, such as a README or `.git/`, are never touched. Two workbooks in the same directory would share the
default `VBAProject` folder, so give one of them `--output`.

## Project layout

```
src/VbaDocumenter/          console app (command line, output, exit codes)
src/VbaDocumenter.Core/     all the logic
  Extraction/               reading the VBA project out of the workbook (MS-OVBA)
  Parsing/                  lightweight VBA parser: declarations, line ranges, procedure bodies
  Analysis/                 reference resolution and the module/member dependency graphs
  Output/                   writing the source files and the page
  Resources/Page/           the page's HTML template, CSS and JavaScript (plain files, no build step)
  Resources/Vendor/         d3 and Mermaid, embedded and inlined into the page
tests/VbaDocumenter.Tests/  xUnit tests
  Fixtures/SampleVba/       synthetic 12-module VBA project
  Fixtures/SampleVbaExtras/ declarations SampleVba doesn't cover
  Fixtures/Workbooks/       real Excel workbooks from Apache POI's test data
tests/VbaDocumenter.BrowserTests/  Playwright tests of the generated page in headless Chromium
```

## Third-party components

| Component | Licence | Use |
|---|---|---|
| [OpenMcdf](https://github.com/openmcdf/openmcdf) 3.3.0 | MPL-2.0 (unmodified NuGet package) | Reads compound files |
| [System.CommandLine](https://github.com/dotnet/command-line-api) 2.0.0 | MIT | Command line |
| [Serilog](https://serilog.net) 4.3.0 and Serilog.Sinks.Console 6.0.0 | Apache-2.0 | Logging |
| [d3](https://d3js.org) 7.9.0 | ISC | Dependency graph (vendored) |
| [Mermaid](https://mermaid.js.org) 11.17.2 | MIT | Class diagram (vendored) |
| [Apache POI](https://poi.apache.org) test data | Apache-2.0 | Test fixtures only |
| [Playwright for .NET](https://playwright.dev/dotnet/) 1.63.0 | MIT | Browser tests only |

## Licence

[MIT](../LICENSE). The third-party components above keep their own licences.
