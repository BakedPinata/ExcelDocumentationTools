# J1 spike: writing modules into Excel add-ins

Step J1 of the feedback plan (`FEEDBACK-PLAN.md`): can vbadoc write modules into an add-in (`.xlam`) that Excel
accepts? This is throwaway code, kept out of the tools and their solutions. Its findings go in vbadoc's
`docs/DECISIONS.md`, and it's deleted after J2.

```bash
dotnet run --project spikes/j1-addins                          # writes the add-ins below into spikes/j1-addins/out/
dotnet run --project spikes/j1-addins -- inspect "<file>"...   # content type, protection, and the project's streams
dotnet run --project spikes/j1-addins -- compare "<a>" "<b>"   # which of two projects' streams differ
```

It reads the sample add-ins in `vbadoc/tests/VbaDocumenter.Tests/Fixtures/Workbooks/` and writes with vbadoc's own
writer (`TargetWorkbook`), which reads each result back and checks every module word for word. vbadoc won't take an
`.xlam` as a target yet, so the spike gives the writer a copy named `.xlsm`, and puts the add-in's content type back
where the writer changes it, which is only when an add-in gains a project. That's all J2 has to add, if Excel agrees.

## What it found before Excel

- **Reading works.** vbadoc documents `Toolkit.xlam`, `LockedToolkit.xlam` and `Toolkit.xla`, and `import` takes
  modules from each, with the same modules, source, interface and reference as `Toolkit.xlsm`. A locked project's source
  isn't encrypted, so the password isn't needed to read it. `Empty.xlam` has no project, so there's nothing to read.
- **An add-in is an `.xlsm` with another content type.** `Toolkit.xlam` and `Toolkit.xlsm` have the same parts, and
  their `workbook.xml` differs only in a co-authoring element. The workbook part's content type is
  `application/vnd.ms-excel.addin.macroEnabled.main+xml` in place of `…sheet.macroEnabled.main+xml`. In the project, only
  the `PROJECT` stream differs: a new project ID, and CMG, DPB and GC encrypted again, as on every save.
- **Locking** sets the project ID to zeros, adds `HelpFile=""`, and changes CMG (protection state `04`), DPB (a password)
  and GC (not visible). A cache stream (`__SRP_0`) changed too. The spike's copy keeps all of that as it was.

## The add-ins it writes

`SpikeCheck` is a new standard module, whose `SpikeHello` shows a message using `SpikePrinter`, a new class that
implements the add-in's own `IStringPrinter` and uses Microsoft Scripting Runtime.

| File | What it is |
|---|---|
| `A1-Toolkit.xlam` | A copy of `Toolkit.xlam` with `SpikeCheck` and `SpikePrinter` added, and `StandardModule` replaced: its `Demo` now shows "StandardModule was replaced by the J1 spike" |
| `A2-LockedToolkit.xlam` | The same, into a copy of `LockedToolkit.xlam`, which should still be locked with the same password |
| `A3-Empty.xlam` | A copy of `Empty.xlam`, which had no VBA, gaining a project: the Toolkit's three modules, `SpikeCheck`, `SpikePrinter` and the Scripting reference |
| `A4-New.xlam` | A new add-in built by the tool from nothing, with the same modules and reference as A3 |
| `A5-FromAddin.xlsm` | A new workbook (not an add-in) with the Toolkit's three modules taken from `Toolkit.xlam`, as `vbadoc import Toolkit.xlam --items CustomStringPrinter,StandardModule --new A5-FromAddin.xlsm` writes it today |

## What to check in Excel

At home and on a work machine. At work, pull the branch and run the spike there (first command above), so the files are
made locally. Close Excel between add-ins, so only one spike add-in is loaded at a time.

For **A1 to A4**, one at a time:

1. **Open it** with File > Open. An add-in has no window, so nothing seems to happen. Enable macros if Excel asks. Does
   it open with no message about repairing?
2. **In the VBA editor** (Alt+F11), is its project listed, with `SpikeCheck`, `SpikePrinter`, `StandardModule`,
   `CustomStringPrinter` and `IStringPrinter`, and is Microsoft Scripting Runtime ticked in Tools > References?
   - **A2 only:** is the project still locked? It should ask for its password, `thequickbrownfox`, when opened. Before
     unlocking it, try `Application.Run "'A2-LockedToolkit.xlam'!SpikeHello"` in the Immediate window (Ctrl+G): does
     the message come up?
3. **Debug > Compile VBAProject.** No message means it compiled.
4. **Run `SpikeHello`** (click inside it in `SpikeCheck`, then F5). The message should give the add-in's name,
   `IsAddin: True`, "Hello from SpikePrinter, written by the J1 spike => From IStringPrinter_GetTheString" and
   "Dictionary items: 3". Then run `Demo` in `StandardModule`: in A1 and A2 it shows the "replaced" message; in A3 and
   A4 it prints "Hello World!" in the Immediate window.
5. **Save and reopen.** Add a comment line to `SpikeCheck`, save from the VBA editor (Ctrl+S), close Excel, and open
   the add-in again. Is the comment there, with every module? A2 should still ask for its password.

Then **install A4 through the Add-ins dialog**, the way an add-in is normally used: File > Options > Add-ins, Manage:
Excel Add-ins > Go > Browse, and choose `A4-New.xlam`. If Excel offers to copy it into its AddIns folder, say No. Tick
it, then restart Excel: is it loaded (listed in the VBA editor), and does `SpikeHello` run? While it's loaded, run the
published vbadoc on it (`vbadoc out\A4-New.xlam`): does it write the page? Afterwards, untick it in the same dialog. At
work, say whether the dialog let you install it at all.

For **A5**, open it, enable content, compile, and run `Demo`, which prints "Hello World!" in the Immediate window.

For each, say what happened at each step, with any message word for word.
