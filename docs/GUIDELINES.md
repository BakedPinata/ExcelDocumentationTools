# Guidelines

The conventions every tool in this repository follows, so that they look, behave and are built the same way.
Decisions that belong to one tool are in its own `docs/DECISIONS.md`.

## Naming

**The short name is the product; the long name is the code's identity.**

| | vbadoc | pqdoc |
|---|---|---|
| Executable (`AssemblyName` of the console app) | `vbadoc` | `pqdoc` |
| Tool folder | `vbadoc/` | `pqdoc/` |
| Product name in docs, page titles, `Product` | vbadoc | pqdoc |
| Release tags | `vbadoc-v<version>` | `pqdoc-v<version>` |
| Page JavaScript global and browser storage keys | `window.vbadoc`, `vbadoc-…` | `window.pqdoc`, `pqdoc-…` |
| Documentation files, for `Sales.xlsm` (`--output-name` overrides) | `Sales-vba.html` | `Sales-powerquery.html`, `.csv`, `.puml` |
| Solution | `vbadoc/VbaDocumenter.sln` | `pqdoc/PowerQueryDocumenter.sln` |
| Projects and namespaces | `VbaDocumenter`, `VbaDocumenter.Core`, `VbaDocumenter.Tests`, `VbaDocumenter.BrowserTests` | `PowerQueryDocumenter`, `PowerQueryDocumenter.Core`, `PowerQueryDocumenter.Tests`, `PowerQueryDocumenter.BrowserTests` |

Classes keep names that describe what they do, such as `DependencyAnalyser`.

**Documentation files are named after what they document**, the workbook's name (or a folder's) and a suffix for the
tool, because a file that's emailed or printed loses its folder. `#` and `%` become `_` in file names only, and titles
show the real name. Each tool deletes older files it wrote under another name from a folder it owns, recognising them
by the generator tag every page carries, and leaves everything else alone. Settings files, such as saved views, keep
fixed names.

## Spelling

Use Australian English in everything we write: identifiers, comments, docs and UI text (`Analyser`, `colour`,
`behaviour`, `licence` as a noun, `tokeniser`).

Names that come from somewhere else keep their own spelling: .NET, VBA, CSS, the DOM, libraries and file formats
(`Class_Initialize`, `JsonSerializer`, CSS `color`, `NO_COLOR`, the `LICENSE` file). So do values stored in the data
of generated pages, because changing one changes every page.

## Platform and build

- Every project targets **.NET 10**, set once in the root `Directory.Build.props`. .NET 10 is a long-term support
  release, supported until November 2028. Moving to a newer version is a deliberate decision, made when the work
  machine has its SDK.
- The root `global.json` selects the SDK, and switches `dotnet test` to the Microsoft Testing Platform, which xUnit v3
  needs on .NET 10.
- **Everything works from the `dotnet` command line:** building, testing, running and publishing, as the root
  `README.md` sets out. The work machine has Visual Studio 2022, which doesn't support .NET 10, so no step may need an
  IDE.
- The root `Directory.Build.props` holds `TargetFramework`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors`,
  `Deterministic` and `ToolsPublishDir`. Each tool's `Directory.Build.props` imports it and sets `Version`, `Product` and
  `ToolRoot`.
- **Central Package Management:** the root `Directory.Packages.props` holds every NuGet version, and `PackageReference`
  items have no `Version`. Keep a comment beside any version chosen for a reason.

## Code style

- The root `.editorconfig` holds the rules. They are suggestions, not build errors.
- **C# uses 2-space indentation.** Every other file type follows its language's usual convention, as `.editorconfig`
  sets out: 2 spaces for HTML, CSS, JavaScript, JSON, YAML and MSBuild files, and tabs for `.sln`.
- Write clear, conventional C# rather than clever code. Briefly explain non-obvious design decisions in comments.
- Line endings are left to `.gitattributes`. Embedded resources and test fixtures are `-text`, so they stay
  byte-for-byte as committed. Vendored files and test fixtures are never reformatted.
- A commit that only changes formatting does nothing else, and its hash goes in `.git-blame-ignore-revs`.

## Structure

- Each tool has a **Core library** holding all the logic, and a console app that only parses arguments, prints and
  sets the exit code.
- Each tool's folder has a `README.md`, `Requirements.md`, `CLAUDE.md`, `docs/PLAN.md`, `docs/DECISIONS.md`, its
  solution, `src/` and `tests/`.
- **Code both tools need lives once, in `shared/`** (`ExcelDocumenter.Shared`, with `ExcelDocumenter.Shared.Tests` and
  its own solution). It holds only what is duplicated or needed by both: opening a workbook, exit codes, the build
  stamp, writing a source folder and atomic writes, asking before overwriting (`--on-exists`, `--force`), the page
  shell and its helpers, and vendored d3. The tools' Core
  libraries use it and never each other, and it never uses them. It isn't a product, so it has no short name or version
  of its own; `shared/README.md` says what's in it and records its decisions. Code that only one tool needs stays in
  that tool, even if the other might need it one day.

## Command line, logging and exit codes

- Every tool uses **System.CommandLine**, so `--help`, `--version`, validation and usage text work the same way.
- Shared options, where they apply: `--output`/`-o` (output folder), `--on-exists` and `--force`/`-y` (what to do
  when an output already exists: ask when interactive, otherwise fail, unless told otherwise), `--verbose`/`-v`,
  `--quiet`/`-q`, `--help`/`-h`/`-?` and `--version`. Files a tool keeps in step with the workbook, such as module
  and `.m` files, are never asked about, and neither is an output that would be written unchanged.
- Every tool logs with **Serilog** (console sink). Messages go to standard error, with colour only when standard
  error is a terminal and `NO_COLOR` isn't set.
- Every tool uses these exit codes:

  | Code | Meaning |
  |---|---|
  | 0 | Success |
  | 1 | Unexpected error |
  | 2 | Invalid arguments or configuration |
  | 3 | Input problem (missing or unreadable input) |
  | 4 | Output refused (an output exists and overwriting was refused) |
  | 5 | One or more outputs failed (others may have succeeded) |
  | 130 | Cancelled with Ctrl+C |

## Versions and the build stamp

- Each tool has its own version, in its own `Directory.Build.props`. Raise it at the end of a stage.
- Each tool shows `<version>+<commit>[-dirty]`, in `--version` and in the pages it generates.
- **The stamp is per tool.** The commit is the last one that touched the tool's folder, `shared/` or the root build
  files, and `-dirty` means uncommitted changes in those paths. The root build files are exactly
  `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` and `global.json`, so root docs don't
  change a stamp and a commit to one tool doesn't change another's pages. A commit to `shared/` changes both, because
  both are built from it.

## Publishing

- Every tool publishes into **one folder**, `ToolsPublishDir` in the root `Directory.Build.props`, so adding that folder
  to `PATH` adds every tool.
- Each console app has a folder publish profile, `Properties/PublishProfiles/FolderProfile.pubxml`, the same in every
  tool. It takes its folder from `ToolsPublishDir` and doesn't name the target framework, which stays in one place.
- `publish.ps1` at the root publishes every project that has that profile, so a new tool only needs to add one. It adds
  to the folder rather than clearing it first, so keep the profile's "delete existing files" option off.
- The folder is for using the tools on this machine. To publish somewhere else, such as for a GitHub release, keep the
  profile and pass `-p:PublishDir=<full path>`. A relative path is taken from each project, so it scatters a copy
  through the repository.

## HTML pages

- **Every page is built on one shell**, in `shared/` (`ShellPage`, with `shell.html`, `shell.css` and `shell.js` in
  `Resources/Shell/`). It owns the frame: colour tokens per theme on `html[data-theme]`, a remembered theme switch
  with system default, system UI fonts, a Content-Security-Policy that blocks all network access, a top bar with tabs,
  search box, Load folder and the build stamp linking to the repository, the side panel and its splitter, and the
  source view. A tool brings its views, its search box, what its side panel shows, its data and a favicon in the same
  style. The shell's interface is written at the top of `shell.js`.
- **The explorer** is the shell's left panel: each tool fills its tree with what the page documents, in one or more
  layouts (such as folders and kinds), and the shell does the rest (the switch between layouts, filter, keyboard,
  following the selection, hiding and resizing).
- **Focus on click, Ctrl+click and Esc work the same in every view of both tools:** one Focus on click setting for the
  page, on by default; Ctrl+click (Cmd+click) does the other thing for a click; Esc clears the selection, after an open
  menu or a box with text in it. A new view follows `focused()` from the shell.
- **A page never says when it was made,** so the same workbook gives the same page and git diffs show only real
  changes. The top bar's summary says when the workbook was last saved instead, from its own document properties.
- **The side panel follows the selection** unless locked open: it opens when something is selected and closes when
  the selection is cleared, and the views keep still while it does. The panels' buttons are icons at the ends of the
  top bar, each on its panel's side.
- **Icons are our own inline SVG,** drawn for the page, not taken from an icon set, so there's no licence or
  attribution to carry. If many more are needed, an icon set can be weighed again, including its effect on how quickly
  the page responds.
- **Each tool keeps its own global, data block and stored settings** (`window.vbadoc`, `#vbadoc-data`, `vbadoc-…`),
  so its views talk to the tool, not the shell, and a setting remembered before the shell keeps working.
- **Generated pages never load anything from the network.**
- Vendored libraries are stored once, byte-for-byte unchanged, in `shared/src/ExcelDocumenter.Shared/Resources/Vendor/`
  with their licence files, and are inlined into each page with the licence as a comment.
- Colours are validated with the dataviz palette validator, not eyeballed.

## Tests

- Every test project uses **xUnit v3**. Test projects don't collect coverage, because nothing reads it.
- `dotnet test` runs on the Microsoft Testing Platform, so it takes a project or solution as `--project` or
  `--solution`, not as a bare path. A bare `dotnet test` in a tool's folder runs that tool's tests.
- A normal test run needs no network and nothing that exists only on one machine, apart from the browser tests'
  one-off download of Chromium on their first run. Any other test that needs something extra is optional, and skips
  itself with a message saying how to enable it.
- Pages have Playwright browser tests in their own project. Tests don't catch layout or colour problems, so look at
  screenshots in both themes too.

## Dependencies and licences

- **Every dependency must be compatible with the repository's MIT licence**, including vendored libraries and
  test-only packages. Permissive licences (MIT, ISC, BSD, Apache-2.0) are fine. Copyleft licences that would apply to
  our own code (GPL, AGPL) are not allowed.
- **MPL-2.0 is allowed for unmodified packages only.** This covers OpenMcdf, whose licence applies to its own files
  and leaves our code under MIT. If we ever change OpenMcdf's files, those changes must be published under MPL-2.0.
- Check the licence before adding a package or moving to a new major version, and keep a comment beside any exception
  in `Directory.Packages.props`.
- Dependencies today: MIT (System.CommandLine, Microsoft.NET.Test.Sdk, Microsoft.Playwright), ISC (d3),
  Apache-2.0 (Serilog, Serilog.Sinks.Console, xunit.v3, xunit.runner.visualstudio) and MPL-2.0 (OpenMcdf).

## Build workflow and Dependabot

- `.github/workflows/build.yml` builds every tool and runs every test on Windows, on each pull request and each push to
  `main`.
- **Dependabot** checks NuGet packages and GitHub Actions weekly and opens pull requests. Minor and patch updates share
  one pull request, and each major update gets its own. It never merges anything itself.
- For each Dependabot pull request: read the release notes, check that the build passed, and for a major version check
  that the licence is still allowed (see above). Then merge it.
- Dependabot doesn't check licences, and it doesn't update the .NET SDK, because moving to a new .NET version is a
  deliberate decision. Vendored d3 isn't a package dependency, so it's still updated by hand, following the `README.md`
  in `shared/src/ExcelDocumenter.Shared/Resources/Vendor/`.

## Claude Code sessions

- Start Claude Code **in a tool's folder** to work on one tool. It then loads the root `CLAUDE.md` and that tool's
  `CLAUDE.md`, searches stay within the tool, and `dotnet build` and `dotnet test` cover only that tool.
- Start it **at the root** for work across tools: shared configuration, `shared/`, this file, aligning the tools.
- The root `CLAUDE.md` stays short: shared rules and a pointer here. Each tool's `CLAUDE.md` has its commands,
  current stage and tool-specific rules.
