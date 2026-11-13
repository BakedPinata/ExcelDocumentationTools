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
and the class diagram work. What comes next is in the [generalisation plan](../GENERALISATION-PLAN.md).
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

An option vbadoc doesn't have, such as a mistyped `--embed-sources`, is an invalid argument (exit code 2), wherever it's
written. A workbook whose name starts with `-` would be taken for one, so write it as a path, such as `.\-draft.xlsm`.

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

### Taking modules out of a workbook

To reuse modules and classes that aren't tied to one workbook, such as logging or file handling, choose them on the
page's **Export** tab. It shows what they bring with them, what to check in the target, and which library references
come too. **Download manifest** saves the choice as `vbadoc-export.json`, for `vbadoc import`:

```bash
vbadoc import Sales.xlsm --manifest vbadoc-export.json                       # says what would come
vbadoc import Sales.xlsm --manifest vbadoc-export.json --new Toolkit.xlsm    # a new workbook
vbadoc import Sales.xlsm --manifest vbadoc-export.json --into Target.xlsx    # writes Target-imported.xlsm, beside it
vbadoc import Sales.xlsm --manifest vbadoc-export.json --into Tools.xlam     # an add-in: writes Tools-imported.xlam
vbadoc import Sales.xlsm --manifest vbadoc-export.json --new Toolkit.xlam    # a new add-in
vbadoc import Sales.xlsm --manifest vbadoc-export.json --bundle Shared       # a folder, to import by hand
vbadoc import Sales.xlsm --items Logger,FilePathFromDialog --new Toolkit.xlsm  # by name, without a manifest
```

The manifest lists what you picked and not what that brings, so running it again later picks up any new dependencies.

**Neither workbook is changed.** `--into` takes `.xlsx`, `.xlsm`, `.xlsb` and `.xlam` files and writes a copy beside the
target, with the modules added and the target's sensitivity label, if it has one, as it was. An `.xlsx` is written as an
`.xlsm`, as only that can hold modules. An add-in stays an add-in, even one with no VBA yet, and a locked project stays
locked, with the same password. An older `.xla` can't be written into: save it as an Excel Add-in (`.xlam`) first. `--on-exists` and `--force` apply to the file written. A new workbook has no label, so
Excel asks for one where your organisation requires it. A workbook with a label that encrypts can't be read or written.

**When the target already has a module of that name,** an identical one is left alone, and otherwise it asks about each,
saying what's at stake, before anything is written: skip, overwrite, skip all, overwrite all, or quit. Without anyone to
ask, it stops unless `--on-clash overwrite` or `--on-clash skip` says what to do. A sheet's or the workbook's module, or a
UserForm, can't be overwritten, only skipped.

**To change the target itself,** which can't be undone, add `--in-place`. It says what will change and asks first:

```bash
vbadoc import Sales.xlsm --manifest shared.json --into Target.xlsm --in-place           # asks "are you sure?"
vbadoc import Sales.xlsm --manifest shared.json --into Target.xlsm --in-place --force   # doesn't, for unattended runs
```

`--force` answers that question and no other: an unattended run also needs `--on-clash` if the target might have any of
the modules. With nobody to ask and no `--force`, it stops and changes nothing. The target is replaced in one step, once
the result is finished and read back, so a failure leaves it as it was. A workbook open in Excel can't be replaced, and it
says so. That includes an add-in Excel has loaded, so untick it in File > Options > Add-ins > Go first, or close Excel.
An `.xlsx` can't gain modules in place.

**Library references** come too, apart from those you unticked or `--no-references`, and any the target has. One that
can't be found on this machine is left out and listed, because it would stop the whole project compiling, even code that
doesn't need it. The check is of the machine running vbadoc, so run it where the workbook will be used.

**Modules are written as source, and Excel compiles them on opening.** The result isn't signed, so a machine that only
runs signed macros, or macros from trusted locations, blocks it until it's signed or moved. A target whose VBA project is
signed is refused, as the change would break the signature, unless `--drop-signature` says to remove it.

A bundle holds the modules, ready for **File > Import File** in the VBA editor, and a `README.md` saying how, for a target
that can't be written, such as an encrypted one. It only goes into an empty folder or an earlier bundle, so it can't
delete your own modules.

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

**Show…** in the explorer chooses what kinds of thing the explorer and both views show: kinds of module and member,
private members, unreferenced or referenced members, kinds of line, and items with no connections. A note in each view's
toolbar says when choices leave things out, with Show everything. In the class diagram, **Members** lists each module's
members in its box or leaves the boxes to their names.

To see less, tick folders, modules or members in the explorer: while anything is ticked, the graph and the class
diagram show only that, and a note in the explorer and each view's toolbar says so, with Clear. Ticking a module brings
its members. Ticking doesn't select, so you can still click around inside what's shown. The buttons beside the filter
box expand or collapse every folder.

**Lock view**, beside Focus, keeps what the graph and the class diagram show exactly where it is, so you can click
around and read the source without the picture changing. Locking ticks everything shown in the explorer: untick an
item to drop it, or tick another to add it. While it's locked a click only outlines the item and shows its source,
and dragging still moves a node. Unlock with the button again, and the ticks go back to what they were. A locked
graph draws every line between the items it shows, so locking a focused graph can add some.

Star a module or member to make it a favourite, with the star on its row in the explorer or beside its name in the
source panel. Favourites are listed at the top of the explorer and starred in the graph, and **★ Path** shows the
shortest chain of dependencies between the selection and the nearest favourite. **Focus** on the Favourites group shows
only the favourites and what they use, in every view.

Modules used by many others, such as a logger or a settings module, are listed in the explorer's **Widely used**
group, with **Hide all** to take them out of the views at once, and marked with a hub where they are. How many counts as
many is a share of the other modules, set in Show…. Classes with `VB_PredeclaredId = True` are labelled predeclared.

To take something out of every view, hide it with the struck-through eye beside its star. It stays in the explorer,
struck through, and is listed in the Hidden group at the bottom, where you can show it again. Each view's toolbar counts
what's hidden, and the source panel says when what you've selected uses something hidden.

**Views**, in the top bar, saves how the page is arranged as a named view with a description: the tab, what's
selected and whether it's focused, Focus on click, whether the view is locked, what's ticked, hidden and chosen in
Show…, whether the explorer and source panel are shown and how wide, the graph's level, direction and settings, where you've moved things in the graph
and the class diagram (focused or not), and what's open in the Calls tab. Open a view to come back to it, or **Save
over** any view with the page as it is now; Undo puts it back. **Export** downloads your views as `vbadoc-views.json`,
to keep with the source or share, and **Import** reads one back, saying first if it's from another workbook. A view
that mentions something the workbook no longer has still opens, and says what's missing.

The **Calls** tab follows the execution path: start from a macro, event handler or worksheet function (or a
favourite, or Ctrl+click any procedure) and open its calls one level at a time, in the order of the code. Each call
links to its line. Calls through an interface open to the implementations, RaiseEvent to its handlers, and **Called by**
turns the tree round to show what leads to a procedure. The Ifs, loops, Select Cases, Withs and error handlers around the calls show as rows holding them, and **Sequence**
draws what's open as a sequence diagram, with modules as lifelines and the blocks as frames. The button that shows on a
row (or `*`) opens everything below it, to read or draw a whole entry point at once. The tree can't see
`Application.Run` or `OnAction`.

**Dependencies**, in the source panel, lists what the selection uses and what uses it, each item once. Click a line
under an item to peek at the code around it, without leaving what you've selected; **Go to** in the preview, or
Ctrl+click on the line, goes there instead, and Esc closes the last preview you opened. Open an item
to see every line where it's referenced, and choose one to jump to that line in the source.

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
src/VbaDocumenter/          console app: only Program.cs
src/VbaDocumenter.Cli/      the command line as a library (options, running, logging, exit codes)
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
