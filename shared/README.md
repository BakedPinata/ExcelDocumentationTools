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
| `Resources/Shell/` | The page shell: `shell.html` (the template), `shell.css` (the frame and shared colour tokens) and `shell.js` (theme, settings, tabs, side panel, splitter, Load folder, source view; its interface is at the top) | both pages |
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

### Asking before overwriting (step A5)

- **pqdoc's overwrite code moved here** when vbadoc took up `--on-exists` and `--force`: the policy, the prompt and
  the options, with their messages unchanged. The resolver works on anything with a path and a name for messages
  (`IPlannedOutput`), so pqdoc's outputs keep their formats and vbadoc's page is just `page`.
- **A tool says which files count as existing.** The resolver takes a check, so vbadoc can leave out a page it would
  write unchanged: there's nothing to overwrite, so nothing to ask.
- **This library now uses Serilog and System.CommandLine**, which both tools already did, for the policy's messages
  and the options.
