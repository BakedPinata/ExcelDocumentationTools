# ExcelDocumentationTools

Command-line tools that document what's inside Excel workbooks. Each tool has its own folder, solution, version
and README, and they all follow the same [conventions](docs/GUIDELINES.md).

| Tool | Reads | Produces | More |
|---|---|---|---|
| **vbadoc** | A macro-enabled workbook (`.xlsm`, `.xlsb`, `.xlam`, `.xls` or `.xla`) | The VBA as text files, and a self-contained HTML page with a dependency graph and class diagram | [vbadoc/README.md](vbadoc/README.md) |
| **pqdoc** | A folder of exported Power Query (`.m`) files | The dependencies between queries as CSV, PlantUML and an interactive HTML graph | [pqdoc/README.md](pqdoc/README.md) |

## Building

You need the .NET 9 SDK. To build every tool from the repository root:

```
dotnet build ExcelDocumentationTools.sln
```

To work on one tool, run `dotnet build` and `dotnet test` in its folder.

## Publishing

To publish every tool in one step, run this from the repository root in PowerShell 7:

```
.\publish.ps1
```

Each tool becomes a single self-contained executable for 64-bit Windows, so it needs no .NET runtime. They all go in
one folder, `%USERPROFILE%\source\Executables\ExcelDocumentationTools\`, so adding that folder to your `PATH` adds every
tool, including any added later.

## Licence

[MIT](LICENSE), covering every tool. Third-party components keep their own licences, listed in each tool's README.
