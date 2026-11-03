# Sample workbooks

Workbooks with Power Query queries, made in Excel for pqdoc's tests (feedback plan step B1). They hold made-up data
only, because this repository is public. What step B1 confirmed about how Excel stores the queries is in
`pqdoc/docs/DECISIONS.md`.

| File | What it's for |
|---|---|
| `Queries.xlsx` | The main sample: 14 queries covering the cases below |
| `Queries.xlsb` | The same workbook saved as a binary workbook, to confirm it stores the queries the same way. Its Power Query data is byte-identical to the `.xlsx`'s |

## Queries

| Query | What it covers |
|---|---|
| `Companies` | An inline table; loaded to the data model only; in the Reference group |
| `Sales Data` | A name with a space; loaded to a sheet; a block comment on its last lines |
| `fnClean` | A function, used by `Chain_C` and `Manual Targets` |
| `pRegion` | A parameter (`meta [IsParameterQuery = true]`), used by `Chain_B` |
| `Chain_A` to `Chain_E` | A chain, each using the one before (`Chain_A` uses `Sales Data`). A to D are in the Staging group. `Chain_E` has a description, loads to a sheet, and has a line comment on its last line |
| `Sales/Returns` | A name with `/`, which Windows doesn't allow in file names; loaded to a sheet |
| `Discount%` | A name with `%`; loaded to the data model; in the Reference group |
| `Manual Targets` | An Enter Data table (compressed in the M itself); loaded to a sheet; in the Reference group |
| `Notes` | A text value containing `;` and a line starting `shared` |
| `Commented` | Line and block comments throughout, including right after `=` and on the last line |

Every query that isn't loaded to a sheet or the data model is connection only.

## Changing them

Keep the data made up, and remove personal information before committing: in File Explorer, Properties > Details >
Remove Properties and Personal Information. Excel also stores the folder a workbook was saved in, and loading a query to
the data model stores a temporary folder path, so save from a folder whose path you don't mind being public. If you
change the queries, update this README and the tests that use them.
