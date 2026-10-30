# Design decisions

Decisions made while building the tool that aren't spelled out in [Requirements.md](../Requirements.md),
or that change it. Add to this file whenever a new decision is agreed. Conventions shared with the other tools are in
[GUIDELINES.md](../../docs/GUIDELINES.md).

## Platform and libraries

- **The first version is 1.0.0.** The tool's earlier form came from a Claude chat and isn't counted, so the docs
  describe the tool as it is, not how it differs from that.
- **.NET 9**, the same as vbadoc. The target framework is in the root `Directory.Build.props`.
- **Central Package Management.** Every NuGet version is in the root `Directory.Packages.props`.
- **Shared project settings** are in the root `Directory.Build.props`. `pqdoc/Directory.Build.props` imports it and sets
  `Version`, `Product` and `ToolRoot`.
- **`--version` shows only the version for now.** The `.csproj` turns off the commit in the informational version.
  Stage 3b removes that, so pqdoc shows the same build stamp as vbadoc.
- **The `.env` reader is hand-written** rather than a package.
- **An environment variable or `.env` file, not .NET user secrets**, configures the integration tests. The tool already
  reads both, they work the same way locally and in a CI pipeline, and they need no extra packages.
- **The PlantUML user agent is `pqdoc/<version>`**, with the version read from the assembly, so it can't drift.
- **d3 7.9.0** is vendored byte-for-byte in `Resources/Vendor/`, and is the same file as vbadoc's copy.

## Analysis

- **A tokeniser and a light structural pass, not a full M parser.** The tokeniser separates identifiers from comments
  and text literals. The structural pass then ignores `let` step names, record field names, function parameters
  (including `each`'s `_`) and any use of them within their scope, and field access and projection. This is a
  heuristic: unusual code can still produce a missed or extra dependency. `--verbose` logs the references found in
  each query.
- **Malformed M never throws.** An unterminated comment, text literal or quoted identifier runs to the end of the input.
- **Only identifiers that match a known query count as references**, so built-in functions and keywords can't produce a
  false dependency and no filter of built-in names is needed. That is what lets a query be named `Source` or `Table`.
- **Dotted names are one identifier.** `Table.SelectRows` doesn't match a query named `Table`.
- **Lower-case M type names in type positions** (`as table`, `type [A = text]`) only match a query spelled exactly the
  same way.
- **Query names are case-insensitive**, so a file whose name differs from another's only by case is ignored with a
  warning.
- **A file that can't be read doesn't stop the run.** Its query stays in the graph, because other queries may reference
  it, but its own references are missing. Outputs are still produced, and the exit code is 3.

## Output

- **The default output name is `powerquery-dependencies`**, because it describes what the file contains.
- **`--output-name` is checked against the characters Windows forbids, on every platform**, so the same name works
  everywhere.
- **The CSV is UTF-8 with a byte order mark**, which Excel relies on.
- **PlantUML aliases are `q1`, `q2`, … in name order**, with the real name as the label, so a name can't break the diagram.
- **There is deliberately no default PlantUML server.** The diagram includes query names, which must never reach a
  third-party service unless the user chooses that.
- **Rendering uses `GET` with the encoded diagram**, or `POST` with the plain text if the URL would be longer than 2,000
  characters. Each request times out after 30 seconds.
- **A rendered response gets a light signature check**, so an HTML error page served with status 200 isn't saved as a
  diagram.
- **Every output path is checked before anything is written**, so a refused overwrite never leaves a partial set of
  outputs.
- **Files are written to a temporary file and then moved into place**, so a failure or Ctrl+C never leaves a truncated file.
- **Overwrite prompts go to standard error**, so standard output stays clean.

## Page

- **Queries sit in columns by depth**, the longest chain of queries that feed them, so data flows left to right. d3's
  force layout runs within those columns. Isolated queries sit in their own column on the far left, and the queries
  of a circular dependency share a column, which keeps the depth calculation finite.
- **Placeholders in the template are filled in a single pass**, so text inserted for one is never scanned for another.
- **The page follows the system theme**, with its own fonts and colours, until stage 3d moves it to vbadoc's design.

## Tests

- **Integration tests need a PlantUML server** and skip themselves unless `PLANTUML_SERVER_URL` is set in the
  environment or in a `.env` file in `pqdoc/`. They are in the `Integration` category.
- **Two tests skip on this kind of machine:** one needs a case-sensitive file system, and one needs permission to
  create a symbolic link.
