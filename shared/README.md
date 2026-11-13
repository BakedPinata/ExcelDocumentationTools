# CodeDocumenter.Shared

The code both tools need, kept once. vbadoc's and pqdoc's Core libraries use it; it never uses them, and they never use
each other. It isn't a product: it has no command line, no version of its own, and changes to it change both tools'
build stamps. See "Structure" in [docs/GUIDELINES.md](../docs/GUIDELINES.md) for what belongs here.

## What's in it

| Folder | What | Used by |
|---|---|---|
| (root) | `ExitCodes`, the codes every tool returns; `ToolVersion`, the build stamp read from a tool's own assembly | both |
| `Workbooks/` | `WorkbookFile`: reading a workbook while Excel has it open, telling zip-based from compound files, spotting encryption; `ExtractionException`, exit code 3; `OpcPackage`: a workbook's parts in memory, to change some and keep the rest; `BlankWorkbook`: a new workbook with one sheet, and the content types of a macro-enabled workbook and an add-in; `TargetFile`: what can be written into, from each tool's list, and why anything else can't | both readers; writing workbooks |
| `Files/` | `AtomicFileWriter`: write to a temporary file, then move it into place, or replace a file keeping its permissions; `SourceFolderWriter`: keep a folder of source files in step with a workbook; `OverwriteResolver` and `OnExistsBehaviour`: what to do when an output already exists, asking through `IUserInteraction` | vbadoc's module files and page; pqdoc's `.m` files and outputs |
| `CommandLine/` | `OverwriteOptions`: `--on-exists` and `--force` (`-y`), checked and bound the same way in both tools; `ImportOptions`: the `import` command; `ImportTargetOptions`: `--new`, `--into`, `--in-place` and `--on-clash`; `UnknownOptions`: words that look like options but aren't the command's, which System.CommandLine would take for arguments; `ConsoleUserInteraction`: the prompts, on standard error | both command lines |
| `Importing/` | `ImportPlan`: what a choice comes to; `ExportManifest`: the file the Export tab writes; `BundleFolder`: a folder of chosen files with a README; `ImportClash` and `ClashResolver`: what's in the target already, and what to do about it; `ImportRun`: the steps of `import` both tools share; `ImportableItem`: something a source has to take, for the desktop app's list | both `import` commands |
| `Pages/` | `ViewsFile`: a saved views file in an output folder, read for building into the page; `ShellPage`: a page built on the shell, from the parts a tool brings; `PageBuilder`: filling a template's placeholders, inlining scripts after d3 and the shell's, escaping text and JSON, the favicon; `EmbeddedResources`; `VendoredScripts` | both pages |
| `Resources/Shell/` | The page shell: `shell.html` (the template), `shell.css` (the frame and shared colour tokens) and `shell.js` (theme, settings, tabs, the explorer, the side panel, their splitters, Load folder, source view; its interface is at the top) | both pages |
| `Resources/Vendor/` | d3 and its licence, byte-for-byte as published (see its README) | both pages |

## Commands

Run these from the repository root, so both tools build and test against any change here.

```bash
dotnet build CodeDocumentationTools.sln -warnaserror
dotnet test --project shared/tests/CodeDocumenter.Shared.Tests   # this library's own tests
dotnet test --solution CodeDocumentationTools.sln                # everything, since both tools depend on it
```

## Decisions

- **Made in step C1 of the feedback plan** from the copies the two tools had grown. Moving them changed nothing the
  tools write: every fixture's output was compared byte for byte before and after, apart from the build stamp and
  pqdoc's generation time.
- **Each tool keeps its own messages.** The shared code reads, detects and writes; what a tool tells the user about a
  workbook (no VBA project, no queries, an older `.xls`) stays in that tool. A few messages that were word for word the
  same, such as "Can't read …", now come from here.
- **`ToolVersion.Of(assembly)`** takes the tool's name from the assembly's `Product`, which each tool's
  `Directory.Build.props` sets, so it reads the version of whichever tool asks rather than this library's.
- **`SourceFolderWriter` is given finished files.** Each tool works out its file names and bytes, and reports a clash
  in its own words, before anything on disk changes; this then deletes, writes and adds `.gitattributes`. It refuses
  names that differ only by case, which the tools check first anyway.
- **One way of escaping text in a page** (`&`, `<`, `>` and `"`), vbadoc's. pqdoc used `WebUtility.HtmlEncode`, which
  also wrote `'` and non-ASCII characters as numeric references. Browsers show both the same, so only a page's bytes
  change, and only for a title with such characters.
- **One copy of d3**, here. Each tool had its own, and they were identical.

### The page shell (step C2)

- **The shell owns the frame, the tools own what's in it.** The shell has the top bar, tabs, theme, stored settings,
  side panel and splitter, Load folder and the source view, which were line for line the same in both pages. Each tool
  keeps its views, its search box (vbadoc jumps to a match from a list; pqdoc lights up every match), what its side
  panel shows, and vbadoc its trail.
- **Nothing visible changed.** Screenshots of both pages, in both themes, wide and narrow, were compared pixel by pixel
  before and after, with the build stamp made the same; they differed only where the same page differs from itself
  between runs. Where the two pages' rules differed (the side panel's default size, the search box's width, pqdoc's
  placeholder colour), each tool keeps its own.
- **The side panel's ids are the shell's now:** `side-panel`, `side-panel-header`, `side-panel-body`,
  `side-panel-toggle`, `side-panel-close` and `side-panel-splitter`, in place of vbadoc's `source-…` and pqdoc's
  `details-…`. The browser tests changed only there. The panel keeps its name in each tool (Source, Details), and its
  stored size and visibility keep their keys (`vbadoc-source-size`, `pqdoc-details-hidden`), so nobody loses them.
- **Two tooltips are new in vbadoc**, because the template is shared: its heading's (the workbook's name) and Load
  folder's.
- **Marked lines are the shell's `marked` class**, in place of vbadoc's `in-member`, so the source view doesn't name a
  vbadoc idea. The colour token for them (`--line-highlight`) moved with it.
- **The tool starts the shell, then says where its source comes from** (`shell.source.init`), because only the tool
  can read its data: vbadoc decodes with the project's code page, pqdoc by byte order mark.
- **No colour changed.** The shared tokens were identical in both pages, so none needed validating again.

### The explorer (step D1)

- **The shell draws the tree; each tool says what's in it** (`shell.explorer.setLayouts(layouts, select)` since
  D1b), as plain nodes with a label, an id when choosable, a detail, a badge and children. The shell owns expanding,
  the filter, the keyboard (the ARIA tree pattern) and following the selection (`reveal`).
- **Both panels resize the same way:** the side panel's code became `resizablePanel`, which the explorer uses too, on
  the other side. Each panel's size is its share of the layout, remembered per tool (`<tool>-explorer-size` and
  `-hidden`), and the explorer is open by default only on a wide screen.
- **The filter matches names only** and keeps what holds a match, opening it while filtering; clearing it puts back
  what was open.

### View controls and the summary (step D1c)

- **Focus on click is the shell's** (`shell.focusOnClick()` / `setFocusOnClick(on)`, remembered as
  `<tool>-focus-on-click`, on unless turned off), so every view in both tools follows one setting. The shell keeps
  whether the selection is focused (`shell.focused()`, `onFocusChange`): `selectionChanged(id, other)` focuses a new
  selection when the setting is on, unless `other` (a Ctrl+click or Cmd+click) says to do the other thing. The tools
  call it on every selection, even of what's already selected, and it returns whether the selection changed; if it
  didn't, only the focus may have, and `onFocusChange` says so. The explorer passes Ctrl+click through.
- **Esc clears the selection in the shell,** through the select function given to `explorer.setLayouts`. It listens in
  the capture phase, so it can leave Esc to an open `details.dropdown` menu or a box with text in it, before their own
  handlers close or empty them.
- **The top bar's summary is a shell slot** (`ShellPage.Summary` and `WorkbookSaved`): the tool's counts, then
  "Workbook saved" and a `<time>` written in UTC (so the page is the same wherever it's made), which `shell.js` shows in
  the viewer's own time. `WorkbookFile.LastSaved(bytes)` reads it from the workbook's own document properties:
  `dcterms:modified` in `docProps/core.xml`, or PIDSI_LASTSAVE_DTM in an .xls file's summary information ([MS-OLEPS]).
  It fails safe, to null. The summary's style moved from pqdoc's stylesheet into `shell.css`, and so did a view
  toolbar's pressed button.

### Panel controls (step D1a)

- **The explorer's button is at the left end of the top bar, beside the title, and the side panel's at the right
  end,** after the version, as icons (three bars and `</>`) with their names as `aria-label`s and in a tooltip. On a
  narrow screen the explorer's stays beside the title.
- **The tool tells the shell of every change of selection** (`shell.selectionChanged(id)`, which replaced
  `shell.explorer.reveal`), before drawing it, so the side panel is open when it's drawn. The explorer marks the item
  and the side panel opens or closes with it, unless it's locked open. The lock is remembered
  (`<tool>-<panel>-locked`); whether the panel is shown no longer is, since the selection decides. Closing the panel by
  hand unlocks it.
- **`shell.panel.following()` says the views are changing size because the panel followed the selection.** A view's
  ResizeObserver then keeps still rather than keeping its middle in the middle, and the view moves the selection into
  sight only if the panel now covers it. The flag is cleared two frames later, after the ResizeObserver has run.
- **The code view has a copy button,** in `codeHtml`, so both tools have it: the lines shown, as the source has them,
  joined with line feeds (the browser writes the system's line breaks). It's in a row of no height that sticks to the
  top of what scrolls, and sticks to that row's right, so it stays in view either way; where a tool's code box scrolls
  sideways on its own (pqdoc's), it stays at the top of the box. "Copied" shows for two seconds and is read out from a
  live region (`#announcer`). Without the clipboard API, or if it's refused, the older copy command is tried; if that
  fails too, the code is selected for Ctrl+C.
- **The lock and copy icons are our own,** like the folder (see below).

### The Export tab and importing (step H2)

- **The Export tab is the shell's,** added after the tool's tabs by `ShellPage`, with its view in the template. The tool
  gives it what it needs through `shell.exports.init`. It lists the explorer's layout, down to whole items: what's inside
  an item, such as a module's members, can't be picked.
- **Its picks are its own,** remembered per workbook as `<tool>-export|<workbook>`, and apart from the explorer's filter,
  which says what the views show, not what to take. *Add what's ticked in the explorer* starts from it, and a tick on
  something inside an item stands for the item (`pickFor`).
- **It isn't a view of the workbook,** so it lists everything, whatever the filter, hiding, the Show… choices and focus
  are doing, and has no notes about them. That's why it has no `.view-toolbar`, which is what gets those notes.
- **The rules are Core's, and the page gets them as data:** what can't be picked and why, and what each item relies on.
  The page and `ImportPlan` only follow what uses what, which is short enough to have twice, and a vbadoc browser test
  compares the two for every module in the sample.
- **`ImportPlan`:** what was picked, what that brings (each with what uses it), what it relies on that can't come
  (`ImportNeed`: what it is and why, listed once with everything that relies on it), and what may be wanted too, such as
  an interface's implementations, which nothing uses and so nothing brings.
- **The manifest** is `<tool>-export.json`: `{ "formatVersion": 1, "items": [...], "leaveOutReferences": [...], "tool":
  "<tool>", "workbook": "<name>" }`, written as a views file is. It holds what was picked, not what that brings, so
  running it again after the source changes picks up new dependencies. A manifest loaded into the page replaces the
  choice, with Undo; what the workbook can't give is left out and listed. Another tool's, or a newer format, is refused
  with the reason, by the page and by `ExportManifest`.
- **A bundle** is a folder of the chosen files and a `README.md`, whose first line marks the folder as a bundle.
  `BundleFolder` only writes into an empty folder or an earlier bundle of the same tool, because `SourceFolderWriter`
  deletes the tool's other files, and someone's own modules mustn't be at risk from a mistyped path. `--on-exists` and
  `--force` apply to replacing an earlier bundle that would change.
- **`import` is a subcommand,** so the root command has an empty action: a command with subcommands otherwise has to be
  given one. `CliRunner` tells that action from `--help` and `--version`, and `--verbose` and `--quiet` are recursive.

### Writing into workbooks (steps H3 and H4)

- **`OpcPackage` holds parts as bytes** and parses only what it's asked to, so a part nobody touched goes back byte for
  byte, which is what keeps a label. XML is read with its whitespace kept, as a cell can hold nothing else, and saved as
  Excel does.
- **`ImportTargetOptions` adds itself to the command** rather than being part of `ImportOptions`, so a tool gets the
  options when it can write workbooks. It takes over the help text of `--bundle` and `--force`, which say more once there's
  a workbook to write. `shell.exports.init`'s `writeTo` is the same step in the page: the hint is a bundle without it.
- **`--on-exists` and `--on-clash` are separate questions,** one about the file written and the other about the items in it,
  so `--force` answers only the first, and `--in-place`'s are-you-sure, which is about the file too. `--on-exists` can't be
  given with `--in-place`, so it can't stand in for that answer: only `--force` or a person can give it.
- **`ClashResolver` stops rather than skips what can't be replaced** for `--on-clash overwrite`: asked to replace
  everything, leaving something out unasked would be the wrong surprise. Overwrite All goes on asking about those, as
  it's the only answer that has to.
- **`ImportRun.ChangeInPlace` says a target can't be replaced before it asks,** by opening it for itself alone, so nobody
  answers a question about what can't happen. The build happens after every answer, and only its finished, read-back
  result reaches `AtomicFileWriter.Replace`, which uses `File.Replace` so the file keeps its permissions and creation
  time. A move would give it the folder's.

### Printing (step L5 of the generalisation plan)

- **The Print menu is the shell's,** in the top bar after Views, shown once a tool calls
  `shell.print.init({ ready(), element(), describe(id) })`. This view, Every saved view (with how many), and Include
  each item's source, remembered as `<tool>-print-source`.
- **The document is its own element,** `#print-document`, filled when asked and shown only in print while
  `html.printing` is set, when everything else in the body is hidden. So the browser's own Print still prints the
  page as it looks, and nothing about the page's layout had to change for print.
- **`preparePrint(which, options)`** makes it and gives a promise; `finishPrint()` empties it and puts the theme
  back. The menu calls `window.print()` between the two, finishing on `afterprint`. Tests call the two directly.
- **Every saved view:** the arrangement is captured, each listed view is applied in turn (as opening it does, without
  making it the view showing), the tool's `ready()` awaited, its part made, and the captured arrangement applied
  again at the end.
- **A diagram is copied as drawn** (`printCopy`): a deep clone, with each SVG element's computed style written onto
  its copy (a fixed list of properties: fill, stroke, fonts, markers, display and so on), so the copy needs none of
  the page's rules, which are often written against a view's id. Ids are prefixed and `url(#…)` references rewritten,
  so its arrowheads are its own and still draw when the original is hidden. HTML in a copy keeps its classes and
  loses its ids.
- **An SVG with a `g.viewport`** (every graph and the class diagram) loses the viewport's pan and zoom and gets a
  `viewBox` around what it shows, so it fills the page's width however the page was zoomed. Any other SVG gets a
  `viewBox` of its own size and scales down to fit.
- **In the light theme.** The theme is set to light while the document exists, without being remembered, so the
  computed styles copied are the light ones and the code's colours are too; `finishPrint` puts it back.
- **What's listed** is what `items.init`'s `shown()` gives for the open view, the same answer Lock view uses;
  without it, the ticked items, or the selection. At most 200 items (`PRINT_ITEMS`), and 300 lines of source an item
  (`PRINT_SOURCE_LINES`), each with a line saying how many more there are.
- **`describe(id)`** gives `{ name, kind, signature, note, description, uses, usedBy, source: { id, first, last } }`:
  `signature` prints as code and `note` as text, and `uses` and `usedBy` are names.
- **Each view after the first starts a new page** (`break-before: page`), and an item isn't split across pages unless
  it has source.

### Views built into the page (step L4 of the generalisation plan)

- **`ViewsFile.Read(folder, tool, source)`** reads `<tool>-views.json`, the file the Views menu exports, and gives
  `ViewsFileResult`: the JSON to build in (`formatVersion`, `tool` and `views`, indented, with line feeds, so the
  same file always gives the same text), how many views that is, and a warning or null. No file is
  `ViewsFileResult.None`. It never throws for what's in the file, and never writes.
- **What makes a file unusable,** each a warning naming the file and the reason, with nothing built in: not JSON
  (comments and trailing commas are allowed, as people edit these by hand), not a views file (no `views` array or
  `formatVersion`), another tool's, or a `formatVersion` above `ViewsFile.FormatVersion`. Views without a name are
  left out and counted. A file whose `workbook` isn't this page's is built in, with a warning that its views may
  mention items the page doesn't have, since the page already copes with those.
- **`ShellPage.ViewsJson`** puts them in the page as `<script type="application/json" id="<tool>-views">`, made safe
  as the data is. With no views there's no block.
- **In the page,** the shell lists the page's views first, then the person's own (`listedViews`), each of the page's
  marked "in the page". They have Save over but no Edit or Delete, as the page can't change itself.
- **A person's own version.** Save over, Save changes, or a new view saved over one of the page's makes a view of
  that name in the browser, shown in the page's view's place and marked "yours". Deleting it shows the page's again.
  Names are matched whatever their case, as they always were.
- **A browser's view that's the same as the page's isn't kept** (`dropRedundantViews`, comparing the description and
  the arrangement): when the page loads, after an import, and after a save. On loading, the browser's store is
  written back without them. That's what lets the round trip work: export your views, put the file in the folder,
  run the tool, and the page's views take over from the copies in your browser. Without it, an old copy in one
  person's browser would hide a newer view of the same name that came with a newer page.
- **Export writes every view listed,** the page's with the person's versions in their place, so the file can replace
  the one in the folder. Its status says where to put it.
- **`VIEWS_FORMAT` in `shell.js` and `ViewsFile.FormatVersion` are the same number,** and change together.

### Peek (step L2 of the generalisation plan)

- **A click on a line in a Uses / Used by list peeks:** a preview opens under the line (`.ref-peek`, in the line's
  list item), with the code around it from the same source the side panel reads, highlighted, and the line marked
  (`.peek-target`). Nothing else changes: not the selection, the views or the panel's own source.
- **Going there** is Go to in the preview (`[data-peek-go]`) or Ctrl+click on the line: the item in `refs[].in` is
  selected and `showLine` marks the line, as a click did before. Ctrl+click no longer passes "the other thing" on to
  the selection, since it's used up choosing between peeking and going.
- **How much it shows:** the line and 5 either side (`PEEK_CONTEXT`), kept within `refs[].first` to `refs[].last`,
  the lines of what holds the reference, when the tool gives them, and within the file otherwise. When there's more
  of it, Show all of it (`[data-peek-whole]`) shows it all, in a box that scrolls to the line, and Show less goes back.
- **Any number can be open.** A click on its line closes one. Esc closes the last opened that's still in the page,
  after an open menu or a box with text in it and before the selection is cleared, and stops the event there, as the
  views have Esc handlers of their own on the document.
- **Without the source** the preview says so, and in Load folder mode offers Load folder (any button with
  `data-load-folder` opens it, as `#load-folder-prompt` does). Go to still works.
- **Previews belong to the list as drawn.** They aren't remembered or kept in a saved view, and they go when a tool
  draws its list again (a new selection, Load folder, hiding something).
- **Only a list's lines peek** (`[data-peek]`). Any other button with `data-ref-select` and `data-ref-line`, such as
  pqdoc's steps, goes straight to its line, with Ctrl+click as the other thing for the selection.
- **The list holding a preview is marked `has-peek`,** for a tool that caps its lists' height, as vbadoc does.
- **A preview's lines have `data-peek-line`, not `data-line`,** so `showLine` and a tool's own look-ups of the panel's
  source never find them.

### Lock view (step L1 of the generalisation plan)

- **Lock view is the filter, plus a hold on the views,** so there's no second idea of what's shown. The shell puts
  the button at the start of every `.view-toolbar` (`[data-lock-view]`), one lock for the page. Locking asks the tool
  what the open view shows (`items.init`'s `shown()`, giving `{ ids, alone }` or null), ticks those, and keeps the
  ticks from before; unlocking puts them back. With null (a view that can't say, such as vbadoc's Calls tab) the
  ticks stay as they are and only the hold applies.
- **The hold:** `focused()` and `focusFavourites()` are false while it's on, so every view that follows them stops
  focusing without knowing of the lock. What else a view does on a click (lighting, fitting, centring) is its own to
  stop, by asking `lock.on()`.
- **`onLockChange(fn)` is told in place of `onFilterChange`,** once the filter and the lock have both changed, so a
  tool draws once.
- **"Alone":** an item in the filter by itself, without what it holds (`filter.alone(id)`, `aloneIds()`). A tick
  never makes one; only locking does, for an item shown without all it holds. Its box is half ticked, and a click on
  it drops it. `filter.active()` counts them.
- **Never silent:** the filter's notes read "Locked: …" in place of "Filtered: …", with Unlock in place of Clear
  (clearing the ticks would show everything, still locked), the button is pressed in every toolbar, and
  `html.view-locked` is there for a view's styles.
- **Remembered per workbook** as `<tool>-lock|<workbook>` (the ticks to go back to) and `<tool>-filter-alone|<workbook>`.
  A saved view keeps `lock` and `alone`, only when it has them, so views saved before don't change.

### Saved views (step G1)

- **The Views menu is the shell's,** in the top bar after the tabs: Save as a new view (a name, unique whatever its
  case, and an optional description), Save changes to the view last opened once the page has changed since, open,
  Save over (any other view, without opening it first), Edit (rename and describe), Delete, Export and Import. A new
  view given a name that's taken offers Save over it. Save over, Save changes, Delete and Import offer Undo, which puts
  the list back as it was (`listBefore`), until the menu's next status. The button shows the view last opened or saved.
- **A view holds everything the page shows** (since G1's review): the shell's part, `tab`; `selection`
  (`{ id, focused }`, when something's selected); `focus` (`{ onClick, favourites }`: Focus on click and Focus on
  favourites); `filter`; `hidden`; `show` (the Show… choices, and `share`, the widely used share); `explorer`
  (`{ shown, size, layout }`) and `panel` (`{ shown, size, locked }`), where `size` is the panel's share of the layout
  or null for the default; **and the tool's,** each added with `shell.views.part(name, { save(), apply(value),
  ids(value) })`. `save()` gives JSON (or null for nothing); `apply(value)` sets the view without drawing, and `value`
  is undefined for a view without that part, which starts afresh; `ids(value)` lists the items it mentions.
  Favourites, the theme, and preferences such as how dragging works stay the person's own. A view saved before G1's
  review has no `selection`, `focus`, `explorer` or `panel`, and opening it leaves those as they are.
- **Opening a view** sets the shell's state without its usual events, the selection and its focus included, with the
  panels as the view has them rather than following the selection, and keeps the views still while the panels change
  size (`panel.following()`), since they're drawn for their new size. Then each part's `apply`, then `onViewApplied`
  once, with the view's selection, which the tool takes as its own without calling `selectionChanged` (in place of
  the selection's events, `onFilterChange`, `onHiddenChange` and `onChoicesChange`), and then `onViewChange` for the
  view's tab, which is shown without an event of its own first.
- **Kept per workbook in the browser** as `<tool>-views|<workbook>`; if the browser can't keep them, the menu says to
  export them. Which view was open isn't kept.
- **The file** is `<tool>-views.json`: `{ "formatVersion": 1, "tool": "<tool>", "views": [...], "workbook": "<name>" }`,
  written with sorted keys and 2-space indentation and a final line feed, downloaded through a `Blob` link. Import (a
  hidden file input, `#views-import`) adds its views, replacing those of the same name; a file that isn't JSON or has
  no `views`, another tool's, or a newer format is refused with the reason, and views without a name are left out and
  counted. A file that names another workbook, or mentions items this one doesn't have, is checked first
  (`#views-confirm`): the menu says so, with the views it would replace, and imports only on Import anyway. The
  workbook's name is optional, so G1's files still import, checked only for missing items.
- **Items a view mentions that the workbook doesn't have** (`items.init`'s `exists(id)`) are listed before an import,
  in the menu, and on opening, in the page notice (`#page-notice`, across the top of the page, with Dismiss), up to
  eight by name, and all of them on the console as a warning. They're never an error.

### Uses / Used by and showing a line (step E1)

- **The list is the shell's** (`shell.references.listHtml(entries, empty)`), each item once: its name selects it, its
  tags (the tool's: kinds of link, number of references) follow, and opening it lists the lines where it's referenced,
  with their text when the source is there. A tool keeps its own headings and containers around it.
- **Going to a line** (since step L2, Go to in its preview or Ctrl+click; a click before) selects the item that holds
  it (an entry's `refs[].in`), then `shell.source.showLine(line)` scrolls the side panel's source to the middle and
  marks the line until something else is. The tool's code view must
  be drawn by `codeHtml`, whose lines carry `data-line`.
- **Line numbers are the file's,** as the page shows it: vbadoc's exported module file, pqdoc's `.m` file.

### Widely used (step D4)

- **The tool counts, the shell lists:** `items.init`'s `widelyUsed(share)` returns the explorer nodes for what's used
  by at least `share`% of the others, most used first, which may hold children (vbadoc puts a module's widely used
  members under it). The shell lists them in a Widely used group after the layout and before Hidden, closed to begin
  with, and puts a hub after the name of each, wherever it is, with the node's title as its tooltip.
- **Hide all** hides the group's top-level items, and becomes Show all once they're all hidden. Nothing is hidden to
  begin with.
- **The share** is a section of the Show… menu (a number, 1 to 100, 20 to begin with), remembered per tool as
  `<tool>-widely-used-share`. It isn't counted as a Show… choice, since it changes what's marked, not what's shown.
- **The shell's groups** (Favourites, Widely used, Hidden) keep everything whatever the Show… choices, and an item's
  main row is always the one in the layout.

### The Show… menu (step D3c)

- **One Show… menu in the explorer** (`shell.choices`: `init({ sections })`, `on(id)`, `value(name)`, `count()`,
  `key()`, `reset()`, and `onChoicesChange`) chooses what kinds of thing the tree and every view show, in place of the
  views' own menus. The tool gives sections of check boxes (ticked, so shown, unless turned off) or radios (the first
  the default), and says through `items.init`'s `kept(id)` whether the choices keep an item in the tree; its views apply
  the choices as they mean something to them. Remembered per tool as `<tool>-show`.
- **The tree drops what's left out,** and folders and groups left with nothing; the Favourites and Hidden groups keep
  everything, being the user's own lists. Left-out rows don't count as matches for the filter box or as rows for the
  keyboard.
- **Never silent:** the button counts the choices in effect ("Show… (2)"), outlined in the accent colour, and so does a
  note at the end of every view's toolbar, whose count opens the menu (and the explorer, if it's closed) and whose Show
  everything puts the choices back.
- **Show nothing and All / None** (since E5a): Show nothing clears every box, and each section of boxes has All and
  None in its heading, so a few things can be picked quickly. Radios stay as they are.
- **The menu fits the window:** it's opened by the shell so it can measure first, takes the room below it, and moves
  left if it would run off; its sections flow into two columns where there's room, and long labels wrap.
- **Drop-downs are the shell's:** every `<details class="dropdown">` closes on a press outside it or on Esc, which
  closes the menu before it clears the selection.

### Hiding and Focus on favourites (step D3b)

- **Hiding is the shell's** (`shell.hidden`: `list()`, `has(id)`, `toggle(id)`, `clear()`, `onHiddenChange`), remembered
  per workbook as `<tool>-hidden|<workbook>`; the tool says what else goes with an item (a vbadoc module's members) and
  combines it with the filter into what its views show.
- **Every hide button has `data-hide`,** kept in step as the stars are: one beside each row's star, shown on hover and
  staying once pressed, and one for the side panel's header from `hidden.buttonHtml(id)`. The row's doesn't select.
- **Never silent:** a hidden item stays in the layout, struck through with what it holds, and is listed in a Hidden
  group at the foot of the tree, closed to begin with, whose rows have no checkboxes and whose Show all brings them
  all back. Each view's toolbar counts hidden items, and the count opens the explorer at that group. The tool puts
  `hidden.noteHtml(id, hiddenNear)` in its side panel, which says when the item is hidden (with Show it) or how many of
  the items it's linked to are.
- **Focus on favourites** (`focusFavourites()`, `setFocusFavourites(on)`) is a page-wide setting, remembered as
  `<tool>-focus-favourites`, turned on and off by the Focus button on the Favourites group's row. While it's on and
  there are favourites, the views show them and what they use, as well as the focused selection's own, and each
  toolbar says so, with Show all. Changes, including to the favourites while it's on, come through `onFocusChange`.
- **The toolbar notes** are the shell's, in the order Focus on favourites, the filter, hidden items, and (since D3c)
  the Show… choices.
- **A row short of room shortens its detail before its name,** since the buttons at its end take some of it.

### The views' filter, and Expand / Collapse all (step D3a)

- **Every row has a checkbox, apart from its name,** so ticking and selecting are two different things: a click on the
  box ticks, a click on the name selects, and neither does the other. Space ticks from the keyboard, so Enter alone
  chooses. Esc clears the selection, not the ticks.
- **While anything is ticked the views show only that** (`shell.filter`: `active()`, `has(id)`, `ids()`, `clear()`,
  and `onFilterChange`). The shell keeps the ticks, remembered per workbook as `<tool>-filter|<workbook>`, and the tool
  says what a tick covers (a vbadoc module brings its members) and draws its views from that. Ticks on items no longer
  in the workbook are kept but don't count.
- **Ticking a folder or group ticks the items in it,** the first item on each branch. An item's tick covers what it
  holds, so a module's members show ticked and can't be unticked on their own; a group with some ticks shows a dash.
- **It's never silent:** a bar under the layouts and a note at the end of every view's toolbar say what's shown, in the
  tool's words (`items.init`'s `describeFilter(ids)`, such as "5 of 70 modules"), each with Clear.
- **Expand all opens every folder and group, not each item's children,** which on a large project would list every
  member; Collapse all closes everything. Both are icon buttons beside the filter box, in our own SVG.
- **`.link-button`** (the bar's Clear) moved into the shell's CSS from vbadoc's.

### Favourites (step D2)

- **Favourites are the shell's** (`shell.favourites`), so both tools have them the same way: any number, starred from
  an explorer row or the side panel's header, listed in a Favourites group at the top of the explorer in every layout,
  in the order they were starred, and remembered per workbook as `<tool>-favourites|<workbook>`. A favourite that's no
  longer in the workbook is ignored, not forgotten, so it comes back if the item does.
- **The tool says three things** in `items.init` (`favourites.init` before D3a): the workbook, whether an id is in it,
  and the explorer node for one (without children; vbadoc names a member `Module.Member`, as it isn't under its module
  there). Since D3a it also says how to describe the filter (below). It draws the side panel's star with
  `favourites.buttonHtml(id)`, and its views listen with `onFavouritesChange`.
- **Every star is a button with `data-favourite`,** which one handler toggles and keeps in step. A row's star doesn't
  select (the tree ignores clicks on it), shows on hover and stays once starred, and isn't in the tab order: the
  panel's star is the keyboard's way.
- **An item can show twice,** in Favourites and in the layout. Both are marked when selected; the one in the layout is
  opened up to; and the tree scrolls only if neither is in view, so choosing a favourite leaves the tree where it is.
  The Favourites group is drawn again when favourites change, keeping it open or closed as it was.
- **The star is our own SVG,** drawn for the page like the other icons.

### Explorer layouts (step D1b)

- **A tool can give more than one layout of the same items** (`layouts`: `[{ name, label, title, nodes }]`), such as
  folders and kinds. With more than one, a switch above the tree chooses, remembered per tool as
  `<tool>-explorer-layout`; the first is the default. Changing layout keeps the filter and the selection.
- **A node with `folder: true` is drawn with a folder icon,** so the folder layout looks different from the kind
  layout at a glance. The icon is our own inline SVG, not from an icon set, whose free licences (such as Font Awesome's
  CC BY 4.0) aren't among the guidelines' and ask for attribution in every page (agreed with the user).
- **A node's tooltip is on its row,** not the whole item, so a group's (such as a query group's description) doesn't
  show over the rows inside it.
- **The segmented control moved into `shell.css`** from vbadoc's stylesheet, since the switch uses it; vbadoc's
  toolbars look the same.

### Asking before overwriting (step A5)

- **pqdoc's overwrite code moved here** when vbadoc took up `--on-exists` and `--force`: the policy, the prompt and
  the options, with their messages unchanged. The resolver works on anything with a path and a name for messages
  (`IPlannedOutput`), so pqdoc's outputs keep their formats and vbadoc's page is just `page`.
- **A tool says which files count as existing.** The resolver takes a check, so vbadoc can leave out a page it would
  write unchanged: there's nothing to overwrite, so nothing to ask.
- **This library now uses Serilog and System.CommandLine**, which both tools already did, for the policy's messages
  and the options.
