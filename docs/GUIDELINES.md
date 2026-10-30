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
| Solution | `vbadoc/VbaDocumenter.sln` | `pqdoc/PowerQueryDocumenter.sln` |
| Projects and namespaces | `VbaDocumenter`, `VbaDocumenter.Core`, `VbaDocumenter.Tests`, `VbaDocumenter.BrowserTests` | `PowerQueryDocumenter`, `PowerQueryDocumenter.Core`, `PowerQueryDocumenter.Tests`, `PowerQueryDocumenter.BrowserTests` |

Classes keep names that describe what they do, such as `DependencyAnalyser`.

## Spelling

Use Australian English in everything we write: identifiers, comments, docs and UI text (`Analyser`, `colour`,
`behaviour`, `licence` as a noun, `tokeniser`).

Names that come from somewhere else keep their own spelling: .NET, VBA, CSS, the DOM, libraries and file formats
(`Class_Initialize`, `JsonSerializer`, CSS `color`, `NO_COLOR`, the `LICENSE` file). So do values stored in the data
of generated pages, because changing one changes every page.

## Platform and build

- Every project targets **.NET 9**, set once in the root `Directory.Build.props`. The root `global.json` selects the
  SDK. .NET 9 support ends on 2026-11-10; moving to .NET 10 is a one-line change, to be made when the machine the
  tools run on allows it.
- The root `Directory.Build.props` holds `TargetFramework`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors` and
  `Deterministic`. Each tool's `Directory.Build.props` imports it and sets `Version`, `Product` and `ToolRoot`.
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
- **There is no shared code project for now.** The overlap between the tools is cheaper to copy than to couple. Keep
  them similar through this file and review. If real duplication appears, a shared library goes in `shared/`.

## Command line, logging and exit codes

- Every tool uses **System.CommandLine**, so `--help`, `--version`, validation and usage text work the same way.
- Shared options, where they apply: `--output`/`-o` (output folder), `--verbose`/`-v`, `--quiet`/`-q`,
  `--help`/`-h`/`-?` and `--version`.
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
- **The stamp is per tool.** The commit is the last one that touched the tool's folder or the root build files, and
  `-dirty` means uncommitted changes in those paths. The root build files are exactly `Directory.Build.props`,
  `Directory.Build.targets`, `Directory.Packages.props` and `global.json`, so root docs don't change a stamp and a
  commit to one tool doesn't change another's pages.

## HTML pages

- vbadoc's page is the reference design: colour tokens per theme on `html[data-theme]`, a remembered theme switch
  with system default, system UI fonts, a Content-Security-Policy that blocks all network access, a top bar showing
  the build stamp and linking to the repository, and a favicon in the same style.
- **Generated pages never load anything from the network.**
- Vendored libraries are stored byte-for-byte unchanged in `Resources/Vendor/` with their licence files, and are
  inlined into the page with the licence as a comment.
- Colours are validated with the dataviz palette validator, not eyeballed.

## Tests

- Every test project uses **xUnit v3**. Test projects don't collect coverage, because nothing reads it.
- A normal test run needs no network and nothing that exists only on one machine. A test that does need something
  extra is optional, and skips itself with a message saying how to enable it.
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
- Dependencies today: MIT (System.CommandLine, Microsoft.NET.Test.Sdk, Microsoft.Playwright, Mermaid), ISC (d3),
  Apache-2.0 (Serilog, Serilog.Sinks.Console, xunit.v3, xunit.runner.visualstudio) and MPL-2.0 (OpenMcdf).

## Claude Code sessions

- Start Claude Code **in a tool's folder** to work on one tool. It then loads the root `CLAUDE.md` and that tool's
  `CLAUDE.md`, searches stay within the tool, and `dotnet build` and `dotnet test` cover only that tool.
- Start it **at the root** for work across tools: shared configuration, this file, aligning the tools.
- The root `CLAUDE.md` stays short: shared rules and a pointer here. Each tool's `CLAUDE.md` has its commands,
  current stage and tool-specific rules.

## Where the tools don't follow this yet

`MONOREPO-PLAN.md` brings the tools in line one step at a time. Until then:

| Rule | Tool | Step |
|---|---|---|
| Page design, browser tests, licence comment in the page | pqdoc's page has its own design | 3d |
