<img src="src/VbaDocumenter.Core/Resources/Page/favicon.svg" alt="" width="80" align="right">

# vbadoc: VBA Export & Documentation Tool

A .NET console app that exports the VBA from a macro-enabled Excel workbook into text files,
so it can be kept in git, and generates a single self-contained HTML page that shows how the
project fits together:

- an interactive **dependency graph** (d3.js), at module and member level, and
- a **class diagram** (d3.js), whose boxes can be dragged into place.

It reads the workbook file directly. It does not need Excel installed, doesn't use COM, and doesn't need
the "Trust access to the VBA project object model" setting. It also works while the workbook is open in Excel.

See [Requirements.md](Requirements.md) for the full specification.

## Status

Work in progress. Exporting the source files, analysing dependencies, the page's source browser, the dependency graph
and the class diagram work. What comes next is in the [feedback plan](../FEEDBACK-PLAN.md).
See [docs/PLAN.md](docs/PLAN.md) for the stages and progress, and [docs/DECISIONS.md](docs/DECISIONS.md)
for the design decisions behind them.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2022 doesn't support .NET 10, so
  build there from the command line (see the [root README](../README.md#building-and-testing)).

## Build and test

From the `vbadoc` folder:

```bash
dotnet build
dotnet test                                        # unit tests and browser tests
dotnet test --project tests/VbaDocumenter.Tests    # unit tests only
dotnet run --project src/VbaDocumenter -- <workbook>
```

The browser tests check the generated page in headless Chromium. On their first run they download Playwright's
Chromium (about 150 MB) into the user's cache folder.

To publish vbadoc as a single self-contained executable, into the folder shared by every tool:

```bash
dotnet publish src/VbaDocumenter -c Release -p:PublishProfile=FolderProfile
```

`publish.ps1` does this for every tool; see the [root README](../README.md#publishing).

## Usage

```bash
dotnet run --project src/VbaDocumenter -- <workbook> [--output <folder>] [--output-name <name>] [--embed-source]
                                            [--on-exists <behaviour>] [--force] [--verbose | --quiet]
```

| Argument | Alias | |
|---|---|---|
| `<workbook>` | | `.xlsm`, `.xlsb`, `.xlam`, `.xls` or `.xla` |
| `--output <folder>` | `-o` | Output folder. Default: a `VBAProject` folder next to the workbook |
| `--output-name <name>` | `-n` | The page's file name, without `.html`. Default: the workbook's name and `-vba`, such as `Sales-vba`. It can't contain a folder, an extension, `#` or `%` |
| `--embed-source` | | Put the module source inside the page, so it works without Load folder |
| `--on-exists <behaviour>` | | What to do when the page already exists and would change: `prompt`, `overwrite`, `skip` or `fail`. Default: `prompt` when run in a terminal, otherwise `fail` |
| `--force` | `-y` | Overwrite the page without asking. The same as `--on-exists overwrite` |
| `--verbose` | `-v` | List every member found in each module, with its line range, show how long each step took, and show debug-level log output |
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
| 4 | The page already exists and would change, and overwriting it was refused: nothing was written |
| 5 | The source files or page could not be written |
| 130 | Cancelled with Ctrl+C |

The codes are shared with pqdoc.

### An existing page

If the page is already there and this run would change it, vbadoc asks before overwriting it, as pqdoc does:
`[y]es / [n]o / [a]ll / [q]uit`. It asks before writing anything, so quitting changes nothing. Answering no keeps the
page and still brings the module files in line with the workbook. A page that would be written unchanged isn't asked
about, so running vbadoc again on an unchanged workbook doesn't ask. The module files and `.gitattributes` are never
asked about: they always follow the workbook.

When nobody can be asked, such as in a script or a scheduled task, vbadoc stops with exit code 4 instead. Add `--force`
(`-y`) to overwrite, or choose with `--on-exists overwrite`, `skip` or `fail`.

### Changes in 0.9.0

- vbadoc asks before overwriting a page that would change (see above), and a script must now say what it wants with
  `--force` or `--on-exists`, or it stops with exit code 4. A new build of vbadoc changes the build stamp in the page,
  so the first run after updating counts as a change too.

### Changes in 0.8.0

- The page is named after the workbook: `Sales.xlsm` gives `VBAProject/Sales-vba.html`, not `index.html`, so a page
  that's emailed or printed says what it documents. Update any bookmark or script that opens `index.html`. vbadoc
  deletes the old `index.html` on its next run, and any other page it wrote under a name that's no longer used, such as
  a renamed workbook's. Pages it didn't write are left alone.
- A `.gitattributes` created from now on covers `*.html`. One created before covers only `index.html`, so change
  that line to `*.html -text diff` to keep git from converting the page's line endings.

### Changes in 0.6.0

These break anything that runs vbadoc from a script:

- `--out` is now `--output` or `-o`, and the old name isn't accepted.
- Exit codes changed: invalid arguments went from 1 to 2, workbook problems from 2 to 3, and unexpected errors from
  3 to 1.
- The report now goes to standard error, with each line starting with its level, such as `[INF]`.
- `--version` now prints `<version>+<commit>`, with `-dirty` if the build had uncommitted changes.

### The page

The explorer on the left lists every module with its members. If your modules have Rubberduck's
`'@Folder("Parent.Child")` comments, it shows them in those folders, or by kind with **Types** at its top; otherwise by
kind. The annotation can also follow a label in a header comment, such as `' Folder:      @Folder("Reports")`, though
Rubberduck itself reads it only straight after the `'`. Its filter box narrows the list, and choosing an item selects it in the graph, the class diagram and the source
panel. Hide it with the button at the left end of the top bar, or drag its edge to resize it.

**Focus**, at the start of each view's toolbar, is on to begin with: selecting something then shows only it and what
it's connected to, in every view, until you clear the selection with Esc or a click on the background. Ctrl+click
(Cmd+click on a Mac) does the other thing for that click: with Focus on it just selects, and with Focus off it focuses.
The class diagram's legend picks out kinds of box and relationships, as the graph's does. The top bar sums up the
project and says when the workbook was last saved.

Star a module or member to make it a favourite, with the star on its row in the explorer or beside its name in the
source panel. Favourites are listed at the top of the explorer and starred in the graph, and **★ Path** shows the
shortest chain of dependencies between the selection and the nearest favourite.

The source panel opens when you select something and closes when you clear the selection. Lock it open with the
padlock in its corner, or open and close it with `</>` at the right end of the top bar. The copy button over the code
copies the lines shown.

### What it writes

- One file per module: `.bas` (standard module), `.cls` (class, ThisWorkbook and sheets), `.frm` (UserForm).
  Files use the project's code page and CRLF line endings, as the VBA editor's own Export does.
  Classes get the VBA editor's `VERSION 1.0 CLASS` header so they import back as classes.
- A `.gitattributes` that stops git converting those files, unless the folder already has one.
- `<workbook>-vba.html`, such as `Sales-vba.html`: a single self-contained page that documents the project. Open it
  from disk. `#` and `%` in the workbook's name become `_` in the file's name, because they confuse a browser's
  address.
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
  Resources/Page/           vbadoc's part of the page: its views, CSS and JavaScript (plain files, no build step),
                            set into the page shell from ../shared
../shared/                  code shared with pqdoc: opening workbooks, writing files, the page shell, d3
tests/VbaDocumenter.Tests/  xUnit tests
  Fixtures/SampleVba/       synthetic 12-module VBA project
  Fixtures/SampleVbaExtras/ declarations SampleVba doesn't cover
  Fixtures/Workbooks/       real Excel workbooks from Apache POI's test data
tests/VbaDocumenter.BrowserTests/  Playwright tests of the generated page in headless Chromium
```

## Third-party components

| Component | Licence | Use |
|---|---|---|
| [OpenMcdf](https://github.com/openmcdf/openmcdf) 3.4.0 | MPL-2.0 (unmodified NuGet package) | Reads compound files |
| [System.CommandLine](https://github.com/dotnet/command-line-api) 2.0.14 | MIT | Command line |
| [Serilog](https://serilog.net) 4.4.0 and Serilog.Sinks.Console 6.1.1 | Apache-2.0 | Logging |
| [d3](https://d3js.org) 7.9.0 | ISC | Dependency graph and class diagram (vendored) |
| [Apache POI](https://poi.apache.org) test data | Apache-2.0 | Test fixtures only |
| [Playwright for .NET](https://playwright.dev/dotnet/) 1.64.1 | MIT | Browser tests only |

## Licence

[MIT](../LICENSE). The third-party components above keep their own licences.
