# Design decisions

Decisions made while building the tool that aren't spelled out in [Requirements.md](../Requirements.md),
or that change it. Add to this file whenever a new decision is agreed.

## Platform and libraries

- **.NET 9**, not .NET 10. The machine the tool runs on can't have .NET 10 installed yet.
  .NET 9 support ends on 2026-11-10, so move to .NET 10 when possible (a one-line change per project).
- **OpenMcdf 3.3.0**, not 2.4.1 as the spike used. 2.4.1 has two unpatched denial-of-service advisories
  (GHSA-jxpf-xq2m-q525, GHSA-5qwm-7pvp-w988), and only 3.1.4 and later are fixed. Requirements.md is updated to match.
- **Mermaid 11.17.2**, not 12.x. 12.0 was a new major version only weeks old at the time; 11.17.2 is the mature end of 11.x.
- **d3 7.9.0**, the latest.
- Vendored scripts are stored **byte-for-byte unchanged** with their licence files (see
  `src/VbaDocumenter.Core/Resources/Vendor/README.md`). The page writer adds each licence as a comment above its script,
  and must refuse a script containing `<script` or `</script`, which would break inlining.
- **Browser tests** use Playwright with its own downloaded Chromium.
- The page's JavaScript and CSS are **hand-written plain files with no build step** (no bundler, no TypeScript).

## Export

- The default output folder is **`VBAProject` next to the workbook**; `--out <folder>` overrides it.
  Two workbooks in one directory would share that folder, so one needs `--out`.
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
- **The created `.gitattributes`** marks `*.bas`, `*.cls`, `*.frm` and `index.html` as `-text diff` (no conversion,
  textual diffs) and `*.frx` as `binary`. An existing one is never overwritten.
- **The HTML page is `index.html`.**

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
- **Type-declaration characters** (`Dim s$`, `Function Name$()`) are mapped to their types. This matters because Mermaid
  reads a trailing `$` as "static".
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
  so stage 3 must give members ids that stay unique in that case.
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
  (about 150 MB) on first run. `dotnet test tests/VbaDocumenter.Tests` runs only the unit tests, with no download.

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
  - "Keep dragged nodes where dropped" pins them (double-click releases); off by default.
- **Edges are single curves.** Bundling edges between modules was tried and rejected as the default; it remains an
  option, off by default.
- **A settings panel** (Settings button in the graph toolbar) controls names, text and node size, edge opacity and
  curves, bundling, module outlines, dragging behaviour and the layout forces. Changes apply straight away (layout
  changes animate). Settings are remembered per browser, with a reset button. The Show… and Settings drop-downs close
  on a click outside them or Escape.
- **Focus re-lays out only the highlighted neighbourhood** and follows the selection while on. "Show all" leaves it.
- **Remembered per browser:** level, Direct/Transitive and the settings panel. Member filters aren't remembered.
- **Edge thickness grows slightly with the reference count.** Hovering an edge shows its count; hovering a node shows
  its kind and how many things it uses and is used by.

### Built in stage 5 (class diagram)

- **Mermaid class IDs are generated** (`M1`, `M2`, … in project order), with the real name as the label, so no name can
  break parsing. `maxTextSize` and `maxEdges` are raised for large projects. `securityLevel` stays `'strict'`; click
  handlers are attached to the rendered SVG.
- **The Mermaid text is generated in the page** (`classes.js`) from structured data, so focus mode and the Show…
  options just generate a smaller diagram. The C# side (`ClassDiagram`, unit-tested) works out each member's line and
  the relationships; the page data carries them unescaped (`members[].class`, `classEdges`).
- **Escaping:** checked against Mermaid 11.17.2. Member text goes through Mermaid's parser, markdown and entity
  decoding, so `# { } ~ < > & $ * _ [ ] ( ) `` ` are written as entity codes (`#123;`), which Mermaid displays as the
  character. Only a method's own brackets stay real: Mermaid treats any line containing `)` as a method and crashes on a
  `)` without a matching `(`. Class bodies are used rather than `Class : member` lines, which are much stricter.
- **Box contents:**
  - methods `+Name(params) Type`; attributes `-m_Id : String`; Friend is `~`;
  - events, constants, enums, types and declares are prefixed with `Event`, `Const`, `Enum`, `Type`, `Declare`;
  - a property is shown once, on its first accessor, as `+Property Status : String «Get/Let»`; an indexed property
    (with parameters besides the value) is drawn as a method so its parameters show;
  - enum values are left out (the Enum is shown); constant values are cut to 40 characters;
  - a declaration repeated under `#If`/`#Else` that gives the same line is shown once.
- **Relationships:** Implements is realisation. A module-level variable typed as another project module (other than a
  standard module) is an association labelled with the variable name, prefixed `WithEvents` for event sources. Every
  other module edge is a dependency, except where an association already links the same two modules (which also
  covers the WithEvents Events edge).
- **Clicks:** each box is mapped back to its module from the generated id, and each member line by its position, since
  Mermaid draws attributes then methods, each in order. Clicking a property's line selects its first accessor, and
  the line stays highlighted for the other accessors.
- **Large projects:** Mermaid's layout slows sharply with many edges (600 dependencies took about a minute in headless
  Chromium). So unless the user has chosen in Show…, the full diagram leaves dependencies out when there are more
  than 150, says so in the toolbar, and Focus (a small neighbourhood) still shows them. The browser tests' generated
  project is now 300 modules for both views; its full class diagram draws in about 20 s. Known limit: boxes with no
  relationships are laid out by Mermaid in one long row.
- **Looks:** Mermaid's `base` theme coloured from the page's CSS variables, redrawn on theme change; box outlines take
  the graph's module-group colours. The diagram is an SVG in a scrolling box: zoom buttons, Fit, and Ctrl+wheel. It
  first fits the width, but not below 40%.
- **Remembered per browser:** the Show… options (members, private members, dependencies). Focus follows the selection.
- **The toolbar drop-downs' outside-click and Escape handling moved to `page.js`** and applies to every
  `<details class="dropdown">`.

### Agreed for later stages

- **In `--embed-source` mode** each module goes in its own `<script type="text/plain">` block, so diffs of the page
  are line by line.
- **In load-folder mode** the page stores a short hash of each source file and warns if the chosen folder doesn't match.
- **Dependency graph focus:** a Focus button showing only the selection's neighbourhood (following the
  direct/transitive toggle), a search box, and member-kind filters (e.g. hide variables or enum values).
