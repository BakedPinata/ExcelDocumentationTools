# Design decisions

Decisions made while building the tool that aren't spelled out in [Requirements.md](../Requirements.md),
or that change it. Add to this file whenever a new decision is agreed. Conventions shared with the other tools are in
[GUIDELINES.md](../../docs/GUIDELINES.md).

## Platform and libraries

- **One repository.** vbadoc now lives in `vbadoc/` in BakedPinata/ExcelDocumentationTools, which is this repository's
  old BakedPinata/vbadocumenter renamed. Its history moved with it. Stage 6 continues now that vbadoc is aligned
  with pqdoc.
- **.NET 10**, moved from .NET 9 on 2026-11-03, a week before .NET 9's support ended, once the work machine had the
  .NET 10 SDK. Output was byte-identical apart from the build stamp. The framework is set in the root
  `Directory.Build.props`, which moves every tool.
- **OpenMcdf 3.4.0**, not 2.4.1 as the spike used. 2.4.1 has two unpatched denial-of-service advisories
  (GHSA-jxpf-xq2m-q525, GHSA-5qwm-7pvp-w988), and only 3.1.4 and later are fixed. Requirements.md is updated to match.
- **d3 7.9.0**, the latest. It is the only vendored script since 0.7.0, when the class diagram moved from Mermaid to d3
  (step F1 of the feedback plan; see "Built in stage 5").
- Vendored scripts are stored **byte-for-byte unchanged** with their licence files (see
  `shared/src/ExcelDocumenter.Shared/Resources/Vendor/README.md` since step C1). The page writer adds each licence as a
  comment above its script, and must refuse a script containing `<script` or `</script`, which would break inlining.
- **Central Package Management.** Every NuGet version is in the root `Directory.Packages.props`, and `PackageReference`
  items in the projects have no `Version`. Add or upgrade a package there, and keep a comment beside any version chosen for a reason.
- **Shared project settings** (`TargetFramework`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors`,
  `Deterministic`) are in the root `Directory.Build.props`. `vbadoc/Directory.Build.props` imports it and sets `Version`,
  `Product` and `ToolRoot`. Each `.csproj` holds only what is specific to it.
- **Version and build stamp.** `Version` (0.6.0 after stage 5e) is raised at the end of each stage. The root
  `Directory.Build.targets` stamps the informational version with the last commit that touched `vbadoc/`, `shared/` or
  the root build files, not `HEAD`, so a commit to another tool doesn't change vbadoc's pages. It adds `-dirty` when
  those paths have uncommitted changes. The shared `ToolVersion` reads it back for the page, and System.CommandLine for
  `--version`.
- **`.editorconfig`** at the root records the code style as IDE suggestions, not build errors, and sets no line endings or
  charset, which are left to `.gitattributes`.
- **C# uses 2-space indentation**, changed from 4 in one whitespace-only commit listed in `.git-blame-ignore-revs`.
  Run `git config blame.ignoreRevsFile .git-blame-ignore-revs` once to use it locally; GitHub uses it automatically.
  Git can't match blank lines, lone braces and bare keywords across that commit, so a few still show it. `git blame -w`
  shows none.
- **Australian spelling** in our own identifiers, comments, docs and UI text. The `realization` edge kind stays as it
  is, because it is data in generated pages.
- **Tests use xUnit v3**, like pqdoc's, so test projects are executables. `coverlet.collector` was dropped when they
  moved: nothing collected coverage or read it.
- **Browser tests** use Playwright with its own downloaded Chromium.
- The page's JavaScript and CSS are **hand-written plain files with no build step** (no bundler, no TypeScript).

## Command line and logging (stage 5e)

- **System.CommandLine and Serilog** replaced the hand-written parser and console writes, set up the same way as
  pqdoc's. The code is parallel, not shared. It stays in the console app, because Core only reports through what it
  returns.
- **Breaking changes in 0.6.0:** `--out` became `--output`/`-o`, with no alias. Exit codes moved to the scheme shared
  with pqdoc: bad arguments 1 → 2, workbook problems 2 → 3, unexpected errors 3 → 1. The report moved from standard
  output to the logger, which writes to standard error. `--version` prints System.CommandLine's
  `<version>+<commit>[-dirty]`; pages keep the longer sentence from `ToolVersion.Description`.
- **`ExtractionException` is exit code 3.** It also covers two modules mapping to one file name, which is a problem
  with the workbook even though it's found while exporting.
- **Any I/O failure while writing is exit code 5.** Until 0.9.0, code 4 (output refused) wasn't used, because vbadoc
  always brought the folder in line with the workbook.
- **Asking before overwriting the page** (0.9.0, step A5 of the feedback plan, at the user's request). `--on-exists` and
  `--force` (`-y`) work as pqdoc's, with the same code, prompt and messages from the shared library: by default vbadoc
  asks when the session is interactive and refuses with code 4 when it isn't. Only the page is asked about, because
  the module files and `.gitattributes` follow the workbook, as pqdoc's `.m` files do; and only a page this run would
  change, because an unchanged page has nothing to overwrite and re-running on an unchanged workbook shouldn't ask. A
  new build changes the page's stamp, so the first run after updating asks too. The check comes after the page is
  built and before anything is written, so quitting changes nothing. A page that's skipped still lets older pages under
  other names be cleared, since the folder keeps a current page either way.
- **Stack traces appear only with `--verbose`**, as in pqdoc. An unexpected error used to print one every time.
- **Report detail is at Debug level**, so `--verbose` shows it: member lists, module edges and the rest of a list past 20
  items. Parser warnings are at Warning level, so they still show with `--quiet`.
- **Ctrl+C is honoured between steps**, because Core's work is synchronous and quick. Writing, once started, always
  finishes: half an export would be worse than waiting a moment. So the page is now built before anything is written.

## Export

- The default output folder is **`VBAProject` next to the workbook**; `--output <folder>` overrides it.
  Two workbooks in one directory would share that folder, so one needs `--output`.
- **There is no manifest.** Every top-level `.bas`, `.cls` and `.frm` file in the output folder is assumed to come from
  the tool. Ones no longer in the workbook are deleted; all other files (README, `.gitattributes`, `.git/`) are left alone.
- **Document modules (ThisWorkbook, sheets) use `.cls`**, as the VBA editor does, not `.doc.cls` as the probe did.
- **Class and document files get the VBA editor's header** (`VERSION 1.0 CLASS` … `END`). It isn't stored in the workbook,
  but without it the editor imports a `.cls` as a standard module.
- **Forms (`.frm`) contain only code and `Attribute` lines**, with no designer `Begin … End` block, because there is no
  `.frx` for it to refer to. Forms therefore can't be imported back into the VBA editor.
- **Source is kept as raw code-page bytes**, so export is lossless. Line endings are normalised to CRLF.
- **Only changed files are rewritten.** Stale files are deleted before writing, so a module renamed only by case gets its
  new file name even on Windows.
- **A module that can't be read stops the whole run** rather than being skipped, because skipping it would delete its file.
- **The created `.gitattributes`** marks `*.bas`, `*.cls`, `*.frm` and `*.html` as `-text diff` (no conversion,
  textual diffs) and `*.frx` as `binary`. An existing one is never overwritten, so one from before 0.8.0 still names
  `index.html`; the README says to change that line.
- **The page is named after the workbook** (0.8.0, step A4 of the feedback plan): `Sales.xlsm` gives `Sales-vba.html`,
  the same rule as pqdoc's `-powerquery` outputs. It was `index.html`, which says nothing once the page is emailed,
  attached or printed and loses its folder. The suffix keeps it apart from pqdoc's page for the same workbook.
  `--output-name` (`-n`, as in pqdoc, with the same checks plus `#` and `%`) chooses another name. `#` and `%` in a
  workbook's name become `_` in the file name only, because `#` starts the address's fragment, which the page uses for
  Back and Forward, and `%` starts an escape.
- **Older pages are deleted.** After writing the page, vbadoc deletes any other `.html` in the output folder whose
  generator tag says vbadoc wrote it: `index.html` from before 0.8.0, or a renamed workbook's page. Pages written by
  hand or by pqdoc are left alone. It reads only each file's first 2 KB, where the tag is.

## Writing into workbooks: what step H1 found

The spike was `spikes/h1-import`, deleted after H4 and still in the repository's history, with the workbooks' list. Excel accepted all of this, at home and
on a work machine.

- **A project can be written as source only.** MS-OVBA's rule for writers does it: `_VBA_PROJECT` holds only its 7-byte
  header, with version `0xFFFF`, so Excel ignores any compiled code and compiles every module from source on opening.
  The `__SRP_` streams, which are the same kind of cache, are deleted. A new module is a stream of compressed source at
  offset 0, its `dir` records, a line in `PROJECT` and an entry in `PROJECTwm`. Everything else in an existing project
  stays, protection included.
- **Compression has to be the real thing.** Literal tokens alone, as the tests' `SyntheticWorkbook` writes, break the
  format's rule that every chunk but the last decompresses to exactly 4096 bytes.
- **A new project needs no template.** The tool can write a whole workbook and project itself, so nothing made in Excel
  has to be embedded, as the plan had assumed. CMG, DPB and GC are encrypted with a key that's the low byte of the sum
  of the project ID's bytes, braces included, which `SimpleMacro.xlsm`'s confirm. Unprotected, they hold `00000000`,
  `00` and `FF`. A new project gets Excel's two references (OLE Automation and Office), written as `SimpleMacro.xlsm`
  has them, with an Office 14 path, which Excel finds by GUID.
- **A workbook without a project** gets a document module for itself and each sheet, tied by code names in
  `workbook.xml` and the sheets; a new code name mustn't clash with one a sheet already has. An `.xlsx` becomes
  macro-enabled, with its content type changed and the file named `.xlsm`.
- **An `.xlsb` needs no code names:** they're in its binary parts, but Excel added ThisWorkbook and the sheets' modules
  itself to a project that had none.
- **An exported class has to be turned back:** its `VERSION 1.0 CLASS` block goes, and the attributes an export leaves
  out come back (`VB_Base`, `VB_TemplateDerived`, `VB_Customizable`). Copying from a project needs none of that, as the
  stored source is already right.
- **Replacing a module** works by removing its records, stream and lines, then adding it again.
- **Sensitivity labels:** a label without encryption is only metadata, so a changed copy keeps it, byte for byte, and
  Excel showed the same label and saved without asking. A label with encryption can't be read or changed, as with any
  encrypted workbook. A workbook without a label asks for one at work, as any unlabelled workbook does.
- **Library references** are records in the `dir` stream, so they can be copied as they stand, whatever their kind (a
  control reference is several records, and can hold a name of its own). A reference ends with the record that says what
  it is, not at the next name. Excel accepted registered ones, and the user's own modules worked after being copied with
  what they depend on, which vbadoc's module edges found.
- **A reference to a library that isn't installed stops the whole project compiling,** with "Can't find project or
  library", even in a procedure that needs no library. So the tool has to leave such a reference out rather than copy
  it. `HKEY_CLASSES_ROOT\TypeLib\{GUID}` says whether one is installed; only the GUID counts, as a project can name an
  older version than the machine has (Office `#2.0` where the machine has 2.6 to 2.8).

## Choosing what to import (0.29.0, step H2 of the feedback plan)

The Export tab, the manifest and bundles are mostly the shell's and `shared/`'s: see `shared/README.md`.

- **`ModuleImport` holds the rules,** and the page gets their results as data (`whyNot` and `needs` on each module), so
  the tab and the command can't disagree about them. Only following what uses what is written twice.
- **A dependency on a document module or form never comes along.** A sheet's code name is listed, since the target needs a
  sheet called that. Plain `ThisWorkbook` isn't, since it just means the target's own: the user's view of generic is that
  relying on the workbook is fine. A procedure of the source's own on ThisWorkbook is listed, as it won't be there. The
  workbook's module is known by its `VB_Base`, so a renamed one still counts.
- **`Application.Run`, `CallByName` and `OnAction` are found in the statements' text,** and listed once per module with
  late-bound calls, as what can't be followed. Text in the source is enough: this only has to prompt a look.
- **Picking an interface offers its implementations,** and so does anything that brings an interface, since an interface
  is little use without something implementing it. The module edges give them; nothing uses an implementation, so
  following what uses what never would. This is the shape of the user's path classes.
- **Library references are read from the `dir` stream** (`VbaProject.References`), and the page lists those that aren't
  the two every project has. Each is known by its name in the manifest, as that's what the VBA editor shows.
- **`--no-references` is vbadoc's alone,** on `import`, since pqdoc has none.

## Writing into workbooks (0.30.0, step H3 of the feedback plan)

The commands and what they promise are in Requirements.md. What both tools need is in `shared/`: see its README.

- **Checked against what Excel accepted.** Excel can't be run here, so `--new` and `--into` were compared with the spike's
  R1 and R2, which Excel accepted, on the same input. `--into` matched in every part, `dir` record and stream. `--new`
  differed only in a wide first column the spike gave its sheet for its own test text.
- **The spike's writing, with what it got wrong put right:**
  - A module called `Name` or `ID` took the project's own line with it when replaced, as a `[Workspace]` line looks like
    one. Lines are now removed by section.
  - A module called `Dir` collided with the `dir` stream, as names in a compound file ignore case. A stream's name is
    separate from its module's, so such a module gets a free one.
  - A project with no `PROJECTwm` gained a partial one. It's now left without.
  - A last chunk that wouldn't compress was an error. It's stored raw and padded, as MS-OVBA says, which the read-back
    then catches for source. Text always compresses, so this is for the format's sake.
- **A control reference is known by the library it came from.** Its libid names a cache made on each machine
  (`MSForms.exd`), with a GUID of its own, so `suffix-generator.xlsm`'s `{93114339-…}` means nothing on another machine.
  The library's GUID is `OriginalTypeLib`, after the libid in the extended record. Without this, MSForms would read as
  not installed, and as different in two projects that both have it.
- **A reference keeps its records as they stand** (`VbaReference.Records`), which the reader takes from the `dir` stream,
  so any kind can be copied, from any kind of workbook, `.xls` included. Control references and references to other VBA
  projects are copied like any other, once they can be found: their records have only been read back here, so whether
  Excel accepts them is for the user's check. If it doesn't, they should be left out and listed instead.
- **A new project takes the source's code page,** so its bytes needn't change. Into a project in another code page,
  source is converted strictly: a character that can't be held stops the run, as a question mark in a string would be a
  silent change to what the code does. The same code page keeps the bytes as they are, which don't always decode.
- **A target's code names count as names taken** when it has no project, and a code name the tool gives keeps clear of
  the modules coming in (`Sheet2`, `ThisWorkbook1`). An `.xlsb`'s are in binary parts, so they can't be seen, and Excel
  sorts those out itself.
- **Identical ignores only what ends a module,** since the VBA editor keeps or drops a last line as it likes. Anything
  else is a difference, even in an attribute, as that's what the person is being asked about.
- **`.xlam` and `.xltm` aren't targets,** as only `.xlsx`, `.xlsm` and `.xlsb` have been tried in Excel.
- **`--drop-signature` is how a signed target goes ahead,** as the plan's "told to go ahead". It removes the signature's
  parts, relationships and types, rather than leave one that no longer matches. No real signed workbook has been tried.
- **A reference is added even if every module was skipped or identical,** as those modules may need it.
- **Where the library is looked up is this machine,** which may not be where the workbook is used. There's no better
  place to look, so the README says to run it where the workbook will be used.

## Speed (step A2 of the feedback plan)

- **`--verbose` shows how long each step took:** reading, parsing, analysing, building the page, writing the module
  files and writing the page.
- **Measured on 2026-11-03** with the published executable, on the private workbook (tens of modules) on a local disk.
  Each step took 80–180 ms, about 0.6 s in all, plus about 0.3 s to start.
- **The pause users saw is antivirus scanning.** Writing the page sometimes took 2–3 s. Windows Defender scans a
  script-heavy file the first time anything opens it. That happens when the run reads the previous page to see
  whether it changed, or overwrites it. Always writing without reading, and writing a new file then renaming it over
  the old one, were both measured and cost the same, so neither was kept. A browser opening the page waits for the
  same scan.
- **Mermaid is almost all of that cost.** It's 3.6 MB of the 4.3 MB page. The same page without it scanned in about
  0.2 s instead of 3 s. Removing Mermaid (step F1) is the fix.
- **ReadyToRun was measured and not taken.** It saved about 0.2 s a run, and made the executable 12 MB bigger.
- **After removing Mermaid (0.7.0),** measured the same way: the private workbook's page is 0.8 MB, writing it takes
  1–2 ms on every run, and the first read of a newly written page (what a browser waits for) takes 0.1–0.2 s. The
  other steps are unchanged, and the executable is 3.6 MB smaller.
- **The shared library** (0.8.1, step C1 of the feedback plan). Opening a workbook, exit codes, `ToolVersion`, the
  folder writer behind `ProjectExporter`, the page-building helpers and d3 moved to `shared/`, as one copy with pqdoc's
  (see [shared/README.md](../../shared/README.md)). vbadoc's messages and file formats are unchanged: every fixture's
  output was byte-identical apart from the build stamp. Two things changed underneath: module files and the page are
  now written atomically, through a temporary file, as pqdoc's outputs already were; and the page's JSON gets the same
  script-element escaping as pqdoc's, which changes nothing because the serialiser already escapes those characters.
- **The page shell** (0.8.2, step C2). The top bar, tabs, theme, stored settings, source panel and splitter, Load folder
  and the source view are the shared shell's now (see [shared/README.md](../../shared/README.md)); vbadoc's page keeps
  its views, search, trail, Home and what the panel shows. `page.html` became three fragments (`search.html`,
  `trail.html`, `views.html`) that `HtmlPageWriter` gives to `ShellPage`. The page looks and works as before, checked
  pixel by pixel. The panel's ids are now `side-panel-…`, and marked lines use the class `marked`; stored settings keep
  their keys. The heading and Load folder gained tooltips.

## Source analysis (stages 2–3)

- A member's source **includes the block of comments directly above it**.
- **Property references:** an assignment target (`o.Status = x`) resolves to `Property Let`, `Set o.X = y` to
  `Property Set`, and everything else to `Get`. The class diagram still shows each property once.
- **Type references in declarations** (`Dim o As Order`, parameter and return types) count as **Uses**.
- **References to a module that hit no project member** (`Sheet1.Range`, `New Order` without a `Class_Initialize`)
  count at module level. At member level they link to a "(module)" node for that module, so nothing silently disappears.
- **Calls through an interface-typed variable** (`repo.LoadAll`) link to the interface member, not a guessed
  implementation. Implementations are reachable through Implements edges.
- **Recursion** shows as a self-loop.
- **Type-declaration characters** (`Dim s$`, `Function Name$()`) are mapped to their types, so the class diagram shows
  `String` rather than a `$` (which Mermaid, used until 0.7.0, read as "static").
- **Late-bound references** are reported as a count in the console and as an "Unresolved" list per member in the page.
- **Unreferenced members** that don't look like entry points (event handlers, public parameterless Subs in standard
  modules, `Class_Initialize`) are flagged: a dashed style, a filter, and a console list. This can never be certain,
  because `Application.Run`, button `OnAction` and late-bound calls are invisible to static analysis.
- **Handlers for worksheet ActiveX controls** (e.g. `Sheet1.btnRun_Click`) are labelled "control event handler
  (control not in VBA project)", with no control node. Those controls live in the sheet XML, not in the VBA project.
- **Real files have `Attribute` lines inside procedures** (e.g. `Attribute TestMacro.VB_Description = "..."`), and the
  parser must skip them.

### Parser (stage 2)

- **The parser reads the exported file's text**, header included, so every line number matches the file on disk
  and the source shown in the page.
- **Procedure bodies are stored as a list of statements** (split at `:`, comments and labels removed), including
  `Dim`/`Const`/`ReDim` lines, because their array bounds and values can reference other members. Locals are also
  listed separately with their types. Interpreting statements is stage 3's job.
- **Both branches of `#If … #Else` are parsed.** A Declare in both branches appears twice (same name, different lines),
  so stage 3 must give members ids that stay unique in that case. (Replaced in A6: such a name is now one member.)
- **VBA's own declaration rules are followed:**
  - in `Dim a, b As String`, only `b` is a String (`a` is untyped, i.e. Variant);
  - module-level `Dim` and `Const` are Private; procedures, Enums, Types and Declares default to Public;
  - an identifier followed by `:` at the start of a line is a label (keywords such as `Else:` excepted).
- **Parser warnings never stop a run.** Lines the parser doesn't understand are reported in the console and skipped.
  All SampleVba, SampleVbaExtras and real-workbook modules parse with no warnings, and the tests require that.
- **`--verbose`** lists every member with its line range. It isn't in the requirements, but it makes the parser's
  results visible from the command line.

### Dependency model (stage 3)

- **Node ids:**
  - modules: the module name, e.g. `Order`. Each module has a node with that id in the member-level graph too,
    so a selection can carry over between levels and tabs;
  - members: `Module.Name`; properties `Module.Name[Get]` / `[Let]` / `[Set]`; enum values `Module.Enum.Value`;
  - a name declared twice (under `#If`/`#Else`) gets `~2` on the second id.
- **Edges carry a count** (how many references they stand for). Module edges add up member edges between different
  modules, plus one Implements edge per `Implements` statement. Member-level Implements edges aren't added again.
- **Name lookup order:** locals and parameters, then the module's own members, then module names, then public members of
  standard modules and public Enum values, then Enum names. If two modules declare the same global name, the first
  module in name order wins (VBA itself would report an ambiguous name).
- **`New Order` links to the `Order` module node**, not to `Class_Initialize`.
- **Accessing another module's Private member**, or a name that isn't a project member, counts as using that module.
- **References to the member's own module node are dropped** (e.g. `Me.Range` in a sheet).
- **`ThisWorkbook` also finds the workbook module under a localised name** (e.g. German `DieseArbeitsmappe`). It is
  recognised by the Workbook class id in `VB_Base`.
- **Late-bound** also covers member access on the result of `CreateObject`/`GetObject`. It is reported as an expression
  such as `CreateObject(…).OpenTextFile`, with its line.
- **Member roles** (entry points that are never flagged as unreferenced):
  - macro: a public parameterless Sub in a standard module;
  - worksheet function: a public Function in a standard module. This refines the earlier list, because such
    functions may be used in cell formulas;
  - built-in event handler: `Workbook_`, `Worksheet_`, `Chart_`, `UserForm_`, `Class_Initialize`/`Class_Terminate`;
  - WithEvents handler;
  - control event handler: any Sub with an underscore in a document or form module;
  - interface implementation.
- **Unreferenced** means nothing other than the member itself refers to it:
  - enum values are never flagged;
  - an Enum counts as used if any of its values is;
  - declarations repeated under `#If` share one answer.
- **Known limits:** names inside strings (`Application.Run "Foo"`, `OnAction = "Module.Foo"`) and calls through
  collections such as `ThisWorkbook.Sheets("X").Foo` can't be seen. This is why unreferenced members are reported with
  a caveat.

## Test fixtures

- **`Fixtures/SampleVba`** stays unchanged. Its files have LF line endings and use `.doc.cls`; tests normalise them.
- **`Fixtures/SampleVbaExtras`** (added in stage 2) covers what SampleVba lacks: `Const`, `Enum`, `Type`, `Declare`,
  `Property Set`, line continuations, `Rem`, type-declaration characters.
- **`Fixtures/Workbooks`** contains real Excel workbooks from Apache POI's test data (Apache-2.0).
  None has a UserForm, so forms are tested only with synthetic data until a real sample is available.

## Page (stages 4–5)

### Built in stage 4a

- **The page's data is indented JSON** in a `<script type="application/json">` block: one value per line, so git diffs
  stay readable. `<`, `>` and `&` are escaped as `<` and so on, so the data can't end the script element.
- **A Content-Security-Policy** (`default-src 'none'`, inline scripts and styles only) makes the browser itself refuse
  any network request. "Nothing loads from the network" is enforced, not just intended.
- **Embedded source** goes in hidden `<pre>` blocks (HTML-escaped, read back with `textContent`), not
  `<script type="text/plain">`: VBA strings can contain `</script>`, which can't be escaped inside a script element.
- **Load folder** reads only files directly in the chosen folder (so `.git/` is ignored), decodes them with
  `TextDecoder` using the project's code page, and checks each against the SHA-256 recorded in the page. It warns about
  files that changed or are missing.
- **Theme:** a stored choice wins; otherwise the page follows `prefers-color-scheme`. **Chromium reports "light" when the
  user has no preference** (indistinguishable from choosing light), so "can't report a preference" can only mean a
  browser without the feature, which gets dark. The theme is set by a small script in `<head>` before the page is drawn,
  so it doesn't flash.
- **A search box in the top bar** selects any module or member by id (exact or partial match). This is the search box
  agreed for the graph, made available to both views.
- **Views plug into `window.vbadoc`:** `select(id)`, `selection()`, `onSelectionChange`, `theme()`, `onThemeChange`,
  `activeView()`, `onViewChange`. There is one selection for the whole page, so it carries across tabs automatically.
- **The page title is the workbook's file name.** The VBA project name is almost always "VBAProject".
- **Browser tests are a separate project** (`tests/VbaDocumenter.BrowserTests`). It downloads Playwright's Chromium
  (about 150 MB) on first run. `dotnet test --project tests/VbaDocumenter.Tests` runs only the unit tests, with no
  download.

### Built in stage 4b (dependency graph)

- **Colours were validated, not eyeballed**, using the dataviz palette validator. Any two graph nodes can be next to
  each other, so all pairs must be distinguishable, and only three categorical hues pass that test. So:
  - colour shows three module groups: standard module (blue), class or interface (orange), UserForm or document (aqua);
  - shape shows all five kinds: square, circle, hollow circle (interface), diamond, triangle;
  - edge kinds differ by dash pattern first (Uses solid, Implements dashed with a hollow UML-style arrow, Events
    dotted) and by colour second (grey, violet, green);
  - the palette's status green is deliberately not used.
- **Member level groups each module's members** in a cluster with a shaded outline and the module's name. Member shape
  shows the member kind; colour carries over from the module group. A module's own node (for references such as
  `As Order`) is only drawn when something refers to it.
- **Labels:** module and member names are always shown by default (revised after testing on a dense real workbook;
  the earlier "member names only when zoomed in" is now a setting). Labels are clickable, because people aim at names.
- **Names never overlap at member level.** Each member is laid out as a rectangle covering its mark and its name
  (measured in the drawing font), and rectangles in a module are kept apart. Modules are kept apart as whole
  rectangles. Any overlap left when the layout comes to rest is removed outright. Browser tests check this.
- **Highlighting:** Direct highlights the edges touching the selection and the nodes at their other ends. Transitive
  follows every chain downstream and upstream. A module selected at member level selects its whole cluster; a member
  selected at module level shows as its module. The page's selection itself isn't changed by switching level.
- **The starting layout is computed in one go** (d3-force ticks run before drawing). d3-force starts from fixed
  positions, so the same project and settings always give the same first picture; "Re-run layout" returns to it.
  The simulation is then kept, not discarded:
  - dragging a node, or a module's outline (which moves the whole module), wakes it so the graph rearranges live;
  - a module holding a dragged node stays put and pushes others away, so it isn't torn apart;
  - "Keep dragged nodes where dropped" pins them (double-click releases); off by default;
  - **a click leaves the layout alone.** Nothing is pinned and the simulation isn't woken until the pointer moves
    more than the 4-pixel click distance, as in pqdoc. Waking it as the button went down (fixed in 0.6.2) let every
    click pull modules together, because the running forces only partly separate members and full overlap removal
    waits until the graph comes to rest.
- **Edges are single curves.** Bundling edges between modules was tried and rejected as the default; it remains an
  option, off by default.
- **A settings panel** (Settings button in the graph toolbar) controls names, text and node size, edge opacity and
  curves, bundling, module outlines, dragging behaviour and the layout forces. Changes apply straight away (layout
  changes animate). Settings are remembered per browser, with a reset button. The Show… and Settings drop-downs close
  on a click outside them or Escape.
- **Focus re-lays out only the highlighted neighbourhood** and follows the selection while on. "Show all" leaves it.
- **Direction toggle (added later):** All / Dependencies / Dependents, next to Direct/Transitive; Focus follows it
  too. Edges between two selected nodes are always highlighted, which is what keeps a selected module's internal
  edges visible at member level. The Uses / Used by list in the source panel ignores it, like Direct/Transitive.
- **Remembered per browser:** level, Direct/Transitive, the direction and the settings panel. Member filters weren't remembered at
  first; since stage 5b the Show… choices are (see below).
- **Edge thickness grows slightly with the reference count.** Hovering an edge shows its count; hovering a node shows
  its kind and how many things it uses and is used by.

### Built in stage 5 (class diagram), redrawn with d3 in 0.7.0

Stage 5 drew the diagram with Mermaid. In 0.7.0 (step F1 of the feedback plan) the page draws it with d3 instead:
Mermaid laid boxes out itself, so they couldn't be moved; it took about 20 s for 300 modules and so dropped
dependencies over 150; and its 3.6 MB was almost all of the antivirus scan that slowed every export (see Speed).

- **The C# side (`ClassDiagram`, unit-tested) works out each member's line and the relationships**, and the page data
  carries them as plain text (`members[].class`, `classEdges`). The page sets all text with `textContent`, so no name
  or value needs escaping and none can be read as markup.
- **Box contents:**
  - methods `+Name(params) : Type`; attributes `-m_Id : String`; Friend is `~`;
  - events, constants, enums, types and declares are prefixed with `Event`, `Const`, `Enum`, `Type`, `Declare`;
  - a property is shown once, on its first accessor, as `+Property Status : String «Get/Let»`; an indexed property
    (with parameters besides the value) is drawn as a method so its parameters show;
  - enum values are left out (the Enum is shown); constant values are cut to 40 characters;
  - a declaration repeated under `#If`/`#Else` that gives the same line is shown once.
- **Relationships:** Implements is realisation. A module-level variable typed as another project module (other than a
  standard module) is an association labelled with the variable name, prefixed `WithEvents` for event sources. Every
  other module edge is a dependency, except where an association already links the same two modules (which also
  covers the WithEvents Events edge).
- **Boxes** are UML-style: a header with the stereotype and name, then an attributes and a methods compartment. Text
  is measured in the font it's drawn in, so each box fits its widest line, between 110 and 520 px. A longer line is cut
  short with "…" and shows in full in a tooltip.
- **The layout is worked out before drawing**, so the same project and Show… choices always give the same picture:
  - **users sit above what they use**, so arrows point down and widely used modules sink to the bottom. This applies
    to Implements too, so an interface sits below its implementations. Mixing directions would make layers contradict
    each other;
  - **modules that use each other in a circle share a layer** (Tarjan's strongly connected groups), as queries in a
    circular dependency share a column in pqdoc. Breaking cycles instead turned the large test project's ring of 300
    modules into 300 layers;
  - a group nothing uses moves down to just above the highest group it uses, so its edges stay short;
  - within a layer, eight sweeps of the barycentre method keep related boxes near each other. Ties keep the current
    order, so the result is fixed;
  - a layer wider than about 1.6 times the square root of the diagram's area (at least 1,400 px) wraps onto more
    rows, so big projects stay roughly as wide as they are tall;
  - boxes are then moved towards the middle of their neighbours in other rows, keeping each row's order and spacing;
  - modules with no relationships go in a grid underneath, in module order.
- **Edges** leave the side of their box that faces the other box and arrive at the opposite side, as curves square to
  the sides. Ends sharing a side are spread along it in the order of the boxes at their other ends. Association labels
  sit at the curve's middle, with a halo in the background colour. Lines keep their width on screen at any zoom.
- **Clicks:** a box selects its module and a line its member. Clicking a property's line selects its first accessor,
  and the line stays highlighted for the other accessors. A press that moves less than 4 px is a click, as in the graph.
- **Dragging** moves a box, and its edges follow. Where boxes are dropped is remembered per workbook and per Show…
  choice (which changes box sizes, so the layout), under `vbadoc-classes-positions|<workbook>|<choices>`, until
  **Re-run layout**. In focus mode the smaller diagram is laid out afresh and moves aren't remembered.
- **Zoom and pan work like the graph:** the wheel zooms, dragging the background pans, with zoom buttons and Fit. The
  starting view fits the diagram but not below 40%, so names stay readable; a diagram that doesn't fit starts at its
  top. Fit and the starting view keep clear of the toolbar and the open legend. A box selected elsewhere slides into
  view: all of it, or its top if it's taller than the view.
- **Large projects:** the 300-module test project, with every dependency, lays out in well under a second. Dependencies
  are never left out automatically any more.
- **Looks:** colours come from the page's CSS variables (box outlines take the graph's module-group colours, edges the
  graph's edge colours), so a theme change needs no redrawing.
- **Remembered per browser:** the Show… options (members, private members, dependencies). Focus follows the selection.
- **The toolbar drop-downs' outside-click and Escape handling moved to `page.js`** and applies to every
  `<details class="dropdown">`.

### Source panel and build stamp (between stages 5 and 6)

- **The source panel can be hidden and resized**, with a Source switch in the top bar, a × in the panel, and a
  splitter (drag, arrow keys, Home/End; double-click or Enter resets). Both views share the one panel, so they share
  its size and visibility, which are remembered per browser. The size is a share of the layout (20–70%), of its width
  or, below 900 px where the panel sits under the view, of its height. Selecting something doesn't reopen a hidden
  panel. The graph keeps its middle in the middle when its view changes size; the class diagram keeps its zoom and
  position.
- **The page shows which build of vbadoc made it**: `vbadoc 0.5.0 (b7c0ebe)` at the right of the top bar, with the
  full commit in its tooltip, and the same text in `<meta name="generator">`. Requirements.md asks for no
  run-specific values in generated files. The stamp is the same on every run of a given build, but changes the page
  whenever the tool is rebuilt from a new commit, even one that changes nothing else in the output. That one-line
  change is the point: it shows the tool changed. The exported module files carry no stamp, so they only change when
  the workbook (or the export itself) does.

### Legend highlighting and Show… (stage 5b)

The Show… menu moved into the explorer in D3c, for every view and the tree, and closing drop-downs moved into the
shell; see The Show… menu in the explorer above. The legend is as below.

- **Legend entries are toggle buttons.** Clicking one greys out everything it doesn't stand for, like a selection
  does, without selecting anything:
  - a node entry (module kind, member kind, "The module itself", Unreferenced, module colour) lights the nodes it
    matches and the edges between them. "Enums and types" includes enum values, which share its triangle;
  - an edge entry lights edges of that kind and the nodes at both ends, so their names stay readable;
  - entries add to each other, and clicking one again turns it off. A Clear link appears while any is on.
- **Legend and selection narrow each other.** With both, only what is in the selection's neighbourhood *and* lit by
  the legend stays lit (e.g. a class plus Implements shows only that class's Implements links). The selected node
  always stays lit. A module selected at member level selects all its members; the legend narrows those too, and
  the module's outline stays lit.
- **Clearing:** clicking the background or pressing Escape clears both the legend and the selection. Escape does
  this only when no drop-down is open (then it just closes it) and focus isn't in a field. Switching level clears
  the legend, because its entries differ between levels. The legend isn't remembered.
- **The Show… menu removes things (and re-runs the layout); the legend only greys them out.** Ticked means shown:
  - both levels: module kinds, edge kinds, and "Nodes with no connections" (a self-loop doesn't count as one);
  - member level: member kinds, private members (Friend counts as not private), and References: All /
    Unreferenced only / Referenced only, replacing the "Only unreferenced members" check box.
- **Show… choices are remembered per browser** (`vbadoc-graph-show`), as the class diagram's are. Because something
  may be left out from an earlier visit, the button counts the choices in effect, e.g. "Show… (2)", and is
  outlined in the accent colour. "Show everything" resets them.
- **The Show… menu has two columns at member level** (modules, edges and other; members and references). It stacks
  them into one when they would run past the edge of the view, e.g. with the source panel open. The menu measures
  this as it opens, before anything is drawn.

### Dependencies list (stage 5c)

- **A collapsible Dependencies section at the end of the source panel's header** lists what the selection uses and
  what uses it. It's in the source panel rather than the graph because that's where details about the selection
  live, it works on both tabs, and it doesn't cover the graph. The cost: hiding the source panel hides it too.
- **Closed by default; whether it's open is remembered per browser** (`vbadoc-dependencies-open`). The state is
  noted when the summary is clicked, not on the `toggle` event, which fires a moment later: by then a new selection
  may already have redrawn the header and lost it.
- **Direct links only, at the selection's own level**: a module lists module edges, a member lists member edges.
  The graph's Direct/Transitive toggle and Show… filters don't apply, so the list always shows everything.
- **Entries:** the name, its kind (muted), a tag for Implements ("implements" / "implemented by") and Events edges,
  and "×n" when the edge stands for more than one reference. A member in the selection's own module is shown by
  name, others as `Module.Name`; a member's link to a module node reads "the module itself". Sorted by name,
  case-insensitively, without `localeCompare` so the order is the same in every browser language.
- **Clicking an entry selects it**, like the search box, so the list is also a way to walk the graph.
- **Recursion is noted ("Calls itself"), not listed**, since selecting the selection would do nothing.
- **Each list scrolls on its own** (about eight entries tall), so a much-used member can't push the source out of
  the panel.

### Home, trail, Path from Home, Back/Forward (stage 5d)

Home and the trail were replaced by favourites in D2, and Path from Home became Path from the nearest favourite; see
Favourites below. Back and Forward stay as they are.

- **Home** is one module or member per workbook, set or cleared with the ☆/★ button beside the name in the source
  panel. It's remembered per browser under `vbadoc-home|<workbook file name>`, because every page opened from disk
  shares one localStorage. A remembered Home that no longer exists in the workbook is ignored. The graph stars it:
  its node (at module level, the module holding a Home member) and, at member level, a Home module's outline name.
  `window.vbadoc` gains `home()`, `setHome(id)` and `onHomeChange(fn)`.
- **The trail is the route actually walked**, not a computed path. It's a strip under the top bar, shared by both
  tabs:
  - every selection adds a step, whatever made it (either view, search, the Dependencies list, the trail itself,
    Back/Forward);
  - selecting a step already on the trail cuts the trail back to it, so loops collapse and clicking an earlier
    step is "go back to there";
  - with a Home the trail starts at Home, and setting a new Home starts a new trail (Home, then where you are);
    without one it starts at the first selection;
  - × starts again from Home (if any) and where you are now. Clearing the selection keeps the trail;
  - the strip shows when there's a Home or at least two steps. Over 7 steps it shows the first, "…" (click to
    show all) and the last 5;
  - the trail isn't remembered: it lasts as long as the page.
- **Path from Home** (the "★ Path" button) highlights the shortest chain of edges between Home and the selection,
  in the graph as drawn (so Show… choices count), at the current level:
  - edges are followed in their own direction only, Home → selection ("Home uses the selection") or
    selection → Home ("The selection uses Home"), whichever is shorter (Home → selection on a tie). A chain that
    changes direction halfway would say nothing about either end;
  - a note in the toolbar describes the result, or says that nothing links them (mentioning the Show… choices
    when any are in effect). With no path, Home and the selection stay lit; with one, only the path does, since a
    Home module at member level stands for all its members but the path touches just one;
  - the view zooms to fit the path. The path follows the selection, like Focus;
  - it's disabled, with the reason in its tooltip, when there's no Home or selection, either is hidden by Show…,
    or Home is the selection at this level. Turning it on leaves Focus (the path may lie outside the neighbourhood)
    and clears the legend; Focus, a legend click, the background or Escape turn it off.
- **Back and Forward:** each selection is pushed onto the browser history as the address's fragment, e.g.
  `Sales-vba.html#Order.Status%5BLet%5D` (the id, URI-encoded). Back/Forward reselect, and a page opened with a fragment
  starts with that selection (unknown names are ignored). Clearing the selection pushes an empty fragment.
  Browsers that refuse history changes for pages opened from disk just don't get this; selection still works.
- **Fit keeps clear of the toolbar's real height**, since the toolbar wraps onto a second row in narrow views.

### Explorer (0.10.0, step D1 of the feedback plan)

- **The explorer is the shell's,** filled by `page.js` (`explorerLayouts()` since D1b); see `shared/README.md`.
- **`'@Folder` is read as Rubberduck reads it:** a comment in the declarations, before the first procedure, with or
  without brackets; the first one counts. It's kept as written in `ModuleModel.Folder` and the page splits it at the
  dots. A module with none goes in No folder, at the end, rather than the project's name as Rubberduck shows it,
  because "No folder" says why it's there.
- **Grouping by kind** only when no module has a folder, in the order standard modules, classes, interfaces, forms,
  document modules; empty groups aren't shown.
- **Members in source order,** not by name, because that's how the module reads. An Enum's values go under it. Kind is
  the detail, the role is a badge, and an unreferenced member is in italics with a tooltip.
- **Browser tests written for the graph's geometry** open the page without the explorer (a stored setting), because
  a narrower view moves nodes under the legend or out of view. The explorer has tests of its own.

### Follow-ups after G1's review (0.28.0, step G1a of the feedback plan)

- **A view keeps everything the page shows,** as the user asked: the selection and whether it's focused, Focus on
  click and Focus on favourites, both panels (shown, size, the source panel's lock), the explorer's layout and the
  widely used share (the shell's, so pqdoc's views keep them too), and vbadoc's own: whether the source panel shows a
  member in its whole module and its Dependencies, the graph's Dependencies / Dependents, Path, legend picks and
  settings, and the class diagram's legend picks. Opening a view no longer clears the selection.
- **Not kept:** favourites and the theme, which are the person's own, and the graph's two dragging settings (Rearrange
  while dragging, Keep dragged nodes where dropped), which are how they like to work rather than what the page shows.
  Nor the zoom: a view opens fitted, as a focus does, so it reads on any screen.
- **A view saved in G1 leaves what it doesn't have as it is** (the panels, Focus on click, the graph's direction and
  settings), rather than setting defaults, so opening an old view changes only what it saved.
- **The graph put the layout on screen back in place of a view's positions,** when the filter and Show… choices were
  the same (after Re-run layout, or a focus the view cleared): opening a view cleared the kept layouts, but the redraw
  kept the one on screen again first. Now the layout on screen isn't kept once a view is opened, and a view's positions
  come before a kept layout. G1's tests changed the filter between saving and opening, so they missed it.
- **A focused graph's or class diagram's moves are kept for that focus,** while the page is open, as the whole graph's
  are: going back to a focus finds what was moved in it, and a view saved with a focus keeps them, with what decided
  the focus (the selection, direction, favourites and reach; or the modules the diagram showed), so they're put back
  only for the same focus. Before, a focused graph or diagram was laid out afresh each time.
- **Save over, on every view but the one showing** (which has Save changes), saves the page into it without opening
  it, keeping its name and description, and makes it the view showing. A new view given a name that's taken offers
  Save over it instead of only refusing. Save over, Save changes, Delete and Import have Undo rather than asking first,
  as Delete already did: one step back, offered until the menu's next message.
- **Import asks first only when the file doesn't match:** when it names another workbook (files now carry the
  workbook's name) or mentions items this one doesn't have, listing them and any views it would replace; a file that
  matches is imported at once, so reading a newer copy of the same file stays one step. A file from G1, without the
  workbook's name, is checked for missing items only.

### Saved views (0.27.0, step G1 of the feedback plan)

- **The views are the shell's,** so pqdoc has them the same way (see `shared/README.md`). The shell keeps a view's
  name, description, tab, filter, hidden items and Show… choices; each of the tool's views adds its own part through
  `vbadoc.viewPart(name, { save, apply, ids })`: `graph`, `classes` and `calls`.
- **"The focus set" in the plan is the explorer's filter,** which D3a made: what a view shows. The selection, and so a
  focus on it, isn't kept: opening a view clears it, so the page looks as the view was saved. Favourites stay personal.
  (Since G1a a view keeps the selection and its focus, as the user asked; see above.)
- **The graph keeps its level, and positions only where something was moved** (dragged, let go, or spaced out
  differently in the settings), for each level, in the whole graph as the view's filter and choices draw it. Every
  node's position is kept, rounded to whole units, since the rest moves around what's dragged; which are pinned is
  listed. A level nothing was moved at is laid out afresh, which gives the same picture, and keeps files small.
  Positions are put back when that level is next drawn; nodes the view doesn't know (the workbook has changed) start
  beside the known members of their module, or what they're linked to, and settle while the rest is held still.
- **The class diagram keeps the Members switch and the moved boxes** of the view's diagram. Moves in a filtered diagram
  are now kept while the page is open, so a view with a filter can keep them; D3a laid a filtered diagram out afresh
  each time. The whole diagram's are still kept in the browser, as before.
- **The Calls tab keeps its starting point, open rows, Calls or Called by, and tree or diagram.** A row is known by its
  path, so after the workbook changes, a row may not open where it did; the ids in the paths are checked like any
  other.
- **Opening a view sets everything at once, and the views draw once:** the shell sets its state without its usual
  events, each part's `apply` sets its own without drawing, then `onViewApplied` (which `page.js` turns into one
  `onShownChange`) and `onViewChange` draw the view's tab.
- **"Changed"** compares the page's arrangement now with how it was when the view was opened or saved, when the menu
  opens; Save changes is offered only then. Which view was open isn't remembered after a reload, since the graph's
  positions aren't either.
- **Import adds, replacing views of the same name,** so reading a newer copy of the same file updates its views, and
  says how many it replaced. Names are unique whatever their case. Delete has Undo, rather than asking first.
- **Missing items are listed, never an error:** on import, in the menu, and on opening, in a note across the top of the
  page (which G2 will use for the views file built into the page), up to eight by name; all of them go to the console
  as a warning.
- **Export is a download** of a `Blob` through a link with `download`, which a page opened from disk can do under its
  Content-Security-Policy, and Import a file input; neither needs a server.

### Follow-ups after E5's review (0.26.0, step E5a of the feedback plan)

- **Sequence diagrams of a whole entry point are in scope now:** the old reasons (too large to read, Mermaid drawing
  large ones wrongly) were Mermaid's, and the page draws its own. **Open all below** (a row's button, `*`, or Shift+click
  on a ▸ or a starting point) opens everything under a procedure or a group, as far as recursion allows, stopping after
  2,000 calls with a note rather than freezing a very large project.
- **Nothing in the diagram is cut short:** lanes are as wide as the widest message between neighbours or lifeline
  name needs (170 to 380 px), and each frame as wide as its label and whole guard (cut only past 80 characters), with
  frames around it widening to hold it.
- **Dragging in the graph is gentler** (0.26.1): a drag now stirs the layout up to an alpha target of 0.1 rather than
  0.3. Measured on the production workbook and the 70-module fixture: dragging a module's outline at member level
  pulled the rest in to 85–93% of its spread until it was let go, and moved it about 120–134 px on average; now the
  spread holds (99.7%) and it moves 50–100 px; at module level the rest moves a third to a half as far. Two other fixes were tried:
  holding everything but the dragged nodes' neighbours still, and stopping the layout when dropped (rolled back after
  review: it froze the graph, so nodes could overlap); and weakening only the forces that pull nodes together while
  dragging (at module level, repulsion then has nothing to balance it, and the graph spread to 1.7 times its size). A
  regression test drags an outline on the 70-module fixture and checks the spread holds.
- **Every graph setting explains itself** in a tooltip on its label (and control).
- **The Show… menu has Show nothing, and All / None on each section of boxes,** for picking a few things quickly; radios
  stay as they are. It's the shell's, so pqdoc's menu has them too.

### The sequence diagram (0.25.0, step E5 of the feedback plan)

- **Drawn from what's open in the Calls tree,** which is what the rejected attempt lacked: that drew a whole entry point
  and was too large to read. The tree and diagram share one record of what's open (the tree's keys), so opening or
  closing in either changes both; a message's ▸ and a frame's label open and close there.
- **One diagram per open starting procedure,** stacked, each with its own lifelines in the order modules first take
  part. Messages follow the code's order; a call within a module is a message to itself; an implementation or handler
  reached through an interface member or RaiseEvent is a dashed message; late-bound calls go to a "late-bound"
  lifeline, so the gaps show. Activation bars show how long each opened procedure runs.
- **Blocks are frames,** labelled with their keyword (if, else if, else, select, case, loop, with, on error) and the
  statement as a guard; frames nest, outer ones wider, and a closed frame stays as an outline to open again. Their
  labels are drawn last, over lifelines.
- **Plain SVG with d3, sized to fit and scrolled** rather than zoomed: it's read top to bottom like the code. Text is set
  as text, and colours come from the page's variables, so it follows the theme.
- **Called by has no diagram:** a sequence diagram follows calls forwards. The switch is disabled there.
- **Requirements' Out of scope** kept the rejected whole-entry-point diagram at first; after E5's review it went (see
  below), as its reasons were Mermaid's.
- With every branch of the production workbook open four deep, its 77 diagrams draw in about 230 ms; a toggle redraws
  in about 130 ms.

### The logic around each call (0.24.0, step E3 of the feedback plan)

- **The parser follows block statements** (`BlockTracker`) and gives each statement its innermost block and each
  procedure its `Blocks`; calls carry their block into the page data (`blocks`, and `block` on each call).
- **A block's own statement is in the block around it,** since that's where it runs: an If's condition, a For's range,
  a Select's expression. So a call in a condition is listed just before its block's row.
- **A single-line If is split into its parts** ("If x Then", the Then statements, "Else", the Else statements), with
  everything after Then on the logical line belonging to it, colons or not, as in VBA. The parser test that kept one
  as a single statement now checks the split.
- **An On Error handler** runs from the label On Error GoTo names to the end of the procedure.
- **Under #If,** each branch starts from the blocks open at the #If, and the first branch's carry on after #End If.
- **Unbalanced closers** (an End If with nothing open) are just statements.

### The Calls tab (0.23.0, step E2 of the feedback plan)

- **Core lists every call in the order of the code** (`DependencyModel.Calls`, `calls` in the page data), recorded by
  the resolver as it reads each statement left to right, from E1's references: a call to a procedure, property (each
  side), event (RaiseEvent) or Declare, or a late-bound call with its expression. Assigning a Function's result to its
  name isn't a call; calling it with arguments is recursion. The page builds the tree from these, so nothing is laid
  out ahead and only open rows are drawn.
- **Clicks only select in the tree** (agreed with the user; the guidelines record the exception): following an
  execution path means reading each procedure's source while the path stays open, which re-rooting on every click
  would undo. Ctrl+click, a focused selection made elsewhere, or a focused module (its procedures) starts the tree
  there; Entry points, or clearing the selection, goes back.
- **Entry points** are the existing roles: macros, built-in and control event handlers, and worksheet functions, plus
  favourite procedures. WithEvents handlers and interface implementations aren't entry points: the tree reaches them
  through RaiseEvent and the interface member.
- **Interfaces and events** are followed as the code runs them: a call to `IOrderRepository.Save` opens to each class's
  `IOrderRepository_Save`, and RaiseEvent to the handlers. Turned round, an implementation opens to the interface member
  it implements, and that to what calls it.
- **What the views don't show** (hidden, filtered out, or left out by Show…) is left out of a node's calls and counted
  in one row, rather than hidden silently. The roots are the user's choice, so they're always listed.
- **Each call is its own row,** so a procedure called twice is listed twice, at each line. Opening a row draws the whole
  tree again, keeping what's open (about 45 ms per open on the production workbook with every branch open six deep);
  drawing only the opened row is the next step if that ever grows.

### Uses / Used by with references (0.22.0, step E1 of the feedback plan)

- **Every member edge has its lines** (`Edge.Lines`, `lines` in the page data): the lines of its module's file, as the
  page shows it, where its references are, each once and in order. Module edges have none of their own: a module's list
  gathers its members' references, so choosing a line can select the member that holds it.
- **The line is the reference's own,** not just its statement's: a statement or declaration continued with " _" puts a
  reference on the first of its lines that names the target as a word. The parser keeps each statement's number of
  continued lines and the module's physical lines for this; without them, two references on the production workbook
  pointed at the line above. Type fields now have their own line too.
- **Each item once** (the plan's section 5): an item linked in two ways (ProgressReporter uses OrderService and handles
  its events) is one entry with both tags, and the summary counts items, not links.
- **The list is the shell's** (see `shared/README.md`), so pqdoc's is the same. vbadoc keeps it inside the collapsible
  Dependencies section of the source panel's header, as before.
- **Choosing a line selects what holds it,** which focuses the views on it if Focus on click is on, as any selection
  does, then scrolls the source to the line and marks it until the next selection.
- **`Edge` compares its lines by value,** so equal models stay equal.

### Widely used items and predeclared classes (0.21.0, step D4 of the feedback plan)

- **Core counts, the page decides** (the plan's "worked out in Core ... with the share in the page's settings"):
  `Usage.UsedByOtherModules` gives each module and member the number of other modules that use it, through any kind
  of edge, as `usedBy` in the page data (left out when none). The page lists what's used by at least
  max(2, share × the other modules), so the share can change without making the page again.
- **A module stands for its members:** it's used by at least as many modules as any of its members, so the group lists
  modules, each with its widely used members under it, rather than modules and members side by side. Hide all hides
  the modules listed, which takes their members with them.
- **The default share is 20%,** not the 25% first thought of: on the user's production workbook the most used module
  is used by 8 of the 36 others (22%), so a quarter would have listed nothing. It's a page setting, in the Show… menu,
  remembered per browser as `vbadoc-widely-used-share`; it isn't counted as a Show… choice, since it doesn't change
  what's shown.
- **The group and markers are the shell's** (see `shared/README.md`), so pqdoc has them too. The group sits after the
  layout and before Hidden, closed to begin with; Hide all becomes Show all once everything listed is hidden.
- **Predeclared** is a class's `VB_PredeclaredId = True`, as `predeclared` in the page data (left out otherwise). Only
  classes are marked: forms and document modules always have a default instance, so it says nothing about them. The
  class diagram keeps «class» for them.

### The Show… menu in the explorer (0.20.0, step D3c of the feedback plan)

- **One Show… menu, the shell's, in the explorer,** for the tree, the graph and the class diagram (the plan's "one set
  of filters"), replacing the graph's and the class diagram's own; see `shared/README.md`. vbadoc gives the sections
  and says, through `vbadoc.kept(id)`, whether the choices keep a module or member: its module's kind, its kind of
  member, private members and references. The views apply the rest: kinds of line and items with no connections.
- **A choice applies wherever it means something,** so the graph's and the class diagram's choices merge: member kinds
  and references now reach the class diagram's boxes and the tree, module kinds the class diagram and the tree, and
  items with no connections the class diagram (a box with no relationship drawn). Lines are Uses (the graph's uses and
  the class diagram's dependencies), Implements (both), Events (the graph's) and Holds in a variable (the class
  diagram's associations), each saying where it applies when that's one view. The menu no longer hides member choices
  at module level, since they apply to the tree and the class diagram there too.
- **What's ticked stays,** as in D3a: the views' filter overrides Items with no connections in both views.
- **The class diagram's Members option is a switch in its toolbar,** since it's how boxes are drawn rather than what's
  shown; it keeps its remembered setting (`vbadoc-classes-members`). Moved boxes are now remembered per Members switch
  and set of Show… choices, so boxes moved before are laid out afresh once.
- **Choices remembered before, by the graph (`vbadoc-graph-show`) and the class diagram (`vbadoc-classes-private`,
  `-dependencies`), are ignored,** the plan's simple answer, as for Home in D2; the new set is `vbadoc-show`.
- **The menu fits the window:** it takes the room below it and moves left if it would run off, in place of the graph
  menu's two columns that stacked; its sections flow into two columns where there's room.
- **Drop-downs are the shell's:** pressing outside one or Esc closes it, in the explorer and the views, as vbadoc's did.
- **`window.vbadoc`** has `memberCategories`, `kept(id)`, `choice(id)`, `choiceValue(name)`, `choicesKey()`,
  `choicesCount()` and `onChoicesChange(fn)`.

### Hiding and Focus on favourites (0.19.0, step D3b of the feedback plan)

- **Hiding is the shell's,** as favourites and the filter are, remembered per workbook as `vbadoc-hidden|<workbook file
  name>`. vbadoc says what goes with an item: a hidden module takes its members (in the graph at member level, and
  their rows are struck through under it), and a hidden member goes alone, leaving its module and its module's box,
  which no longer lists it.
- **What the views show is one record,** `vbadoc.shows(id)`: in the filter and not hidden, so hiding wins over a tick.
  The graph and class diagram draw from it and redraw on `onShownChange`; the graph keeps a layout per filter and
  hidden set, as per level.
- **The panel's note** says when the selection is hidden (with Show it, which for a member hidden with its module
  shows the module) or how many of the items it's linked to, either way, are; those are tagged "hidden" in its
  dependencies. The count is of items, not edges.
- **Focus on favourites** (the plan's "Focus can take the favourites as well as the selection") is a page-wide setting,
  `vbadoc-focus-favourites`, turned on from the Favourites group's row, not per workbook, as Focus on click isn't. It
  adds to the focused selection rather than replacing it: the graph shows what the selection's focus shows and the
  favourites with what they use, as far as the reach setting goes; the class diagram, the favourites' modules and the
  modules they use. The graph is drawn again only when what decides it changes, so clicking around inside it keeps
  the layout.
- **`window.vbadoc`** has `hidden()`, `isHidden(id)`, `toggleHidden(id)`, `shows(id)`, `shownKey()`,
  `onShownChange(fn)`, `focusFavourites()` and `setFocusFavourites(on)`.

### The views' filter, and Expand / Collapse all (0.18.0, step D3a of the feedback plan)

- **The filter is the shell's** (see `shared/README.md`), as favourites are, so pqdoc has it too, and it's remembered
  per workbook as `vbadoc-filter|<workbook file name>` (agreed with the user after D2).
- **What a tick covers is vbadoc's:** a module brings its members, and a member its module. So `vbadoc.inFilter(id)`
  is true for a member whose module is ticked, and for a module with a ticked member. At member level the graph shows
  the ticked members (and every member of a ticked module); at module level and in the class diagram, the module stands
  for them. The filter's count is of modules, which is what both levels share.
- **What's ticked is shown even when it's linked to nothing:** the filter overrides Show unconnected, since hiding
  something just ticked would look like the filter had failed.
- **Focus works within the filter:** a selection outside it isn't drawn, so the graph shows the filter as it is. The
  graph keeps a layout per filter, as it does per level and Show choices; the class diagram lays a filtered diagram
  out afresh and doesn't remember moves made in it, as when focused. (Since G1 it keeps them while the page is open,
  for saved views.)
- **`window.vbadoc`** has `inFilter(id)`, `filterActive()`, `filterKey()` and `onFilterChange(fn)`. Space in the
  explorer ticks, so Enter alone chooses.

### Favourites (0.17.0, step D2 of the feedback plan)

- **Favourites replace Home and the trail** (the plan's decision 4): the trail wasn't as useful as expected, and one
  Home was too few. They're the shell's, so pqdoc has them too (see `shared/README.md`): any number, starred from an
  explorer row or beside the name in the source panel, listed at the top of the explorer, remembered per workbook as
  `vbadoc-favourites|<workbook file name>`. A Home remembered before (`vbadoc-home|…`) is ignored rather than turned into
  a favourite, the plan's simple answer.
- **The graph stars every favourite** as it starred Home: its node, the module holding a favourite member at module
  level, and a favourite module's outline name at member level.
- **Path from the nearest favourite** searches from every favourite at once, so the first it reaches is the nearest,
  in either direction, and the note names it ("The favourite Main uses the selection through 1 other node"). The
  selection doesn't count as its own favourite. Without favourites, or with only the selection, the button is disabled
  with the reason in its tooltip.
- **`window.vbadoc`** has `favourites()`, `isFavourite(id)`, `toggleFavourite(id)` and `onFavouritesChange(fn)` in place
  of `home()`, `setHome(id)` and `onHomeChange(fn)`. `trail.html` and its styles went.

### Declarations repeated under `#If` (0.16.0, step A6 of the feedback plan)

- **A name declared again under `#If` is one member.** VBA compiles one branch and won't accept a name declared twice,
  so a later declaration of the same name and kind (and the same Get, Let or Set) is another version of the first when
  either is under `#If`. The parser folds it in: a second procedure header before any of the body (early or late
  binding, the user's case: before, the first header had no body and a missing End line, and the second got it all),
  complete versions in different branches (their bodies are one body), versions in separate `#If` blocks with
  complementary conditions, and variables, constants, Declares and Events. Every branch is still read: the page
  documents every version, and the user chose this over working out which branch is active from the project's
  conditional compilation arguments, which `Win64`, `VBA7` and `Mac` keep from always being decidable.
- **The member spans its versions:** from the `#If` above the first (or its comment block) to the `#End If` after the
  last, or to its `End` line, so the source panel shows them all. Another member between versions in separate blocks is
  inside that span; that's rare, and accepted.
- **`Type` and `Parameters` are the first version's;** `AlternativeTypes` and `AlternativeParameters` keep the others
  that differ. The signature and the class diagram show the types joined (`As Scripting.Dictionary | Object`, a missing
  type being Variant), and a parameter's types the same way when the versions' parameters line up by name.
  Dependencies come from every version's types and parameters, and a reference through the member follows the first
  of its types that's a project class or Type (`ProjectIndex.TypeFromNames`), so `m_Widget.Spin` resolves whichever
  branch says `As Widget`. A variable is associated with a class in the class diagram if any version's type is one.
- **Not folded:** Enums and Types declared twice (rare, and their values or fields would need merging), and a name
  declared twice outside `#If`, which VBA wouldn't compile; those keep the `~2` id and share one answer on whether
  they're referenced.

### Speed on large projects (0.15.0, step D1d of the feedback plan)

- **Measured on a generated project the size and shape of the user's** (`PageFixture.UserSizedEmbedded`: 70 modules in
  folders, about 1,500 procedures, about 320 module and 1,540 member dependencies), in headless Chromium with the CPU
  slowed four times to stand in for a work machine: frame times and long tasks while panning and zooming at both
  levels, hovering, and scrolling the explorer and source panel, and Chromium's own counters for script, style and
  layout.
- **Hovering laid out the whole graph again.** Showing or hiding the tooltip by `hidden` (display) took it out of the
  layout and back, and the browser then laid out every SVG element: about 140 ms a time there, 400 ms of every hover at
  member level. The tooltip is now hidden by visibility, keeping its place in the layout, and contains its own layout;
  hovering 25 nodes went from 10 s of long tasks to under 0.2 s. pqdoc's tooltip had the same pattern and the same fix.
- **The whole graph's layout is kept** when a focused graph or the other level replaces it, per level and Show…
  choices, and put back when it returns, with pinned nodes still pinned. Clearing a focus went from 4.3 s to 1.6 s
  there (the rest is drawing it). The first layout is unchanged, as are Re-run layout and Reset to defaults, which start
  afresh; a change to a layout setting forgets the other kept layouts, which were made with the old setting.
- **With focus on click on, a double-click's first click focuses the graph** and redraws it around the node, so the
  second lands elsewhere and a pinned node isn't released; and since the whole graph's layout is now kept, the pin is
  still there when the focus is cleared (before, laying it out again dropped it). Ctrl+double-click releases a node
  (the Ctrl+click selects without focusing), as does Release pinned in Settings. The user chose to leave it so,
  rather than make every focusing click wait the length of a double-click.
- **Zooming at member level is left as it is.** Its cost is painting about 1,500 member names at each new scale (taking
  the names away halves it; their halos, arrowheads, outlines and edges cost little), and the user would rather keep
  every feature than gain speed, rating the lag at about 3 out of 10.
- **The speed tests** (`SpeedTests`) keep a generous budget for hovering on that project (800 ms of long tasks for 20
  hovers; the old tooltip took about 1,700 ms, the new one none), and check what can't vary: the tooltip's style, and
  layouts coming back unchanged.

### View controls and the summary (0.14.0, step D1c of the feedback plan)

- **Focus on click is the shell's, one setting for the page** (`vbadoc-focus-on-click`, on unless turned off), and the
  Focus button at the start of each view's toolbar shows and changes it; it no longer reads Show all. The views ask
  `vbadoc.focused()` when they draw: the graph draws only the selection's neighbourhood (Direct or Transitive, and the
  direction), and the class diagram the selected module and its relations. Path from Home is found in the whole graph,
  so while it's shown the graph isn't focused. Turning the setting on focuses on the current selection, so the button
  still does what it did when something is selected.
- **Ctrl+click (Cmd+click) does the other thing for that click,** in the graph, the class diagram, the explorer and the
  source panel's links; `vbadoc.select(id, other)` passes it to the shell. In the graph a plain click on the selected
  node focuses it again, so a Ctrl+click is undone by a click.
- **Esc clears the selection in the shell,** in the capture phase, so an open menu, or a box with text in it, can take it
  first. The graph's and the class diagram's own Escape handlers still clear the legend.
- **Direct / Transitive moved into the graph's Settings** (`reach`, in `vbadoc-graph-settings`), as it's seldom changed.
  A choice stored the old way (`vbadoc-graph-reach`) is kept.
- **The class diagram's legend works as the graph's:** kinds of box and relationships are toggles; a kind lights its
  boxes and the lines between them, a relationship its lines and the boxes at their ends; Clear, the background or
  Escape turns them off. Not remembered, as in the graph.
- **The summary in the top bar** gives modules, members (not counting the modules' own nodes), and module and member
  dependencies, as the console does, and when the workbook was last saved (`WorkbookFile.LastSaved`, on
  `VbaProject.Saved`): from `docProps/core.xml`'s `dcterms:modified` in a zip-based workbook, or the summary
  information's last-saved time in an .xls. It's written in UTC, so the page stays deterministic, and the page shows
  it in the viewer's own time.
- **Tests not about focus open the page with it off** (an init script that leaves a changed setting alone), since a
  selection would otherwise leave out most of the graph.

### `'@Folder` after a label (0.13.0, step D1e of the feedback plan)

- **The annotation can follow a label ending in a colon,** as in a header comment block of labelled values tabbed to
  line up (`' Folder:<tabs>@Folder("Reports")`), which is how the user writes their module headers. The label is any
  text without `:`, `@` or `"`. Rubberduck reads an annotation only when `@` comes straight after the `'` (its
  `annotationList` rule), so it ignores such a line as a plain comment, and the module stays in its project's folder
  there; vbadoc already allowed spaces before the `@`, which Rubberduck doesn't either.
- **Prose that mentions the annotation still doesn't count** (`' A module about @Folder("x") annotations`), which is
  why it's a label and not anywhere in the comment. As before, only comments in the declarations count, and the first
  one wins.

### Panel controls (0.12.0, step D1a of the feedback plan)

- **The panels' buttons are icons at the ends of the top bar,** each on the side of the panel it opens; the shell's
  (see `shared/README.md`). The source panel's is `</>` in both tools, though pqdoc's is the details panel, so the
  same button does the same thing in each.
- **The source panel follows the selection unless locked,** and closing it by hand unlocks it: a lock means "keep it
  open", so closing it is the end of that. Whether it's shown is no longer remembered, only the lock; the old
  `vbadoc-source-hidden` setting is ignored.
- **The views keep still when the panel opens or closes with the selection,** rather than keeping their middle in the
  middle, which moved the whole graph by half the panel's width on every click and every clear. The graph and class
  diagram move the selection into sight only if the opening panel would cover it. Resizing the panel by hand, or the
  window, still keeps the middle in the middle. Before this, the resize also interrupted the move into sight.
- **Browser tests about the panel's size, or laid out beside it, open the page with it locked open** (an init script
  that leaves a lock the test changes alone), as it always was before.

### Explorer layouts (0.11.0, step D1b of the feedback plan)

- **Folders and Types are two layouts of the same tree,** chosen with the explorer's switch (the shell's), which is
  remembered as `vbadoc-explorer-layout`. Folders come first when any module has one; without folders there's only
  Types, and no switch, as in D1. Folders are drawn with a folder icon; No folder isn't a folder, so it has none.

### Agreed for later stages

- **In `--embed-source` mode** each module goes in its own `<script type="text/plain">` block, so diffs of the page
  are line by line.
- **In load-folder mode** the page stores a short hash of each source file and warns if the chosen folder doesn't match.
- **Dependency graph focus:** a Focus button showing only the selection's neighbourhood (following the
  direct/transitive toggle), a search box, and member-kind filters (e.g. hide variables or enum values).
