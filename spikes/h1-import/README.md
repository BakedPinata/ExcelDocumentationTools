# H1 spike: writing modules and queries into workbooks

Step H1 of the feedback plan (`FEEDBACK-PLAN.md`): can vbadoc and pqdoc write chosen modules and queries straight into a
workbook that Excel accepts, without Excel? This is throwaway code, kept out of the tools and their solutions. Its
findings go in each tool's `docs/DECISIONS.md`, and it's deleted once H2 starts.

```bash
dotnet run --project spikes/h1-import                     # writes the workbooks below into spikes/h1-import/out/
dotnet run --project spikes/h1-import -- dump <workbook>  # prints a workbook's VBA project, to compare with Excel's
```

It reads the files in `inputs/`, which are shaped like the bundle H2 would write (`.bas` and `.cls` as vbadoc and the
VBA editor export them, `.m` as pqdoc writes them), writes each workbook, and reads it back with vbadoc's and pqdoc's
own readers, which must find every module and query word for word. Whether Excel accepts them is for Excel to say.

## How it writes

**VBA** (`VbaProjectWriter`): source only, with no compiled code. MS-OVBA's own rule for writers does it: the
`_VBA_PROJECT` stream holds only its 7-byte header, with version `0xFFFF`, so Excel can't use any compiled code it finds
and compiles every module from source on opening (the `__SRP_` caches go too). Each new module is a stream of its
compressed source at offset 0, a set of `dir` records, a line in `PROJECT` and an entry in `PROJECTwm`. In an existing
project everything else is kept: other modules, references, protection. A workbook without a project gets a new one,
with Excel's two references (OLE Automation and Office), and a document module for the workbook and each sheet, tied to
them by code names in `workbook.xml` and the sheets; an `.xlsx` becomes macro-enabled (and is named `.xlsm`). An
exported class loses its `VERSION 1.0 CLASS` block and gets back the attributes an export leaves out (`VB_Base`,
`VB_TemplateDerived`, `VB_Customizable`), as Excel stores them.

**Power Query** (`Mashup`): each query's M goes into `Formulas/Section1.m` in the DataMashup's package, gets a
metadata item as a connection-only query, and a connection in `xl/connections.xml`, as Excel has for every query. A
workbook without queries gets a new DataMashup part. The permissions are kept, but the Permission Bindings can't be
remade: they're a check of the package protected with the Windows user's own key (DPAPI). An existing one is kept,
which no longer matches; a new mashup has none. MS-QDEFF says Excel then ignores the permissions.

**Checked by the spike itself:** compression round-trips through vbadoc's decompressor; the CMG, DPB and GC values a
new project needs decrypt back to "not protected", "no password" and "visible", with the key worked out from the
project's ID as Excel's own do (`SimpleMacro.xlsm`'s confirm the key covers the ID with its braces).

## What to check in Excel

Open each workbook from `out/`. A message that Excel "found a problem with some content", or that it repaired
anything, is a failure: please note what it says. Then, for the workbooks with VBA (V, B):

1. **Enable Content** (macros).
2. **Alt+F11**: are SpikeTools, SpikeCounter and SpikeDefaults there? **Debug > Compile VBAProject**: any error?
3. **Alt+F8 > SpikeHello > Run**: does it say the modules compiled and ran, SpikeDouble(21) = 42, the counter holds
   5, and show "café, Zürich" correctly?
4. **Save** (in the same format), close, open again: is everything still there, and does SpikeHello still run?

And for the workbooks with queries (P, B):

1. **Data > Queries & Connections**: are SpikeBase, fnSpikeDouble and Spike Doubled there (with the workbook's own
   queries, where it had some)?
2. Right-click **Spike Doubled > Load To… > Table**: a table of Apples, Pears and Café, with Doubled 2, 4 and 6?
3. **Data > Refresh All**: any error, or a question about privacy levels?
4. **Save**, close, open again: are they still there? Does a query open in the Power Query editor?

| Workbook | Starts from | Adds | Also check | Home | Work |
|---|---|---|---|---|---|
| `V1-SimpleMacro-plus-modules.xlsm` | An `.xlsm` made by Excel, with a module | The three modules | TestMacro (Module1) still runs | | |
| `V2-SimpleMacro-Module1-replaced.xlsm` | The same | Module1 replaced, and the three modules | TestMacro now says Module1 was replaced | | |
| `V3-Queries-xlsx-plus-modules.xlsm` | `Queries.xlsx`, no VBA | A new project with the three modules | The workbook's own queries still refresh | | |
| `V4-new-workbook-modules.xlsm` | Nothing: built by the spike | A new project with the three modules | Ctrl+Alt+F9: B1 shows 42 | | |
| `V5-Queries-xlsb-plus-modules.xlsb` | `Queries.xlsb`, no VBA | A new project, but no document modules (an `.xlsb`'s code names are binary) | Does Excel add ThisWorkbook and the sheets' modules itself? | | |
| `P1-Queries-plus-queries.xlsx` | `Queries.xlsx`, 14 queries | The three queries, with connections | The workbook's own queries still refresh | | |
| `P2-Queries-plus-queries-no-connections.xlsx` | The same | The three queries, without connections | Are they listed at all? | | |
| `P3-SimpleMacro-plus-queries.xlsm` | An `.xlsm` without queries | A new DataMashup with the three queries | TestMacro still runs | | |
| `P4-new-workbook-queries.xlsx` | Nothing: built by the spike | A new DataMashup with the three queries | | | |
| `P5-Queries-xlsb-plus-queries.xlsb` | `Queries.xlsb`, 14 queries | The three queries, without connections (an `.xlsb`'s are binary) | Are they listed at all? | | |
| `B1-new-workbook-modules-and-queries.xlsm` | Nothing: built by the spike | Both | Ctrl+Alt+F9: B1 shows 42 | | |

On a work machine, note too what macro security does with them: a workbook written by a tool is unsigned, so policies
that allow only signed macros, or macros from trusted locations, should block it.
