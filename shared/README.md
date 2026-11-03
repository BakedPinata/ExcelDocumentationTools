# ExcelDocumenter.Shared

The code both tools need, kept once. vbadoc's and pqdoc's Core libraries use it; it never uses them, and they never use
each other. It isn't a product: it has no command line, no version of its own, and changes to it change both tools'
build stamps. See "Structure" in [docs/GUIDELINES.md](../docs/GUIDELINES.md) for what belongs here.

## What's in it

| Folder | What | Used by |
|---|---|---|
| (root) | `ExitCodes`, the codes every tool returns; `ToolVersion`, the build stamp read from a tool's own assembly | both |
| `Workbooks/` | `WorkbookFile`: reading a workbook while Excel has it open, telling zip-based from compound files, spotting encryption; `ExtractionException`, exit code 3 | both readers |
| `Files/` | `AtomicFileWriter`: write to a temporary file, then move it into place; `SourceFolderWriter`: keep a folder of source files in step with a workbook | vbadoc's module files and page; pqdoc's `.m` files and outputs |
| `Pages/` | `PageBuilder`: filling a template's placeholders, inlining scripts after d3, escaping text and JSON, the favicon; `EmbeddedResources`; `VendoredScripts` | both pages |
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
