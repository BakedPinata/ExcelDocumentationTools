# ExcelDocumentationTools

Command-line tools that document what's inside Excel workbooks. Each tool has its own folder, solution, version
and README, and they all follow the same conventions.

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

## Licence

[MIT](LICENSE), covering every tool. Third-party components keep their own licences, listed in each tool's README.
