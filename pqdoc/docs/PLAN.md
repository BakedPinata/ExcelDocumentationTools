# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a pause for
review. It is pushed after the review, and then the next one starts.

**Current state:** version 1.0.0 and stages 3b to 3d are done. Since then pqdoc has been built in steps of the feedback
plan (closed on 2026-11-13; it's in the repository's history at the tag `feedback-plan-final`), which the work log
lists. **Next: the [generalisation plan](../../GENERALISATION-PLAN.md)**, which works across every tool.

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

- **Show where a query loads to** (sheet, table, data model or connection only). Leave out values that change on
  refresh, such as `FillLastUpdated` and `FillCount`, or every refresh changes the page.
- **Read Power BI files.**
- **Bring a query's group along** when importing, merging it into the target's groups. Each query comes in with no group.
- **Queries in add-ins.** Not asked for with vbadoc's add-ins, and not tried in Excel.
- Ideas that involve more than one tool, such as combining them into one program, are in the
  [generalisation plan's Later section](../../GENERALISATION-PLAN.md#9-later).
