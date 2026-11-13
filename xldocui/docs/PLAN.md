# Build plan

The app was built in steps of the feedback plan, phase I (in the repository's history, at the tag
`feedback-plan-final`). Each ended with passing tests, a commit and a pause for review.

**Current state:** version 0.3.0, with the Document and Import tabs, which finishes phase I, and add-ins in the Import
tab's file pickers (step J2). **Next:** the [generalisation plan](../../GENERALISATION-PLAN.md), which renames the app
and widens what it runs.

| Step | Scope | Status |
|---|---|---|
| I1 | Spike: a window published as one file, run on a work machine | Done |
| I2 | Both tools' command lines as libraries another program can run | Done |
| I3 | The app, with the Document tab: every option of both tools' main commands, the command shown with Copy, the log, Cancel, pages opened, settings remembered | Done |
| I4 | The Import tab: the source, what to take from a list or a manifest, where it goes; Preview; the tools' clash questions in dialogs | Done |
| J2 | The Import tab's file pickers offer add-ins when vbadoc is writing (phase J, added after I4) | Done |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).
