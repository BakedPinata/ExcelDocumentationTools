# VBA Export & Documentation Tool — Requirements

## Purpose

The tool helps document a VBA project so that someone can quickly understand what the project is, how it works, and how its components relate to each other.

The VBA editor built into Excel lacks modern IDE features. In particular, it is hard to find where a class or member is referenced. This tool fills that gap from outside Excel.

It is a .NET console application that:

- exports all VBA source from a macro-enabled Excel workbook into text files, and
- generates a single self-contained HTML page with two views of the project:
  - an interactive **dependency graph** (d3.js), and
  - a **class diagram** (Mermaid).

The exported files and the HTML page are meant to be committed to a git repository that sits beside the workbook, so the VBA can be version-controlled and its structure understood at a glance.

## Constraints

- **Target framework:** .NET 9 console application.
- **Licensing:** only free, open-source libraries with MIT or equivalent permissive licences (MIT, BSD, Apache-2.0, ISC). MPL-2.0 is acceptable for unmodified packages.
- **No Excel dependency:** the tool must not use Excel COM interop or require Excel to be installed. It must not require the "Trust access to the VBA project object model" setting.
- **Input formats:** `.xlsm`, `.xlsb`, `.xlam` (zip-based) and `.xls`, `.xla` (compound file). The tool must work while the workbook is open in Excel (open with `FileShare.ReadWrite`).

## Proven extraction approach

A validation spike has already proven this approach works in the target environment. Build on it rather than choosing a different strategy.

1. For zip-based files, read `xl/vbaProject.bin` from the zip using `System.IO.Compression`. For `.xls`/`.xla`, open the file itself as a compound file and use the `_VBA_PROJECT_CUR` storage.
2. Open the compound file with **OpenMcdf 3.3.0** (MPL-2.0, NuGet). The spike used 2.4.1, but 2.x has two unpatched denial-of-service advisories (GHSA-jxpf-xq2m-q525, GHSA-5qwm-7pvp-w988), so the tool uses the patched 3.x API (`RootStorage`, `OpenStorage`, `OpenStream`).
3. Decompress the `VBA/dir` stream using the MS-OVBA compression algorithm (section 2.4.1). Implement this in the tool; it is about 50 lines.
4. Parse the `dir` records to get the code page (`0x0003`) and, for each module: name (`0x0019`, Unicode `0x0047`), stream name (`0x001A`, Unicode `0x0032`), text offset (`0x0031`), and whether it is a standard module (`0x0021`). Records are `id (2 bytes) + size (4 bytes) + data`, except `PROJECTVERSION` (`0x0009`), whose payload is 6 bytes even though its size field says 4.
5. Use the `PROJECT` stream to classify the other modules: `Module=` (standard), `Class=` (class), `BaseClass=` (form), `Document=` (ThisWorkbook and sheets).
6. Decompress each module stream from its text offset and decode it with the project's code page (register `CodePagesEncodingProvider`).
7. If the file is encrypted (a compound file containing an `EncryptedPackage` stream), stop with a clear message.

## Source analysis

A second spike (a sequence-diagram generator, since abandoned) proved that a lightweight parser is enough to build a useful model of the project. A full VBA grammar is not required. The parser must handle at least:

- **Logical lines:** line continuations (` _`), `:` statement separators (but not `:=` named arguments or `:` inside strings), comments (`'` and `Rem`), and `Attribute` lines.
- **Declarations:** procedures (`Sub`, `Function`, `Property Get/Let/Set`) with visibility, parameters and return types, plus `Const`, `Enum`, `Type`, module-level variables (including `WithEvents`), `Event`, `Declare` and `Implements`.
- **Type-aware reference resolution:** resolve `obj.Member` using the declared types of locals, parameters, module variables and public globals, as well as `Me`, module-qualified names and `With` blocks.
- **Late-bound references** (on `Object`, `Variant` or untyped variables) can't be resolved statically. Count and report them rather than guessing.
- **Source line ranges** for every member, so the page can show member-level source (see Source panel).

## Output folder and files

- The output folder sits beside the workbook and is intended to be a git repository, for example `MyBook.xlsm` with a `MyBook/` folder next to it. The tool must not run `git` commands itself.
- The source files and the HTML page live together in that one folder.
- File extensions: `.bas` (standard module), `.cls` (class and document modules), `.frm` (forms, see below).
- **Encoding and line endings:** write source files in the project's code page (as the VBA editor's own Export does), with CRLF line endings, so they can be imported back into the VBA editor unchanged. The HTML page must decode them with the same code page; record the code page in the page's data and use `TextDecoder`.
- If no `.gitattributes` exists in the output folder, create one that stops git converting line endings or encoding on the source files. Never overwrite an existing one.
- Output must be **deterministic** so git diffs only show real changes: stable sort orders, no timestamps or run-specific values in generated files.
- Re-running the tool must update the folder to match the workbook, including removing files for modules that no longer exist. It must only delete files it generated; other files in the repo (README, `.gitattributes`, etc.) must be left alone.

## Forms

Forms (UserForms) are in scope, but no sample workbook with forms has been available for testing yet. Build this part defensively.

- Export each form's code to a `.frm` file.
- Read the form's designer storage (MS-OFORMS format) to list its controls with their name and type, and caption where available. Use this to link event handlers such as `cmdOK_Click` to the control `cmdOK`.
- A byte-exact, VBA-editor-compatible `.frx` file is not required.
- If the designer data can't be parsed, still export the code, and report a warning instead of failing.

## HTML page

### General

- The page must be a single HTML file with all JavaScript and CSS inline. No CDN or other network requests.
- Third-party scripts must be vendored into the tool, for example as embedded resources, and written inline into the page with their licence headers kept:
  - d3.js (ISC licence)
  - Mermaid (MIT licence)
- The page has a **tab for each view**: Dependency graph and Class diagram.
- If something is selected when switching tabs, it should stay selected in the other tab where it exists.
- Both views must stay readable on large projects. Provide zoom and pan, and a way to focus on part of the project (see each view).

### Light and dark mode

- The page follows the system preference by default, using the `prefers-color-scheme` media query.
- If the browser can't report a preference, the page defaults to **dark mode**.
- A switch on the page toggles between light and dark. The user's choice is remembered in `localStorage` (wrap access in `try/catch`, since it can fail on `file://`) and takes precedence over the system preference.
- Every part of the page must follow the theme: the page itself, the d3 graph, the Mermaid diagram (re-render with Mermaid's dark or default theme), and the syntax highlighting.

### Source panel

Shared by both views.

- Selecting a module, class, interface or form shows its whole source.
- Selecting a member shows **only that member's source**: its procedure, or the declaration lines of a `Const`, `Enum`, `Type`, variable, `Event` or `Declare`. Offer a way to see it in the context of the whole module.
- The source is shown with **VBA syntax highlighting**:
  - at minimum keywords, strings, comments, numbers, and procedure and type names;
  - in both light and dark themes;
  - a small built-in highlighter is fine if no suitably licensed library supports VBA.
- **How source reaches the page:** browsers block `fetch()` of local files when a page is opened from disk (`file://`), and this has been confirmed in the target environment. The page must therefore support two modes, chosen when the tool runs:
  1. **Load folder (default):** the source is not embedded in the page. The page shows a "Load folder" button that uses `<input type="file" webkitdirectory>`. The user picks the output folder once per session, and the page then reads the source files itself. This has been tested and works from `file://`. Until a folder is loaded, both views still work fully; only the source panel shows a prompt to load the folder.
  2. **Embedded (`--embed-source` flag):** the source of every module is embedded in the page, so it works without loading a folder. The page is then larger, and code changes appear twice in git diffs.

### Dependency graph view (d3.js)

**Two levels of abstraction**, clearly distinct, with a way to switch between them:

1. **Module level:** one node per module (file). An edge from A to B means code in A uses something defined in B.
2. **Member level:** one node per member: `Sub`, `Function`, `Property Get/Let/Set`, `Const`, `Enum` and its values, `Type`, module-level variables, `Event`, and `Declare` statements. Each member's owning module must be visually obvious, for example by colour or grouping.

**Edge types.** There are three, each visually distinct (line style, colour or arrowhead) and explained in a legend:

1. **Uses:** ordinary references and calls.
2. **Implements:**
   - A class with `Implements IFoo` gets an implements edge to `IFoo`.
   - A class that other classes implement must be identifiable as an interface.
   - At member level, a procedure such as `IFoo_DoThing` must be linked to the interface member `DoThing` it implements.
3. **Events:**
   - `RaiseEvent` links the raising member to its `Event` declaration.
   - A `WithEvents` variable links its module to the class that raises the events.
   - Handlers such as `m_Thing_Changed` link to the event they handle.
   - Form and control handlers such as `cmdOK_Click` link to their control (see Forms).

**Interaction:**

- Clicking a node highlights it together with the nodes it depends on and the nodes that depend on it, plus the edges between them. Everything else is greyed out. Clicking the background clears the selection.
- A toggle switches highlighting between **direct** (one step in each direction) and **transitive** (the full chain in each direction). The default is direct.
- A second toggle chooses which direction is highlighted: **all** (dependencies and dependents, the default), **dependencies only** or **dependents only**.
  - When a module is selected at member level, the edges between its own members always stay highlighted; the toggle only applies to edges leaving or entering the module.
  - When a single member is selected, the toggle applies to every edge, including edges to other members of the same module.
- Clicking a node shows its source in the source panel. At module level that is the whole module; at member level it is just that member.

### Class diagram view (Mermaid)

- **One box per module**, with a stereotype showing its kind: `<<module>>`, `<<class>>`, `<<interface>>`, `<<form>>` or `<<document>>`.
- **Box contents:**
  - members with visibility (`+` public, `-` private), parameters, and return or variable types;
  - properties shown once even when they have both `Get` and `Let`/`Set`;
  - events and constants distinguishable from procedures.
- **Relationships:**
  - `Implements` as realisation (`<|..`);
  - a module-level variable typed as another project class as association (`-->`), labelled with the variable name;
  - `WithEvents` variables as association, marked as events;
  - other usage as dependency (`..>`).
- **Interaction:**
  - Clicking a box selects that module, and clicking a member line selects that member. Both show the source in the source panel.
  - Prefer attaching click handlers to the rendered SVG over Mermaid's `click` directive, so Mermaid's `securityLevel` can stay `strict`.
- **Large projects:**
  - Provide a focus mode that shows only the selected module and the modules directly related to it, plus a way to show everything.
  - Large diagrams must remain correctly rendered and scrollable. The abandoned sequence-diagram spike had lifelines stopping halfway down large Mermaid diagrams.
- **Testing:** verify that the generated Mermaid parses and renders, for example in a headless browser.

## Out of scope

- **Sequence diagrams:** these were prototyped and rejected. Diagrams for real entry points were too large to read, and Mermaid rendered large ones incorrectly.
