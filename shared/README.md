# ExcelDocumenter.Shared

The code both tools need, kept once. vbadoc's and pqdoc's Core libraries use it; it never uses them, and they never use
each other. It isn't a product: it has no command line, no version of its own, and changes to it change both tools'
build stamps. See "Structure" in [docs/GUIDELINES.md](../docs/GUIDELINES.md) for what belongs here.

## What's in it

| Folder | What | Used by |
|---|---|---|
| (root) | `ExitCodes`, the codes every tool returns; `ToolVersion`, the build stamp read from a tool's own assembly | both |
| `Workbooks/` | `WorkbookFile`: reading a workbook while Excel has it open, telling zip-based from compound files, spotting encryption; `ExtractionException`, exit code 3 | both readers |
| `Files/` | `AtomicFileWriter`: write to a temporary file, then move it into place; `SourceFolderWriter`: keep a folder of source files in step with a workbook; `OverwriteResolver` and `OnExistsBehaviour`: what to do when an output already exists, asking through `IUserInteraction` | vbadoc's module files and page; pqdoc's `.m` files and outputs |
| `CommandLine/` | `OverwriteOptions`: `--on-exists` and `--force` (`-y`), checked and bound the same way in both tools; `ConsoleUserInteraction`: the yes / no / all / quit prompt, on standard error | both command lines |
| `Pages/` | `ShellPage`: a page built on the shell, from the parts a tool brings; `PageBuilder`: filling a template's placeholders, inlining scripts after d3 and the shell's, escaping text and JSON, the favicon; `EmbeddedResources`; `VendoredScripts` | both pages |
| `Resources/Shell/` | The page shell: `shell.html` (the template), `shell.css` (the frame and shared colour tokens) and `shell.js` (theme, settings, tabs, the explorer, the side panel, their splitters, Load folder, source view; its interface is at the top) | both pages |
| `Resources/Vendor/` | d3 and its licence, byte-for-byte as published (see its README) | both pages |

## Commands

Run these from the repository root, so both tools build and test against any change here.

```bash
dotnet build ExcelDocumentationTools.sln -warnaserror
dotnet test --project shared/tests/ExcelDocumenter.Shared.Tests   # this library's own tests
dotnet test --solution ExcelDocumentationTools.sln                # everything, since both tools depend on it
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
