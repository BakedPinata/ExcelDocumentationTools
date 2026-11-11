# ExcelDocumentationTools

Command-line tools that document what's inside Excel workbooks, and a desktop app that runs them without typing their
options. Each has its own folder, solution, version and README, and they all follow the same
[conventions](docs/GUIDELINES.md).

| Tool | Reads | Produces | More |
|---|---|---|---|
| **vbadoc** | A macro-enabled workbook (`.xlsm`, `.xlsb`, `.xlam`, `.xls` or `.xla`) | The VBA as text files, and a self-contained HTML page with a dependency graph and class diagram | [vbadoc/README.md](vbadoc/README.md) |
| **pqdoc** | An Excel workbook (`.xlsx`, `.xlsm` or `.xlsb`), or a folder of exported Power Query (`.m`) files | An interactive HTML graph of the dependencies between queries, and optionally CSV and PlantUML | [pqdoc/README.md](pqdoc/README.md) |
| **xldocui** | A workbook or folder, as both tools do | A window on Windows over both tools: every option, the command it runs with Copy, and the tools' log | [xldocui/README.md](xldocui/README.md) |

Code both tools need, such as opening a workbook and building a page, is kept once in [shared/](shared/README.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Everything works from the `dotnet`
command line. Visual Studio 2022 doesn't support .NET 10, so use the command line there, or Visual Studio 2026 or VS
Code with the C# Dev Kit.

From the repository root:

```
dotnet build ExcelDocumentationTools.sln                      # build every tool
dotnet test --solution ExcelDocumentationTools.sln            # run every test
dotnet test --project vbadoc/tests/VbaDocumenter.Tests        # run one test project
```

`global.json` switches `dotnet test` to the Microsoft Testing Platform, which xUnit v3 needs on .NET 10. So name a
project or solution with `--project` or `--solution`, not as a bare path. A bare `dotnet test` in a tool's folder runs
that tool's tests.

The browser tests download Playwright's Chromium (about 150 MB) on their first run. If the machine runs short of memory
with every test project going at once, run them one project at a time.

To run a tool without publishing it, put its arguments after `--`:

```
dotnet run --project vbadoc/src/VbaDocumenter -- <workbook>
dotnet run --project pqdoc/src/PowerQueryDocumenter -- <folder>
dotnet run --project xldocui/src/ExcelDocumenter.Desktop              # the desktop app
```

Each tool's README has more: its options, and commands to run from its own folder.

## Publishing

To publish every tool in one step, run this from the repository root in PowerShell:

```
.\publish.ps1
```

It runs this for each tool, which you can also run yourself:

```
dotnet publish vbadoc/src/VbaDocumenter -c Release -p:PublishProfile=FolderProfile
dotnet publish pqdoc/src/PowerQueryDocumenter -c Release -p:PublishProfile=FolderProfile
dotnet publish xldocui/src/ExcelDocumenter.Desktop -c Release -p:PublishProfile=FolderProfile
```

Each tool becomes a single self-contained executable for 64-bit Windows, so the machine it runs on needs no .NET
runtime. Publishing the desktop app also needs the Windows desktop runtime pack, which `dotnet publish` gets from
NuGet. They all go in one folder, `%USERPROFILE%\source\Executables\ExcelDocumentationTools\`, so adding that folder
to your `PATH` adds every tool, including any added later.

## Licence

[MIT](LICENSE), covering every tool. Third-party components keep their own licences, listed in each tool's README.
