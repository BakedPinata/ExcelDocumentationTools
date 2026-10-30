# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a pause for
review. It is pushed after the review, and then the next one starts.

**Current state:** version 1.0.0 is done. **Next: stage 3b.**

Stages 3b to 3d are the steps of the same name in [MONOREPO-PLAN.md](../../MONOREPO-PLAN.md), which has the detail.

| Stage | Scope | Status |
|---|---|---|
| 1.0.0 | Reads a folder of `.m` files; M tokeniser and reference extraction; dependency graph with query roles and cycle detection; CSV, PlantUML source, rendered PlantUML and interactive HTML exports; System.CommandLine and Serilog; overwrite policy and atomic writes; exit codes | Done |
| 3b | `--output` replaces `--output-folder`; `--version` shows the build stamp | Next |
| 3c | Split into `PowerQueryDocumenter.Core` and the console app, with no behaviour change | |
| 3d | Page rebuilt on vbadoc's design and keeping pqdoc's features: tokens, theme switch, Content-Security-Policy, system fonts, top bar, favicon, views on `window.pqdoc`; colours validated; `PowerQueryDocumenter.BrowserTests` | |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).
