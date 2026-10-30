# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a pause for
review. It is pushed after the review, and then the next one starts.

**Current state:** version 1.0.0 and stages 3b to 3d are done. No stage is scheduled next; see [Later](#later).

Stages 3b to 3d came from the plan that brought the tools into one repository and aligned them. That plan,
`MONOREPO-PLAN.md`, is in the repository's history.

| Stage | Scope | Status |
|---|---|---|
| 1.0.0 | Reads a folder of `.m` files; M tokeniser and reference extraction; dependency graph with query roles and cycle detection; CSV, PlantUML source, rendered PlantUML and interactive HTML exports; System.CommandLine and Serilog; overwrite policy and atomic writes; exit codes | Done |
| 3b | `--output` replaces `--output-folder`; `--version` shows the build stamp | Done |
| 3c | Split into `PowerQueryDocumenter.Core` and the console app, with no behaviour change | Done |
| 3d | Page rebuilt on vbadoc's design and keeping pqdoc's features: tokens, theme switch, Content-Security-Policy, system fonts, top bar, favicon, views on `window.pqdoc`; colours validated; `PowerQueryDocumenter.BrowserTests` | Done |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).

## Later

- **Read queries directly from a workbook** (the `DataMashup` part), as vbadoc reads VBA, so no manual `.m` export is
  needed.
- **A shared library** goes in `shared/` only when both tools have real duplicated code. Reading workbooks directly is
  the likely reason to revisit this, because both tools would then need the same Open Packaging Conventions reading code.
- **Reconsider combining the tools into one program** once pqdoc reads workbooks, since both will then take the same
  input. The preferred direction is one executable (name to be decided, for example `xldoc`) with a subcommand per tool
  (`xldoc vba <workbook>`, `xldoc pq <workbook>`), each still backed by its own Core library. A later option is a command
  that documents everything in a workbook as one HTML page, with links between the parts (for example, VBA that refreshes
  a query linking to that query). Sharing System.CommandLine, options and exit codes, and keeping all the logic in Core
  libraries, is what keeps this cheap.
