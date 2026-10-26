# vbadoc: VBA Export & Documentation Tool

A .NET console app that exports the VBA from a macro-enabled Excel workbook into text files,
so it can be kept in git, and (in progress) generates a single self-contained HTML page that
shows how the project fits together:

- an interactive **dependency graph** (d3.js), at module and member level, and
- a **class diagram** (Mermaid).

It reads the workbook file directly. It does not need Excel installed, doesn't use COM, and doesn't need
the "Trust access to the VBA project object model" setting. It also works while the workbook is open in Excel.

See [Requirements.md](Requirements.md) for the full specification.

## Status

| Stage | | |
|---|---|---|
| 0 | Solution skeleton, vendored d3 and Mermaid | done |
| 1 | Extract VBA from workbooks and export source files | done |
| 2 | Parser: declarations and member line ranges | next |
| 3 | Reference resolution and dependency model | |
| 4 | HTML page and dependency graph | |
| 5 | Class diagram | |
| 6 | UserForm controls | |
| 7 | Polish | |

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Build and test

```bash
dotnet build
dotnet test
```

## Usage

```bash
dotnet run --project src/VbaDocumenter -- <workbook> [--out <folder>] [--embed-source]
```

| Argument | |
|---|---|
| `<workbook>` | `.xlsm`, `.xlsb`, `.xlam`, `.xls` or `.xla` |
| `--out <folder>` | Output folder. Default: a `VBAProject` folder next to the workbook |
| `--embed-source` | Put the module source inside the HTML page (stage 4) |

Exit codes: `0` success, `1` bad arguments, `2` a problem with the workbook (the message explains it),
`3` an unexpected error.

### What it writes

- One file per module: `.bas` (standard module), `.cls` (class, ThisWorkbook and sheets), `.frm` (UserForm).
  Files use the project's code page and CRLF line endings, as the VBA editor's own Export does.
  Classes get the VBA editor's `VERSION 1.0 CLASS` header so they import back as classes.
- A `.gitattributes` that stops git converting those files, unless the folder already has one.

On each run the folder is brought in line with the workbook. Module files (`.bas`, `.cls`, `.frm`) at the top level
of the folder that are no longer in the workbook are deleted, so **don't keep hand-written module files there**.
Other files, such as a README or `.git/`, are never touched. Two workbooks in the same directory would share the
default `VBAProject` folder, so give one of them `--out`.

## Project layout

```
src/VbaDocumenter/          console app (command line, output, exit codes)
src/VbaDocumenter.Core/     all the logic
  Extraction/               reading the VBA project out of the workbook (MS-OVBA)
  Output/                   writing the source files
  Resources/Vendor/         d3 and Mermaid, embedded and inlined into the page
tests/VbaDocumenter.Tests/  xUnit tests
  Fixtures/SampleVba/       synthetic 12-module VBA project
  Fixtures/Workbooks/       real Excel workbooks from Apache POI's test data
```

## Third-party components

| Component | Licence | Use |
|---|---|---|
| [OpenMcdf](https://github.com/openmcdf/openmcdf) 3.3.0 | MPL-2.0 (unmodified NuGet package) | Reads compound files |
| [d3](https://d3js.org) 7.9.0 | ISC | Dependency graph (vendored) |
| [Mermaid](https://mermaid.js.org) 11.17.2 | MIT | Class diagram (vendored) |
| [Apache POI](https://poi.apache.org) test data | Apache-2.0 | Test fixtures only |
