# VBA Export & Documentation Tool — Requirements

## Purpose

The tool helps document a VBA project so that someone can quickly understand what the project is, how it works, and how its components relate to each other.

The VBA editor built into Excel lacks modern IDE features. In particular, it is hard to find where a class or member is referenced. This tool fills that gap from outside Excel.

It is a .NET console application that:

- exports all VBA source from a macro-enabled Excel workbook into text files, and
- generates a single self-contained HTML page with two views of the project:
  - an interactive **dependency graph** (d3.js), and
  - a **class diagram** (d3.js).

The exported files and the HTML page are meant to be committed to a git repository that sits beside the workbook, so the VBA can be version-controlled and its structure understood at a glance.

## Constraints

- **Target framework:** .NET 10 console application.
- **Licensing:** only free, open-source libraries with MIT or equivalent permissive licences (MIT, BSD, Apache-2.0, ISC). MPL-2.0 is acceptable for unmodified packages.
- **No Excel dependency:** the tool must not use Excel COM interop or require Excel to be installed. It must not require the "Trust access to the VBA project object model" setting.
- **Input formats:** `.xlsm`, `.xlsb`, `.xlam` (zip-based) and `.xls`, `.xla` (compound file). The tool must work while the workbook is open in Excel (open with `FileShare.ReadWrite`).

## Proven extraction approach

A validation spike has already proven this approach works in the target environment. Build on it rather than choosing a different strategy.

1. For zip-based files, read `xl/vbaProject.bin` from the zip using `System.IO.Compression`. For `.xls`/`.xla`, open the file itself as a compound file and use the `_VBA_PROJECT_CUR` storage.
2. Open the compound file with **OpenMcdf 3.4.0** (MPL-2.0, NuGet). The spike used 2.4.1, but 2.x has two unpatched denial-of-service advisories (GHSA-jxpf-xq2m-q525, GHSA-5qwm-7pvp-w988), so the tool uses the patched 3.x API (`RootStorage`, `OpenStorage`, `OpenStream`).
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
- **Conditional compilation:** every branch of `#If … #ElseIf … #Else … #End If` is read, so the page documents every version of the code. A name declared again with the same kind (and the same `Get`, `Let` or `Set`) when either declaration is under `#If`, such as a variable or function declared once for early binding and once for late binding, is one member: its source runs from the `#If` to the `#End If` (or its `End` line), its signature shows each version's type (`As Scripting.Dictionary | Object`), and the dependencies of every version count.

## Output folder and files

- The output folder sits beside the workbook and is intended to be a git repository, for example `MyBook.xlsm` with a `MyBook/` folder next to it. The tool must not run `git` commands itself.
- The source files and the HTML page live together in that one folder.
- The page is named after the workbook and `-vba` (`MyBook-vba.html`), the same rule as pqdoc's outputs, so a page that travels on its own says what it documents. `#` and `%` become `_` in the file name only. `--output-name` (`-n`) chooses another name.
- Pages vbadoc wrote earlier under another name (recognised by their generator tag) are deleted from the folder after this run's page is written or kept, so an old page can't pass for the current one. Other `.html` files are left alone.
- **An existing page that this run would change** is handled by `--on-exists`, as in pqdoc: `prompt` (the default) asks *yes / no / all / quit* on standard error when the session is interactive, and behaves as `fail` when it isn't; `overwrite` replaces it (`--force` / `-y` is the same, and conflicts with any other `--on-exists`); `skip` keeps it; `fail` stops. The check comes before anything is written, so a refusal or quitting writes nothing and exits with code 4, with a message saying how to proceed. A page that would be written unchanged isn't asked about. The source files and `.gitattributes` follow the workbook and are never asked about.
- File extensions: `.bas` (standard module), `.cls` (class and document modules), `.frm` (forms, see below).
- **Encoding and line endings:** write source files in the project's code page (as the VBA editor's own Export does), with CRLF line endings, so they can be imported back into the VBA editor unchanged. The HTML page must decode them with the same code page; record the code page in the page's data and use `TextDecoder`.
- If no `.gitattributes` exists in the output folder, create one that stops git converting line endings or encoding on the source files. Never overwrite an existing one.
- Output must be **deterministic** so git diffs only show real changes: stable sort orders, no timestamps or run-specific values in generated files. The page may say when the workbook was last saved, from the workbook's own document properties, since that changes only when the workbook does.
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
- The page has a **tab for each view**: Dependency graph and Class diagram.
- If something is selected when switching tabs, it should stay selected in the other tab where it exists.
- Both views must stay readable on large projects. Provide zoom and pan, and a way to focus on part of the project (see each view).
- They must stay quick to use on a project the size of a large real one (70 modules, about 320 module and 1,540 member dependencies): hovering doesn't hold the page up, and going back to the whole graph after a focus, or to a level shown before, puts its layout back rather than working it out again, with dragged and pinned nodes where they were. Speed is never bought by leaving out what the page shows.
- **Focus on click** is one setting for the whole page, the Focus button at the start of each view's toolbar, on by default and remembered. While it's on, selecting anything (in a view, the explorer, search or the source panel) shows only that and what it's connected to, in every view; clearing the selection shows everything again. Turning it on focuses on the current selection too.
- **Ctrl+click** (Cmd+click on a Mac) does the other thing for that click: with focus on click on, it selects without focusing; with it off, it selects and focuses.
- **Esc** clears the selection, and with it any focus, in every view. An open menu, or a box with text in it, takes Esc first.
- The top bar sums up the project: its modules, members, and module and member dependencies, and when the workbook was last saved, from its own document properties (see Output), shown in the viewer's own time.

### Explorer

- A panel on the left lists every module, with its members under it in source order (an Enum's values under the Enum). Each member shows its kind, its role when it has one (such as Macro), and whether it's unreferenced.
- When any module has a `'@Folder("Parent.Child")` comment in its declarations (the Rubberduck convention; `'@Folder "Parent.Child"` also counts, and so does the annotation straight after a label ending in a colon, as in a header's `' Folder:<tabs>@Folder("Reports")`), modules are shown in those folders by default, nested at the dots and drawn as folders, with modules that have none in a No folder group. A switch shows them by kind instead, and the choice is remembered. Without folders they're shown by kind: standard modules, classes, interfaces, forms and document modules.
- A filter box narrows the tree to the names containing its text, with what holds them. Search stays for jumping straight to a name.
- **Expand all** and **Collapse all**, beside the filter box: Expand all opens every folder and group but not the modules, which would list every member; Collapse all closes everything.
- **The views' filter:** every row has a checkbox, apart from its name, and while anything is ticked the graph and the class diagram show only that. Ticking a folder or group ticks what's in it; a ticked module brings all its members (their boxes show ticked), and a ticked member brings its module, which stands for it at module level and in the class diagram. Ticking doesn't select and selecting doesn't tick; Esc clears the selection, not the filter. It's never silent: a bar in the explorer and a note in each view's toolbar say what's shown ("Filtered: 5 of 70 modules"), each with Clear, and unticking takes an item out. Remembered per workbook in the browser.
- **Hiding:** a module or member, hidden with the button beside the star on its explorer row or in the source panel's header, is taken out of the graph and the class diagram with its edges; a hidden module takes its members with it, and a hidden member leaves its module's box. Hiding wins over the filter. It's never silent: a hidden item stays in the explorer, struck through, and is listed in a Hidden group at its foot, to show again one at a time or all at once; each view's toolbar counts them, opening that group; and the source panel says when the selection is hidden, with Show it, or how many of the items it's linked to are, marking them in its dependencies. Remembered per workbook in the browser. Nothing is hidden to begin with.
- **Show…**, in the explorer, chooses what kinds of thing the tree and every view show, in place of each view's own menu: module kinds; member kinds and private members; all, unreferenced or referenced members; kinds of line (Uses and Implements everywhere, Events in the graph, Holds in a variable in the class diagram); and items with no connections. Each choice applies wherever it means something (member choices to the tree, the graph at member level and the class diagram's boxes), and what's ticked in the views' filter is shown even with no connections. The button counts the choices in effect, as does a note in each view's toolbar, which opens the menu, with Show everything. Show nothing clears every box, and each section of boxes has All and None, so a few things can be picked quickly. Remembered per browser; everything is shown to begin with.
- **Widely used:** modules used by at least a share of the other modules (20% to begin with, set in the Show… menu and remembered) are listed in a Widely used group near the foot of the explorer, most used first, each with its members that are widely used under it, and marked with a hub where they are in the explorer, with how many modules use them. Hide all hides the modules listed (with their members), and shows them again once they're all hidden. Nothing is hidden to begin with. The counts are worked out when the page is made: how many other modules use a module or member, through any kind of edge.
- **Predeclared classes:** a class with `VB_PredeclaredId = True`, used by its name like a standard module, is labelled "Predeclared class" in the explorer (Predeclared in the Types layout), the source panel and the graph's tooltip.
- **Focus on favourites,** turned on from the Favourites group, shows only the favourites and what they use in every view (as far as the graph's reach setting goes), as well as the focused selection and what it's connected to. A note in each view's toolbar says so, with Show all. Remembered.
- **Favourites:** any number of modules and members, starred with the star on an explorer row (which doesn't select it) or beside the name in the source panel, are listed at the top of the explorer in every layout, in the order they were starred, and starred in the graph. They're remembered per workbook in the browser; one that's no longer in the workbook is ignored.
- Choosing an item selects it in every view, and the explorer follows selections made elsewhere. It works from the keyboard as a tree: the arrows move and open or close, Enter chooses, and Space ticks.
- It can be hidden and resized, and both are remembered. It's open by default on a wide screen and closed on a narrow one, where it opens above the views. Its button is at the left end of the top bar, beside the title, as the source panel's is at the right end; both are icons, with their names in a tooltip.

### Light and dark mode

- The page follows the system preference by default, using the `prefers-color-scheme` media query.
- If the browser can't report a preference, the page defaults to **dark mode**.
- A switch on the page toggles between light and dark. The user's choice is remembered in `localStorage` (wrap access in `try/catch`, since it can fail on `file://`) and takes precedence over the system preference.
- Every part of the page must follow the theme: the page itself, the dependency graph, the class diagram, and the syntax highlighting.

### Source panel

Shared by both views.

- It opens when something is selected and closes when the selection is cleared, so the views get the room back. A lock keeps it open, and is remembered. Its button at the right end of the top bar still opens and closes it by hand; closing it by hand also unlocks it. When it opens over what was selected, the view moves that into sight; otherwise the views keep still.
- A button over the code copies the lines shown to the clipboard as plain text, without line numbers, and stays in view as the code scrolls. If the browser won't allow it, the code is selected for Ctrl+C instead.
- **Dependencies (Uses / Used by)** lists each item the selection uses, and each that uses it, once, at the selection's level, with tags for kinds of link other than plain use (implements, events) and the number of references when more than one. Opening an item lists every line where it's referenced: the line number and, when the source is there, the line's text. A reference on a statement continued over several lines is on the line that names what it refers to. Without the source, the line numbers still show.
- **Peek:** a click on one of those lines opens a preview under it: the line and 5 lines either side, highlighted, with the line marked, and never beyond the member that holds it. The selection, the views and the source below stay as they are. **Go to** in the preview, or Ctrl+click on the line, selects the member that holds the line, scrolls the source to it and marks it. **Show all of it** shows the whole procedure in the preview, which then scrolls in its own box. Any number of previews can be open; a click on its line closes one, and Esc closes the last opened before it clears anything else. Without the source, the preview says so, with Load folder, and Go to still works.
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
- **Settings** each explain themselves when hovered. While a node or module is dragged, the rest of the graph rearranges gently around it, without pulling in.
- A setting (in Settings, as it's seldom changed) switches highlighting between **direct** (one step in each direction) and **transitive** (the full chain in each direction). The default is direct. Focus shows the same neighbourhood.
- A second toggle chooses which direction is highlighted: **all** (dependencies and dependents, the default), **dependencies only** or **dependents only**.
  - When a module is selected at member level, the edges between its own members always stay highlighted; the toggle only applies to edges leaving or entering the module.
  - When a single member is selected, the toggle applies to every edge, including edges to other members of the same module.
- Clicking a node shows its source in the source panel. At module level that is the whole module; at member level it is just that member.
- **Path from the nearest favourite** highlights the shortest chain of dependencies between a favourite and the selection, in either direction, in the graph as drawn, and says which favourite and how far.

### Class diagram view

- **One box per module**, with a stereotype showing its kind: `<<module>>`, `<<class>>`, `<<interface>>`, `<<form>>` or `<<document>>`.
- **Box contents:**
  - members with visibility (`+` public, `-` private), parameters, and return or variable types;
  - properties shown once even when they have both `Get` and `Let`/`Set`;
  - events and constants distinguishable from procedures.
- **Relationships**, in UML notation:
  - `Implements` as realisation (dashed line, hollow triangle);
  - a module-level variable typed as another project class as association (solid line, open arrow), labelled with the variable name;
  - `WithEvents` variables as association, marked as events;
  - other usage as dependency (dashed line, open arrow).
- **Drawing:** the page draws the diagram itself with d3. Names and values are shown exactly as written, whatever characters they contain.
- **Layout:** worked out before drawing, so the same project and choices always give the same first picture. Users sit above what they use, and boxes never overlap.
- **Interaction:**
  - Clicking a box selects that module, and clicking a member line selects that member. Both show the source in the source panel.
  - Boxes can be dragged. Where they're dropped is remembered per workbook, until the user asks for the layout to be run again.
  - A Members switch in the toolbar lists each module's members in its box, or leaves the boxes to their names.
  - The legend's entries pick things out as the graph's do: clicking a kind of box or a relationship lights up the boxes and lines that match and fades the rest. Entries add to each other, and Clear, the background or Esc turns them off.
- **Large projects:**
  - Focus (the page's focus on click) shows only the selected module and the modules directly related to it; clearing the selection, or turning it off, shows everything.
  - Large diagrams must remain correctly drawn, with zoom and pan.
- **Testing:** verify the drawn diagram in a headless browser.

### Calls view

- **A tree of the procedures each procedure calls, in the order of its code,** opened one level at a time, so its size never matters. It starts from the entry points (macros, event handlers and worksheet functions, in groups) and the favourite procedures.
- **Each call** shows what's called, its kind, and the line it's on, which selects the procedure making the call and shows the line in the source panel. Calls are to procedures, properties (each side of one), events (RaiseEvent) and Declares; uses of variables and constants stay in Dependencies.
- **Through an interface or an event:** a call to an interface member opens to its implementations, and RaiseEvent to its WithEvents handlers.
- **The logic around each call:** If, ElseIf and Else branches (a single-line If's too), Select Case and each Case, For, Do and While loops, With, and On Error handlers are rows holding the calls made inside them, in order, open to begin with, with a link to their line. A block with no calls isn't shown, and its text is cut to about 60 characters. Turned round, each caller says which block its call is in.
- **Open all below:** a button on a procedure's or group's row (shown on hover), or `*` on the keyboard, opens everything below it, as far as recursion allows, so a whole entry point can be read or drawn at once. It stops after 2,000 calls and says so.
- **Sequence diagram:** a Tree / Sequence switch draws what's open in the tree as a sequence diagram for each open starting procedure: modules as lifelines in the order they take part, calls as messages in the order of the code (dashed to an implementation or handler), late-bound calls to a lifeline of their own, and the blocks around calls as frames. It shares what's open with the tree: a message's ▸ opens its calls (Shift+click opens everything below), a frame's label closes or opens it, and either narrows both. Lanes widen to fit their messages' names, and frames their whole guard, so nothing is cut short. A click on a message selects as in the tree, and Ctrl+click starts from it. The choice is remembered; Called by has no diagram. Drawn by the page with d3.
- **Recursion:** a procedure already open higher up the same branch is marked ↻ and doesn't open again.
- **Left out and counted:** calls to items the views don't show (hidden, filtered out, or left out by Show…) aren't listed, and a row counts them. A late-bound call is a leaf showing its expression. A note says the tree can't see `Application.Run`, `OnAction` or other calls made by name.
- **Calls / Called by** turns the tree round, to show what leads to a procedure; an implementation or handler then opens to what calls the interface member or raises the event. The choice is remembered.
- **Clicks:** a click on a name selects it without changing the tree, so the path opened stays (the exception to Focus on click in the guidelines); Ctrl+click starts the tree from that procedure, as a focused selection made in another view does. Entry points, or clearing the selection, goes back to the entry points. The tree works from the keyboard as the explorer's does.

### Lock view

- **A Lock view button** at the start of every view's toolbar, beside Focus, keeps what the graph and the class diagram show exactly as it is: the items shown, where they are, the zoom and the graph's level. There's one lock for the page, and it's remembered per workbook.
- **While it's on, a click on an item or on empty space changes only the selection and the source panel.** The selected item is outlined; nothing else is lit or dimmed, and nothing is added, removed, moved, fitted or centred. Dragging a node or box still moves it, and only it. Focus and Path are disabled.
- **Locking ticks everything shown in the explorer,** so the explorer and each toolbar's note ("Locked: 5 of 70 modules") say what's kept, and unticking an item drops it or ticking another adds it, with the rest staying where they are. At the graph's member level, a module whose own node is shown while its members aren't is kept on its own, its box half ticked, since a tick on a module brings every member; a click on that box drops it.
- **Unlocking,** with the button again or Unlock in the note, puts the ticks back as they were before locking, and Focus on click works again.
- **Lines:** a locked graph draws every line between the items it shows. A focused graph draws only the lines on the way to the selection, so locking one can add lines between the items that were already there.

### Saved views

- **A view** is everything the page shows, saved under a name, with a description: the tab; the selection and whether it's focused; Focus on click and Focus on favourites; the views' filter, whether the view is locked (with the ticks to go back to), the hidden items, the Show… choices and the widely used share; whether the explorer and the source panel are shown, their sizes, the explorer's layout and the source panel's lock; whether the source panel shows a member in its whole module and its Dependencies; the graph's level, Dependencies / Dependents, Path, legend picks and settings (apart from the two about dragging) and, at each level where anything was moved, where its nodes are and which are pinned, and the same for a focused graph; the class diagram's Members switch, legend picks and moved boxes, focused or not; and the Calls tab's starting point, open rows, Calls or Called by, and tree or sequence diagram. Favourites and the theme stay the person's own.
- **The Views menu** in the top bar saves the page as a new view, or saves changes to the view last opened once the page has changed since (the view is marked "changed"), opens a view, saves over any other view without opening it, and renames, describes or deletes one. Save over, Save changes, Delete and Import have Undo. A new view given a name that's taken offers to save over that view. Names are unique, whatever their case. The button shows the name of the view last opened or saved. Opening a view sets everything at once.
- **Views are kept per workbook in the browser.** **Export** downloads them as `vbadoc-views.json`, and **Import** reads such a file back with a file picker, adding its views and replacing any of the same name. Both work offline, in a page opened from disk. The file is JSON with a format version and the workbook's name, using the page's names, written with sorted keys and 2-space indentation so its diffs are small. A file for another tool, or from a newer vbadoc, is refused, saying why. A file from another workbook, or that mentions items this one doesn't have, says so before anything is imported, with the views it would replace, and is imported only if the person goes on.
- **A view that mentions items the workbook no longer has still opens:** they're left out, and listed in a note across the page and on the browser's console; it's never an error. Nodes it has no position for are placed beside what they're linked to.

### Export tab

- **The Export tab** chooses modules and classes to take out of the workbook, for `vbadoc import`. It lists every module, in the explorer's layout, whatever the filter, hiding and Show… choices are doing. Ticking a folder picks all that's in it, and it can start from what's ticked in the explorer. The choice is remembered per workbook, and is apart from the explorer's filter.
- **Only whole standard modules and classes can be picked.** Document modules and forms can't, and say why.
- **As the choice changes, it shows what it comes to:** what was chosen, what comes along because something chosen uses it (following Uses, Implements and Events), an interface's implementations, offered because nothing uses them, what to check in the target, and the library references.
- **To check in the target** lists what doesn't come along, and never blocks anything: a sheet's code name, a procedure of the source's own on ThisWorkbook, a form, a procedure run by name (`Application.Run`, `CallByName`, `OnAction`), and late-bound calls. Plain `ThisWorkbook` isn't listed, as it simply means the target's own.
- **Library references** are listed to untick, apart from the two every project has.
- **The manifest,** `vbadoc-export.json`, is downloaded from the tab and can be loaded back into it. It holds what was picked, the references to leave out and the workbook's name. A manifest for another tool, or from a newer vbadoc, is refused, saying why.

## Importing

`vbadoc import <source>` takes chosen modules and classes out of a workbook.

- **What to take** comes from `--manifest <file>` or `--items A,B`, one or the other. It says what that comes to, as the Export tab does. A module the workbook doesn't have, or that can't be taken, stops it with exit code 3; a manifest made from another workbook is only mentioned.
- **`--bundle <folder>`** writes the modules as the VBA editor imports them, with a `README.md` on importing them by hand: what's there and why, the references to tick, and what to check. A bundle goes only into an empty folder or an earlier bundle, and a folder of other files stops it with exit code 4. Replacing an earlier bundle that would change follows `--on-exists` and `--force`. `--no-references` leaves the references out of it.
- **`--new <file>`** writes a new workbook holding the modules, with a sheet. The file has to be an `.xlsm`, or an `.xlam`, which makes it an add-in. It's in the source's code page.
- **`--into <file>`** takes an `.xlsx`, `.xlsm`, `.xlsb` or `.xlam` (an add-in) as the target and writes a copy beside it, `<target>-imported`, with the target's extension, or `.xlsm` for an `.xlsx`. An encrypted workbook, an `.xls` or `.xla`, and anything else stop it with exit code 3; an `.xla` is told to be saved as an `.xlam`. Only one of `--new`, `--into` and `--bundle` can be given. Without any, nothing is written.
- **An add-in stays an add-in,** one with no VBA yet included, as only its content type makes it one. A locked project stays locked, with the same password.
- **`--on-exists` and `--force` apply to the file written.** `--new` can't name the source, and neither can a target changed in place: exit code 2.
- **Neither workbook is changed,** byte for byte, unless `--in-place` is given. Everything in the target stays as it was but what has to change: its other modules and references, protection, and a sensitivity label that's only metadata.
- **A module the target has word for word** (ignoring what ends it) is left alone without asking, and counted. When every module is and no reference is new, nothing is written.
- **A name the target has for something else is a clash,** ignoring case. Each is asked about before anything is written, saying what's at stake: what else in the target uses it, and what's coming that would use the target's version. The answers are Skip, Overwrite, Skip All, Overwrite All and Quit, which stops with nothing written and exit code 4. A clash can be with a dependency that came along, and a module of another kind can be overwritten.
- **A sheet's or the workbook's module, or a UserForm, can't be overwritten,** in a project or by a code name in a workbook without one. Overwrite isn't offered, Overwrite All doesn't answer for it, and `--on-clash overwrite` stops with exit code 4.
- **`--on-clash overwrite|skip`** answers for every clash. Without it and with nobody to ask, a clash stops the run with exit code 4. `--force` doesn't answer a clash.
- **`--in-place`** changes the target itself. It says what will change and asks first; a no stops with exit code 4. `--force` answers yes. With nobody to ask and no `--force`, it stops with exit code 4. The target is replaced in one step once the result is finished and read back, keeping its permissions, so a failure leaves it as it was. One that can't be had to the tool alone, as when Excel has it open, stops with exit code 5 before anything is asked; for an add-in, it says to untick it in the Add-ins dialog or close Excel. An `.xlsx` is refused with exit code 4, as it can't hold modules. `--on-exists` can't be given with it.
- **Library references** are added, apart from the two every project has, any left out by the manifest or `--no-references`, and any the target has, known by the library's GUID. One that can't be found on this machine (in the registry by GUID alone, or another project's file) is left out and listed, as it would stop the whole project compiling. So is one whose name the target gives to a different library. Off Windows nothing can be looked up, so they're added and that's said.
- **A signed target** stops with exit code 4, unless `--drop-signature` says to remove the signature, which the change would break.
- **What's written is read back** before anything is replaced or created: every module has to be there, word for word.
- **Source in another code page** is converted to the target's. A character it can't hold stops the run with exit code 3, rather than turning into a question mark.

## Out of scope

