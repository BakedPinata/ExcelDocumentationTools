# ExcelDocumentationTools: monorepo plan

This plan brings two Excel documentation tools into one repository, with shared conventions, so they stay
consistent as they grow. It is written to be handed to a fresh Claude Code session (or a developer) to carry out.

## How to use this plan

- Read the whole file before starting. Sections 1–4 are the background and the decisions; section 5 has the steps.
- Work **one step at a time** (3a, 3b and so on are separate steps). At the end of each step:
  1. `dotnet build -warnaserror` and `dotnet test` pass for every tool touched;
  2. update the progress table below;
  3. commit;
  4. summarise what changed and how to check it, then **stop and wait for review**;
  5. push only after the review.
- Ask before doing anything that goes against this plan, or before deciding an open question (section 6).
- Follow the existing rules in `vbadocumenter/CLAUDE.md` (later `vbadoc/CLAUDE.md`) and its `docs/DECISIONS.md`
  unless this plan changes them.
- Use Australian English in everything we write: identifiers, comments, docs and UI text (`Analyser`, `colour`,
  `behaviour`, `licence` as a noun). Names that come from .NET, VBA or a library keep their own spelling
  (`Class_Initialize`, `JsonSerializer`, CSS `color`).

## Progress

| Step | Scope | Status |
|---|---|---|
| 1 | Restructure: one repository, `vbadoc/` and `pqdoc/`, .NET 9, renames, GitHub rename | Done |
| 2 | Shared configuration, guidelines and CLAUDE.md files; 2-space C#; Australian spelling | Done |
| 3a | vbadoc tests to xUnit v3 | Done |
| 3b | vbadoc command line to System.CommandLine; shared exit codes; pqdoc build stamp | Done |
| 3c | pqdoc split into Core library and console app | Done |
| 3d | pqdoc HTML page on vbadoc's design; pqdoc browser tests | Done, awaiting review |
| 3e | Build workflow and Dependabot | |
| 4 | Later: pqdoc reads queries from the workbook; shared library if real duplication appears | |

## 1. Background

The folder `C:\Users\MichaelArcher\source\repos\ExcelDocumentationTools` holds two .NET console tools:

- **`vbadocumenter/`**: vbadoc. It exports the VBA from a macro-enabled workbook into text files and generates a
  self-contained HTML page (dependency graph with d3, class diagram with Mermaid). It is a clean clone of
  https://github.com/BakedPinata/vbadocumenter on `main`, in sync with `origin`, with no tags. It is part-way through
  its own staged plan (`docs/PLAN.md`: stages 0–5d done, stage 6 next). Its `private/` folder holds a real workbook
  and its output: it is git-ignored and must **never** be committed.
- **`PowerQueryDependencyAnalyser/`**: the Power Query tool, to be renamed pqdoc. It reads a folder of exported
  Power Query `.m` files, works out which queries reference which, and exports CSV, PlantUML (source or rendered
  through a PlantUML server) and an interactive HTML graph. Its folder has a `git init` with **no commits**. Its
  first version came from a Claude chat; it is treated as version 1.0.0, so the README's "Breaking changes from
  version 1" section and the requirements' "current behaviour (baseline)" framing go.

Both folders contain `.vs/`, `bin/` and `obj/` caches, which can be deleted.

The machine has .NET SDKs 9.0.319 and 10.0.403 and runtimes 9.0 and 10.0. **There is no .NET 8 runtime**, so
pqdoc (currently `net8.0`) must be moved to `net9.0` before its tests are run.

### How the two tools differ today

| Area | vbadoc | Power Query tool |
|---|---|---|
| Target framework | `net9.0` | `net8.0` |
| Indentation | 4 spaces, Allman braces | 2 spaces, Allman braces |
| Package versions | Central Package Management (`Directory.Packages.props`) | In each `.csproj` |
| xUnit | v2 (`xunit` 2.9.2, runner 2.8.2) | v3 (`xunit.v3` 3.1.0, runner 3.1.4) |
| Warnings as errors | `dotnet build -warnaserror` | `TreatWarningsAsErrors` in `Directory.Build.props` |
| Structure | `VbaDocumenter.Core` library plus thin console app | One project, folders per area |
| Command line | Hand-written parser | System.CommandLine 2.0.0, Serilog |
| Exit codes | 1 bad arguments, 2 workbook problem, 3 unexpected | 1 unexpected, 2 bad arguments, 3 input, 4 output refused, 5 export failed, 130 cancelled |
| Version | `0.5.0+<commit>[-dirty]` via `Directory.Build.targets` | `2.0.0`, commit hash turned off |
| HTML page | Theme switch, Content-Security-Policy, system fonts, blue/orange/aqua tokens, browser tests | Follows system theme only, Bahnschrift, teal/ochre tokens, no CSP, unit tests only |
| Vendored d3 | 7.9.0 at `Resources/Vendor/d3.min.js` | 7.9.0 at `Resources/d3.v7.9.0.min.js` |
| Spelling | `DependencyAnalyzer` (US) | `Analyser` (Australian) |
| Docs | `CLAUDE.md`, `Requirements.md`, `docs/PLAN.md`, `docs/DECISIONS.md` | `README.md`, `requirements.md` |

## 2. Goals and non-goals

**Goals**

- One repository, `ExcelDocumentationTools`, for all of the Excel documentation tools.
- The tools look, behave and are built the same way: naming, code style, build settings, command line, exit codes,
  versioning, HTML pages, tests and docs.
- Each tool can be worked on alone (its own solution, CLAUDE.md and commands), so a session on one tool doesn't
  need to load the other.

**Non-goals**

- Sharing code between the tools for now (see 3.9).
- New features, other than what aligning the tools needs. Step 3e's build workflow and Dependabot are repository
  tooling, not tool features.

## 3. Decisions

### 3.1 Repository

- The GitHub repository `BakedPinata/vbadocumenter` is **renamed** to `BakedPinata/ExcelDocumentationTools`
  (`gh repo rename`). This keeps vbadoc's history and issues, and GitHub redirects the old URL.
- The repository root is the `ExcelDocumentationTools` folder. vbadoc's history is kept by moving its files into
  `vbadoc/` as a pure rename commit (`git log --follow` still works).
- pqdoc has no history to keep; it is added already renamed, so its history starts with the right names.

### 3.2 Naming

**The short name is the product; the long name is the code's identity.**

| | vbadoc | pqdoc |
|---|---|---|
| Executable (`AssemblyName` of the console app) | `vbadoc` | `pqdoc` |
| Tool folder | `vbadoc/` | `pqdoc/` |
| Product name in docs, page titles, `Product` | vbadoc | pqdoc |
| Release tags | `vbadoc-v<version>` | `pqdoc-v<version>` |
| Page JavaScript global and browser storage keys | `window.vbadoc`, `vbadoc-…` | `window.pqdoc`, `pqdoc-…` |
| Solution | `vbadoc/VbaDocumenter.sln` | `pqdoc/PowerQueryDocumenter.sln` |
| Projects and namespaces | `VbaDocumenter`, `VbaDocumenter.Core`, `VbaDocumenter.Tests`, `VbaDocumenter.BrowserTests` | `PowerQueryDocumenter`, `PowerQueryDocumenter.Core` (from 3c), `PowerQueryDocumenter.Tests`, `PowerQueryDocumenter.BrowserTests` (from 3d) |

- Every file, folder, project, namespace and embedded resource name containing `PowerQueryDependencyAnalyser`
  becomes `PowerQueryDocumenter`.
- Classes keep names that describe what they do: `DependencyAnalyser` stays. vbadoc's `DependencyAnalyzer` becomes
  `DependencyAnalyser` (step 2).
- pqdoc's default output name stays `powerquery-dependencies`, because it describes what the file contains.

### 3.3 Spelling

Australian English, as described under "How to use this plan".

### 3.4 Platform and build

- **.NET 9 (`net9.0`) for every project**, set once in the root `Directory.Build.props`. One `global.json` at the
  root (`9.0.100`, `rollForward: latestFeature`). .NET 9 support ends on 2026-11-10; moving to .NET 10 stays a
  one-line change, to be done when the machine the tools run on allows it.
- Root `Directory.Build.props`: `TargetFramework`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors`,
  `Deterministic`. Each tool has its own `Directory.Build.props` that imports the root one and sets its `Version`
  and `Product`.
- **Central Package Management**: one root `Directory.Packages.props` holds every NuGet version; `PackageReference`
  items have no `Version`. Keep a comment beside any version chosen for a reason.
- **xUnit v3** for all test projects.

### 3.5 Code style

- vbadoc's `.editorconfig` rules become the root `.editorconfig`, with **2-space indentation for C#**, plus pqdoc's
  rule that `.m` fixtures keep their trailing whitespace. Every other file type follows its language's usual
  convention, as `.editorconfig` already sets out (2 spaces for HTML, CSS, JavaScript, JSON, YAML and MSBuild files,
  tabs for `.sln`). Rules stay suggestions, not build errors. Vendored files and test fixtures are never reformatted.
- Line endings and charset are left to `.gitattributes`. Embedded resources and test fixtures are `-text`, so they
  stay byte-for-byte as committed.
- Clear, conventional C#; brief comments explaining non-obvious design decisions (as in vbadoc's CLAUDE.md).

### 3.6 Structure

- Each tool has a `Core` library holding all the logic, and a console app that only parses arguments, prints and
  sets the exit code (vbadoc already works this way; pqdoc moves to it in 3c).
- Each tool folder has: `README.md`, `Requirements.md`, `CLAUDE.md`, `docs/PLAN.md`, `docs/DECISIONS.md`, its
  solution, `src/` and `tests/`.

### 3.7 Command line, logging and exit codes

- Both tools use **System.CommandLine**, so `--help`, `--version`, validation and usage text work the same way.
- Shared options where they apply: `--output`/`-o` (output folder), `--verbose`/`-v`, `--quiet`/`-q`,
  `--help`/`-h`/`-?`, `--version`. vbadoc's `--out` and pqdoc's `--output-folder` both become `--output`, with no
  aliases for the old names.
- Both tools log with **Serilog** (console sink). Messages go to standard error; colour only when standard error is
  a terminal and `NO_COLOR` isn't set.
- **Shared exit codes** (pqdoc's scheme):

  | Code | Meaning |
  |---|---|
  | 0 | Success |
  | 1 | Unexpected error |
  | 2 | Invalid arguments or configuration |
  | 3 | Input problem (missing or unreadable input, e.g. a workbook vbadoc can't read) |
  | 4 | Output refused (an output exists and overwriting was refused) |
  | 5 | One or more outputs failed (others may have succeeded) |
  | 130 | Cancelled with Ctrl+C |

  For vbadoc this is a breaking change (1 → 2, 2 → 3, 3 → 1), recorded in its README and DECISIONS.md.

### 3.8 Versions and the build stamp

- Each tool has its own version, in its own `Directory.Build.props`. pqdoc starts at **1.0.0**; vbadoc keeps
  raising its version at the end of each of its stages.
- Both tools show `<version>+<commit>[-dirty]` (the informational version), in `--version` and in the pages they
  generate, as vbadoc does today.
- **In a monorepo the stamp is per tool**: the commit is the last one that touched
  that tool's folder or the root build files, and `-dirty` means uncommitted changes in those paths. Otherwise a
  commit to pqdoc would change the stamp in every vbadoc page.
- Each tool's `Directory.Build.props` sets a `ToolRoot` property to its own folder, which the root
  `Directory.Build.targets` uses as the path to check. The root build files are exactly `Directory.Build.props`,
  `Directory.Build.targets`, `Directory.Packages.props` and `global.json`, so root docs don't change the stamp.

### 3.9 Sharing code

- **No shared code project for now.** The tools are kept similar by shared configuration, `docs/GUIDELINES.md` and
  review, not by a shared library. The overlap today (d3, page colours, small helpers such as an atomic file writer)
  is cheaper to copy than to couple.
- Revisit when pqdoc reads workbooks directly (step 4), since both tools would then need the same workbook
  (Open Packaging Conventions) reading code. A shared library would go in `shared/`.

### 3.10 HTML pages

- vbadoc's page is the reference design: colour tokens per theme on `html[data-theme]`, a remembered theme switch
  with system default, system UI fonts, a Content-Security-Policy that blocks all network access, a top bar showing
  the build stamp and linking to the repository, a favicon in the same style.
- Vendored libraries are stored byte-for-byte unchanged in `Resources/Vendor/` with their licence files, and are
  inlined into the page with the licence as a comment. Generated pages never load anything from the network.
- Colours are validated (dataviz palette validator), not eyeballed.

### 3.11 Working with Claude Code

- Start Claude Code **in a tool folder** (`vbadoc/` or `pqdoc/`) to work on one tool. It then loads the root
  `CLAUDE.md` and that tool's `CLAUDE.md`, searches stay within the tool, and `dotnet build`/`dotnet test` build
  only that tool.
- Start it **at the root** for work across tools: shared configuration, `docs/GUIDELINES.md`, aligning the tools.
- The root `CLAUDE.md` stays short: shared rules and a pointer to `docs/GUIDELINES.md`. Each tool's `CLAUDE.md` has
  its commands, current stage and tool-specific rules.

### 3.12 Dependencies

- **Every dependency must be compatible with the repository's MIT licence**, including vendored libraries and
  test-only packages. Permissive licences (MIT, ISC, BSD, Apache-2.0) are fine. Copyleft licences that would apply to
  our own code (GPL, AGPL) are not allowed.
- **MPL-2.0 is allowed for unmodified packages only.** This covers OpenMcdf, whose licence applies to its own files
  and leaves our code under MIT. If we ever change OpenMcdf's files, those changes must be published under MPL-2.0.
- Check the licence before adding a package or moving to a new major version, and keep a comment beside any
  exception in `Directory.Packages.props`.
- Dependencies today: MIT (System.CommandLine, Microsoft.NET.Test.Sdk, Microsoft.Playwright, coverlet.collector, d3,
  Mermaid), Apache-2.0 (Serilog, Serilog.Sinks.Console, xunit, xunit.v3, xunit.runner.visualstudio) and MPL-2.0
  (OpenMcdf).
- **Dependabot** keeps NuGet packages and GitHub Actions up to date through pull requests that we review and merge
  (step 3e). It doesn't update vendored d3 and Mermaid, or check licences.

## 4. Target layout

```
ExcelDocumentationTools/
  README.md                       the tools, what each does, links to each README
  CLAUDE.md                       shared way of working; points to docs/GUIDELINES.md
  docs/GUIDELINES.md              shared conventions (section 3, written as rules)
  LICENSE                         MIT, covers every tool
  .github/                        dependabot.yml, workflows/build.yml (step 3e)
  .editorconfig .gitattributes .gitignore .git-blame-ignore-revs global.json
  Directory.Build.props           shared build settings
  Directory.Build.targets         per-tool build stamp
  Directory.Packages.props        every NuGet version
  ExcelDocumentationTools.sln     every project, in vbadoc and pqdoc solution folders
  vbadoc/
    README.md Requirements.md CLAUDE.md VbaDocumenter.sln Directory.Build.props
    docs/PLAN.md docs/DECISIONS.md docs/images/
    src/VbaDocumenter/ src/VbaDocumenter.Core/
    tests/VbaDocumenter.Tests/ tests/VbaDocumenter.BrowserTests/
    private/                      git-ignored
  pqdoc/
    README.md Requirements.md CLAUDE.md PowerQueryDocumenter.sln Directory.Build.props .env.example
    docs/PLAN.md docs/DECISIONS.md
    src/PowerQueryDocumenter/ src/PowerQueryDocumenter.Core/
    tests/PowerQueryDocumenter.Tests/ tests/PowerQueryDocumenter.BrowserTests/
```

## 5. Steps

### Step 1: Restructure

Goal: one repository with both tools building and passing their tests. The only code changes are pqdoc's move to
.NET 9 and its renames, and vbadoc's repository URL.

**Before starting**

- `git -C vbadocumenter status` is clean and `main` matches `origin/main` (fetch first).
- `gh auth status` shows the `BakedPinata` account.
- Confirm with the user before renaming the GitHub repository and before the first push.

**1.1 Clean up**

- Delete `.vs/`, `bin/` and `obj/` in both tool folders.
- Delete `PowerQueryDependencyAnalyser/.git` (no commits, nothing to keep).

**1.2 Make the root the repository and move vbadoc**

- Move `vbadocumenter/.git` to the root, and rename the folder `vbadocumenter` to `vbadoc`.
- Move `vbadoc/LICENSE` and `vbadoc/global.json` to the root, so both tools build with the .NET 9 SDK.
- Stage only vbadoc's files (`git add -A vbadoc LICENSE global.json` plus the deletions at the old paths) and check
  that `git diff --cached -M --name-status` shows them as renames (`R100`), not as deletions and additions.
  `vbadoc/private/` must stay ignored.
- Commit: "Move vbadoc into vbadoc/". This commit contains only renames.

**1.3 Retarget pqdoc to .NET 9 first**

- In `PowerQueryDependencyAnalyser/Directory.Build.props` set `TargetFramework` to `net9.0` and update the comment
  above it. Do this before any build or test run, since there is no .NET 8 runtime.
- Build and run pqdoc's tests now, before renaming, to get a baseline. Fix anything .NET 9 itself breaks (for
  example new analyser warnings under `TreatWarningsAsErrors`) and note it in the step summary.

**1.4 Rename pqdoc**

- Folder `PowerQueryDependencyAnalyser` → `pqdoc`; solution → `PowerQueryDocumenter.sln`; projects, folders,
  `RootNamespace`, namespaces and `using`s → `PowerQueryDocumenter` / `PowerQueryDocumenter.Tests`.
- `AssemblyName` of the console app → `pqdoc`; `Version` → `1.0.0`; `Product` → `pqdoc`.
- Hard-coded names to change:
  - `Cli/CliDefinition.cs`: `ToolName = "PowerQueryDependencyAnalyser"` → `"pqdoc"`;
  - `Export/PlantUmlClient.cs`: the user agent `("PowerQueryDependencyAnalyser", "2.0")` → `pqdoc` and the
    assembly's version, rather than a second hard-coded version;
  - `Resources/graph-template.html`: `<meta name="generator">`;
  - `EmbeddedResource` `LogicalName`s in the `.csproj`, and the code that loads them;
  - `TestSupport/TempDirectory.cs`: the `pqda-tests` folder → `pqdoc-tests`.
- Search for any remaining `PowerQueryDependencyAnalyser`, `PowerQuery Dependency Analyser`, `pqda` and `2.0.0`
  (outside `bin/`, `obj/`); tests that assert on help or version text change with them. The `System.CommandLine`
  package's `2.0.0` is its own version and stays.
- `requirements.md` → `Requirements.md`. Reword it as the specification of version 1.0.0: drop section 2's
  "current behaviour (baseline)" and anything describing changes from the old tool, but keep the terminology
  (upstream/downstream) and every requirement ID.
- README: drop "Breaking changes from version 1"; use `pqdoc` in commands and examples
  (`dotnet run --project src/PowerQueryDocumenter -- C:\queries`, `pqdoc C:\queries -f puml,html`); .NET 9 in
  Requirements.
- Before pqdoc's first commit, add `src/**/Resources/** -text` and `tests/**/Fixtures/** -text` at the end of its
  `.gitattributes`, so its resources and fixtures are byte-for-byte from the start. Check that its tests still pass.
- Keep pqdoc's own `.editorconfig`, `.gitattributes`, `.gitignore` and `Directory.Build.props` for now; step 2
  merges them. Its 2-space indentation stays as it is.
- Commit: "Add pqdoc (Power Query documenter) 1.0.0".

**1.5 Root files**

- `ExcelDocumentationTools.sln` with every project, under `vbadoc` and `pqdoc` solution folders.
- `README.md`: one paragraph on the repository, a table of the tools (name, what it reads, what it produces, link to
  its README), how to build everything (`dotnet build ExcelDocumentationTools.sln`) and the licence.
- This plan stays at the root as `MONOREPO-PLAN.md` and is committed with the other root files.
- Commit: "Add root README and solution".

**1.6 Rename the GitHub repository** (after confirming with the user)

- `gh repo rename ExcelDocumentationTools --repo BakedPinata/vbadocumenter`, then
  `git remote set-url origin https://github.com/BakedPinata/ExcelDocumentationTools.git`.
- Update the repository description to cover both tools.
- vbadoc's repository URL: `src/VbaDocumenter.Core/ToolVersion.cs` (`RepositoryUrl`) and the assertion in
  `tests/VbaDocumenter.BrowserTests/PageShellTests.cs`. Also check READMEs and docs for the old URL.
- vbadoc's `CLAUDE.md` says "commit, push": from now on, push only after the user has reviewed the step.
- Commit: "Point vbadoc at the renamed repository". Push after review.

**Acceptance**

- In `vbadoc/`: `dotnet build -warnaserror` and `dotnet test` pass.
- In `pqdoc/`: the same.
- At the root: `dotnet build ExcelDocumentationTools.sln -warnaserror` passes.
- `git log --follow vbadoc/src/VbaDocumenter.Core/Analysis/DependencyAnalyzer.cs` shows its full history.
- No file under `vbadoc/private/` is tracked.
- `git grep -i PowerQueryDependencyAnalyser` finds nothing (the old name may appear in this plan only).

### Step 2: Shared configuration, guidelines and style

Goal: everything in section 3.4–3.6 and 3.11 in place. Behaviour doesn't change, apart from spelling in UI text.

- **`.editorconfig`** at the root: vbadoc's, with `indent_size = 2` added under `[*.cs]`, pqdoc's `[*.m]` rule, and
  paths generalised (`**/Resources/Vendor/**`, `**/Fixtures/**`). Delete the tools' own `.editorconfig` files.
- **pqdoc's vendored d3** moves to `Resources/Vendor/d3.min.js` with `d3.LICENSE.txt`, as in vbadoc (byte-for-byte
  unchanged; update the `LogicalName` and loader).
- **`.gitattributes`** at the root: `**/Resources/** -text` and `**/Fixtures/** -text` (plus `*.min.js -diff
  linguist-vendored`). Delete the tools' own files. No renormalising is needed, because pqdoc's resources and
  fixtures have been `-text` since its first commit (1.4).
- **`.gitignore`** at the root: the union of both (`bin/`, `obj/`, `.vs/`, `.idea/`, `*.user`, `TestResults/`,
  `private/`, `.env`, `powerquery-dependencies.*`). Delete the tools' own files.
- **`Directory.Build.props`** at the root (section 3.4), and one per tool that imports it
  (`<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />`)
  and sets `Version`, `Product` and `ToolRoot`.
- **`Directory.Build.targets`** at the root: vbadoc's build stamp, scoped per tool (section 3.8). pqdoc keeps
  `IncludeSourceRevisionInInformationalVersion=false` until 3b.
- **`Directory.Packages.props`** at the root with every package. Where both tools use a package, use the newer
  version (`Microsoft.NET.Test.Sdk` 17.14.1). vbadoc stays on xUnit v2 until 3a: try `xunit.runner.visualstudio`
  3.1.4 for both (it also runs v2 tests); if vbadoc's tests aren't found, give vbadoc's test projects
  `VersionOverride="2.8.2"` until 3a.
- **Re-indent vbadoc's C#** to 2 spaces with `dotnet format whitespace` (vendored files and fixtures excluded by
  `.editorconfig`), in a commit that changes nothing else. Other file types aren't re-indented. Add that commit's hash to `.git-blame-ignore-revs`
  (GitHub uses it automatically; locally `git config blame.ignoreRevsFile .git-blame-ignore-revs`).
- **Australian spelling in vbadoc**, in its own commit: `DependencyAnalyzer` → `DependencyAnalyser`, `Analyze` →
  `Analyse`, and other US spellings in our own identifiers, comments, docs and UI text. Don't rename CSS
  properties, DOM or .NET APIs, or JSON data keys.
- **Docs:**
  - `docs/GUIDELINES.md`: section 3 rewritten as standing rules (naming, spelling, platform, style, structure,
    command line and exit codes, versions and stamp, HTML pages, tests, dependencies and licences, docs per tool,
    Claude Code sessions).
  - Root `CLAUDE.md`: short; start-here links (GUIDELINES, each tool's CLAUDE.md), root build command, and the way
    of working (one stage at a time, review before pushing, ask before changing Requirements or DECISIONS).
  - `vbadoc/CLAUDE.md`: commands now run from `vbadoc/`; point to the root CLAUDE.md and GUIDELINES.
  - `pqdoc/CLAUDE.md`, `pqdoc/docs/PLAN.md` (steps 3b–3d as its stages, version 1.0.0 done) and
    `pqdoc/docs/DECISIONS.md` (decisions recorded in its README and code comments, plus those from this plan).
  - `vbadoc/docs/DECISIONS.md`: update the entries this plan changes (.NET 9 shared, CPM at the root, indentation,
    spelling, build stamp scope, repository) and note that vbadoc's stage 6 continues after step 3.
- Commits: one per bullet group above is fine; the whitespace commit must be on its own.

**Acceptance**: both tools build with `-warnaserror` and pass their tests from their own folders and from the root
solution; no `.editorconfig`, `.gitattributes`, `.gitignore`, `global.json` or `Directory.Packages.props` is left in
a tool folder; `git blame` on a re-indented vbadoc file shows the original authors' commits.

### Step 3a: vbadoc tests to xUnit v3

- Move `VbaDocumenter.Tests` and `VbaDocumenter.BrowserTests` to `xunit.v3` (test projects become executables;
  `IAsyncLifetime` returns `ValueTask`; skipping uses `Assert.Skip`). Remove `xunit` v2 and any `VersionOverride`.
- Check whether `coverlet.collector` is still wanted; use the same choice in both tools.
- Acceptance: the same number of tests run and pass as before.

### Step 3b: Command line and exit codes

- vbadoc moves to System.CommandLine (version from the root `Directory.Packages.props`), keeping its options and
  help text, with `--quiet`/`-q`, `-v` for `--verbose` and `--help`/`-h`/`-?` added.
- vbadoc's `--out` becomes `--output`/`-o` (no alias for `--out`; a breaking change, recorded with the exit codes).
  pqdoc's `--output-folder` becomes `--output` (alias `-o` unchanged). Update both READMEs and tests.
- vbadoc logs with Serilog, configured as pqdoc's `Logging/LoggingSetup.cs` is, so `--verbose` and `--quiet` and the
  message format match. Its console report (summary, `--verbose` member list) goes through the logger too.
- vbadoc uses the shared exit codes (section 3.7). Raise vbadoc's version; record the change in its README,
  DECISIONS.md and PLAN.md.
- pqdoc removes `IncludeSourceRevisionInInformationalVersion=false` so `--version` shows the build stamp.

### Step 3c: pqdoc Core library

- Split pqdoc into `PowerQueryDocumenter.Core` (analysis, model, export, output, configuration) and the console app
  (`Program.cs`, the command line, logging setup). Tests reference Core, and the console app where they test
  command-line parsing (`InternalsVisibleTo`, as vbadoc does).
- Acceptance: no behaviour change; all tests pass.

### Step 3d: pqdoc page on vbadoc's design

- Rebuild pqdoc's HTML page on vbadoc's page design (section 3.10): tokens, theme switch, CSP, system fonts, top
  bar with `pqdoc <version> (<commit>)` linking to the repository, favicon in the same style as vbadoc's.
- Keep pqdoc's features (layered layout, upstream/downstream tracing, details panel, search, `/` and Esc keys).
- Build it on the same page structure as vbadoc's: the same top bar and tabs, and views that plug into a
  `window.pqdoc` object shaped like `window.vbadoc` (`select`, `selection`, `onSelectionChange`, `theme`,
  `onThemeChange`, `activeView`, `onViewChange`; see the top of vbadoc's `page.js`). The aim is that a later
  combined workbook page (step 4) can host each tool's views as tabs without rewriting them. Don't build a shared
  page library yet; just keep the structure and names parallel so one can be extracted later.
- Re-validate the root / leaf / intermediate / cycle colours with the dataviz palette validator, next to vbadoc's
  kind colours.
- Add `PowerQueryDocumenter.BrowserTests` (Playwright), modelled on vbadoc's.
- Look at screenshots in both themes, not just test results.

### Step 3e: Build workflow and Dependabot

This comes last so that the package changes in steps 2–3d are finished and don't compete with update pull requests.
It is also a chance to learn Dependabot, so explain each piece while setting it up.

Dependabot is GitHub's built-in service for keeping dependencies up to date. It has two parts: **version updates**
raise a pull request when a newer package is released, and **security updates** raise one when a package we use has a
known vulnerability. Dependabot never merges anything itself, and it only proves a pull request is safe if something
builds and tests it, so the build workflow comes first.

- **`.github/workflows/build.yml`**: on pull requests and pushes to `main`, use `windows-latest`, install the SDK
  from `global.json`, run `dotnet build ExcelDocumentationTools.sln -warnaserror` and `dotnet test`. Check first that
  no test needs `private/`, a PlantUML server or anything else that exists only on this machine.
- **`.github/dependabot.yml`**: weekly updates for `nuget` (directory `/`, which finds the root
  `Directory.Packages.props`) and `github-actions`. Group minor and patch updates into one pull request. Leave out
  the `dotnet-sdk` ecosystem, because moving past .NET 9 is a deliberate decision (section 3.4).
- After confirming with the user, turn on Dependabot alerts and security updates in the repository settings.
- When the first Dependabot pull request arrives, go through it with the user: the release notes, the build result,
  any licence change on a major version (section 3.12), then merging.
- Record in `docs/GUIDELINES.md`: how to handle Dependabot pull requests, and that vendored d3 and Mermaid are still
  updated by hand.
- Acceptance: the workflow passes on `main`; Dependabot's first run shows on the repository's Insights → Dependency
  graph → Dependabot tab with no configuration errors.

### Step 4: Later

- pqdoc reads Power Query queries directly from a workbook (the `DataMashup` part), as vbadoc reads VBA, so no
  manual `.m` export is needed.
- Extract a `shared/` library only when both tools have real duplicated code.
- **Reconsider combining the tools into one program** once pqdoc reads workbooks, since both will then take the same
  input. The preferred direction is one executable (name to be decided, e.g. `xldoc`) with a subcommand per tool
  (`xldoc vba <workbook>`, `xldoc pq <workbook>`), each still backed by its own Core library. A later option is a
  command that documents everything in a workbook as one HTML page, with links between the parts (for example VBA
  that refreshes a query linking to that query). Steps 3b and 3c are what keep this cheap: both tools share
  System.CommandLine, options and exit codes, and all logic lives in Core libraries.

## 6. When the plan is finished

After step 3e is reviewed, check that every decision in section 3 is in `docs/GUIDELINES.md` or a tool's
`docs/DECISIONS.md`, then **delete this file** in its own commit. It stays in the repository's history. Step 4 is
not part of finishing: copy it into the relevant tool's `docs/PLAN.md` before deleting.

There are no open questions at present. Add any that come up here, with a suggested answer in brackets, and ask
the user before relying on one.
