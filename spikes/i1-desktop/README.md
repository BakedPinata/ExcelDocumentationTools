# I1 spike: a desktop window that runs where the tools run

Step I1 of the feedback plan (`FEEDBACK-PLAN.md`): can a Windows Forms program, published as one self-contained file, run
on a locked-down work machine? This is throwaway code, kept out of the tools and their solutions and left out of
`publish.ps1`. It's deleted once the question is answered.

The window has what the real one needs and nothing else: a file picker, a log pane, and a Run button shaped like a real
run, with work off the window's thread, a five-answer question asked on it, and a Cancel that uses a token. As it opens,
it reports how big it is, how quickly it started, and whether anything was unpacked.

## Running it on a work machine

```bash
dotnet publish spikes/i1-desktop -c Release -p:PublishProfile=FolderProfile
```

That uses the tools' own publish settings and puts `xldocui-spike.exe` in the folder the tools are published to. Run it
from there, then:

1. **Check it opens.** If application control stops it, note the message and where it was run from.
2. **Press Browse…** and choose a workbook.
3. **Press Run** and answer the question, which shows five buttons.
4. **Press Run, then Cancel** straight away. The log should say `Cancelled.`
5. **Press Report, then Copy the log,** and keep the numbers. The report has the machine's paths in it, so it isn't for
   committing.

To time a smaller build as well, add `-p:EnableCompressionInSingleFile=true -p:PublishDir=<another folder>` to the publish.

A failed publish is a finding too. The tools only need .NET's runtime pack, but this also needs
`Microsoft.WindowsDesktop.App.Runtime.win-x64` from NuGet.

## What was found at home (2026-11-08, .NET 10.0.14)

The home machine is Arm, so this `win-x64` build ran under emulation. Its size and what it unpacks will be the same on a
work machine, but its times won't.

| | As the tools publish | With compression |
|---|---|---|
| Size | 110.7 MB | 49.3 MB |
| Start to window, first run | about 0.6 s | about 0.5 s |
| Start to window, after that | about 0.26 s | about 0.34 s |

For comparison, `vbadoc.exe` is 71.8 MB.

- **Beside the executable:** only its `.pdb`, as with the tools. Windows Forms needs no loose native libraries.
- **Unpacked:** nothing. The base directory is the executable's own folder, there's no `.net` folder in the temporary
  folder, and no module is loaded from outside Windows.
- **One file does appear in the temporary folder,** with a random name, and is deleted at once. It's only there when
  `EnableVisualStyles` is called. It's almost certainly the theming manifest Windows Forms carries as a resource
  (`System.Windows.Forms.XPThemes.manifest`), written out because Windows needs a file to read it from. It went too quickly
  to read. It isn't program code to be run, so application control shouldn't care, but the work machine is where
  that's tested.
- **Driven through UI Automation:** the file picker returned the chosen path, all five answers appeared and the chosen one
  came back to the worker thread, and Cancel stopped a run part way.

**If the window opens but looks wrong or the temporary file is refused,** the next thing to try is declaring common
controls in an application manifest rather than calling `EnableVisualStyles`, which needs no file written.
