# H1 spike: writing modules and queries into workbooks

Step H1 of the feedback plan (`FEEDBACK-PLAN.md`): can vbadoc and pqdoc write chosen modules and queries straight into a
workbook that Excel accepts, without Excel? This is throwaway code, kept out of the tools and their solutions. Its
findings go in each tool's `docs/DECISIONS.md`, and it's deleted once H3 and H4 have taken what they need.

```bash
dotnet run --project spikes/h1-import                        # writes the workbooks below into spikes/h1-import/out/
dotnet run --project spikes/h1-import -- inspect "<workbook>"  # what a workbook is: labels, encryption, VBA, queries
dotnet run --project spikes/h1-import -- add "<workbook>"    # a copy of it with the modules and queries added
dotnet run --project spikes/h1-import -- copy "<source>" --modules A,B --new "<file>"  # modules, what they use, references
dotnet run --project spikes/h1-import -- dump "<workbook>"   # its VBA project, to compare with Excel's
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

## Results so far

The user tried the V, P and B workbooks below at home and on a work machine, and they all worked (2026-11-06):

- **V5:** Excel added ThisWorkbook and the sheets' modules itself, so an `.xlsb` needs no code names from us.
- **P2 and P5:** the queries were listed without any connections.
- **A label:** every workbook built or changed here that had no label asked for one at work, as any unlabelled
  workbook does. A real workbook saved at work had a metadata-only label: a copy made with `add` opened with no repair
  message and the same label, its VBA and queries worked, and it saved without asking for a label.

- **Library references:** R4's plain procedure failed with "Can't find project or library": a reference to a library
  that isn't installed stops the whole project compiling, even code that needs no library. R0 to R3 behaved as expected.
  Their own modules, with what they depend on, copied across and worked. Not every clash was tried
  (a module or a dependency already in the target), which H3 and H4 test.

## Trying library references

The plan assumed references couldn't come along, but they're only records in the project's `dir` stream. So the spike
copies them from the source project as they stand, whatever their kind, skipping any the target has (known by the GUID
in the libid). It copies all of the source's, because a module's text doesn't say which library each name comes from.

In `out/`, for each of these: **Enable Content**, **Debug > Compile VBAProject**, and **Tools > References**.

| Workbook | What it is | What to expect |
|---|---|---|
| `R0-source-with-reference.xlsm` | A source: SpikeLibrary needs the Microsoft Scripting Runtime, and the reference is written here | Compiles; **SpikeReferences** says the dictionary holds 2 items; the runtime is ticked in References |
| `R1-new-workbook-reference-copied.xlsm` | SpikeLibrary copied out of R0 into a new workbook, with its reference | The same |
| `R2-SimpleMacro-reference-copied.xlsm` | The same into an existing project, which has its own references | The same, TestMacro still runs, and no reference appears twice |
| `R3-reference-left-out.xlsm` | The same with the reference left out, to show the test means something | **Compile error** on `Scripting.Dictionary`: "User-defined type not defined" |
| `R4-reference-not-installed.xlsm` | SpikePlain, which needs no library, with a reference to one that isn't installed | The question: is it marked MISSING? Does the project still compile? Does **SpikeNoLibrary** run? |

Then the test that counts, on your own modules. `copy` takes the modules you name, follows what they use with vbadoc's
analyser, and writes them with the source's references into a new workbook, or a copy of one:

```bash
dotnet run --project spikes/h1-import -- copy "C:\path\to\Source.xlsm" --modules Logger,FilePathFromDialog --new "C:\path\to\Toolkit.xlsm"
dotnet run --project spikes/h1-import -- copy "C:\path\to\Source.xlsm" --modules Logger --into "C:\path\to\Target.xlsx"
```

It says what it took, what came along, what stayed behind (document modules and forms), and the references the result
has. `--no-references` leaves them out. Neither the source nor the target is changed: `--into` writes `Target-spike.xlsm`
beside the target. Its output names your modules and libraries, unlike `inspect`'s. Then in Excel: does the project
compile, and do the modules work?

## Trying a workbook of your own, with a sensitivity label

A label comes in two kinds, and which one a workbook has decides everything:

- **A label without encryption is only metadata:** `docMetadata/LabelInfo.xml`, or `MSIP_Label_` properties in
  `docProps/custom.xml`. The file stays an ordinary zip, so the tools can change a copy, and the label should come
  through as long as those parts do. Nothing in them checks the rest of the file.
- **A label with encryption wraps the whole file** in a compound file with an `EncryptedPackage` stream, which only
  Excel can open. Nothing outside Excel can read or change it, so vbadoc and pqdoc refuse it already, and so does this.

A throwaway workbook is the best start: make one at work, apply the label, add a sheet or two, and save it. Every
label your work uses may differ, so `inspect` one workbook of each. Then, from the repository root:

```bash
dotnet run --project spikes/h1-import -- inspect "C:\path\to\Labelled.xlsx"
dotnet run --project spikes/h1-import -- add "C:\path\to\Labelled.xlsx"
```

`inspect` says which kind of label it is. It prints counts and yes or no, never label names, ids or anything in the
workbook, so it's safe to paste into a chat. `add` only reads the workbook, and writes a copy beside it, named
`Labelled-spike.xlsm` (`.xlsx` becomes `.xlsm`, as it gains macros; `--output <folder>` puts it elsewhere). It reads the
copy back and says which parts changed, ending with whether the label's parts came through byte for byte. Use
`--vba-only` or `--queries-only` to add one or the other. The copy holds what the original holds, so treat it the same.

Then open the copy in Excel:

1. Does it open with no message about repairing, and **no request to choose a label**?
2. Is the **same label showing** (Home > Sensitivity)?
3. Work through the lists below: SpikeHello runs, and the queries load and refresh.
4. Change a cell and **save**. Does it save without asking for a label, or complain about the label?
5. Close it and run `inspect` on it again: is the label still there, and is it still not encrypted?

If `inspect` says the workbook is encrypted, that label can't work with the tools at all, whichever way they import.

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

| Workbook | Starts from | Adds | Also check |
|---|---|---|---|
| `V1-SimpleMacro-plus-modules.xlsm` | An `.xlsm` made by Excel, with a module | The three modules | TestMacro (Module1) still runs |
| `V2-SimpleMacro-Module1-replaced.xlsm` | The same | Module1 replaced, and the three modules | TestMacro now says Module1 was replaced |
| `V3-Queries-xlsx-plus-modules.xlsm` | `Queries.xlsx`, no VBA | A new project with the three modules | The workbook's own queries still refresh |
| `V4-new-workbook-modules.xlsm` | Nothing: built by the spike | A new project with the three modules | Ctrl+Alt+F9: B1 shows 42 |
| `V5-Queries-xlsb-plus-modules.xlsb` | `Queries.xlsb`, no VBA | A new project, but no document modules (an `.xlsb`'s code names are binary) | Does Excel add ThisWorkbook and the sheets' modules itself? |
| `P1-Queries-plus-queries.xlsx` | `Queries.xlsx`, 14 queries | The three queries, with connections | The workbook's own queries still refresh |
| `P2-Queries-plus-queries-no-connections.xlsx` | The same | The three queries, without connections | Are they listed at all? |
| `P3-SimpleMacro-plus-queries.xlsm` | An `.xlsm` without queries | A new DataMashup with the three queries | TestMacro still runs |
| `P4-new-workbook-queries.xlsx` | Nothing: built by the spike | A new DataMashup with the three queries | |
| `P5-Queries-xlsb-plus-queries.xlsb` | `Queries.xlsb`, 14 queries | The three queries, without connections (an `.xlsb`'s are binary) | Are they listed at all? |
| `B1-new-workbook-modules-and-queries.xlsm` | Nothing: built by the spike | Both | Ctrl+Alt+F9: B1 shows 42 |

On a work machine, note too what macro security does with them: a workbook written by a tool is unsigned, so policies
that allow only signed macros, or macros from trusted locations, should block it.
