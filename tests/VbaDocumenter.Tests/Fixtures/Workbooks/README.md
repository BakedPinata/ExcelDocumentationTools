# Real workbook fixtures

Workbooks saved by Excel, taken from the Apache POI project's test data
(<https://github.com/apache/poi/tree/trunk/test-data/spreadsheet>, Apache License 2.0).
They let the extraction tests run against real Excel output, not only files these tests build themselves.
The tests only parse them; they are never opened in Office.

| File | Original POI name | Why it's here |
|---|---|---|
| `SimpleMacro.xlsm` | same | Zip-based workbook; one standard module plus document modules |
| `SimpleMacro.xls` | same | The same project in a legacy compound file (`_VBA_PROJECT_CUR`) |
| `SquareMacro.xls` | same | Legacy compound file with a named standard module |
| `45431.xlsm` | same | Code page 950 (Traditional Chinese, Big5) |
| `suffix-generator.xlsm` | `0-www-crossref-org.lib.rivier.edu_education-files_suffix-generator.xlsm` | Code page 10000 (Mac Roman) |
| `57181.xlsm` | same | German document module names, and `é` (byte 0xE9) in code page 1252 |
| `protected_passtika.xlsb` | same | Encrypted workbook (`EncryptedPackage`) |
| `xlmmacro.xlsm` | same | Macro-enabled but with no VBA project (old XLM macros only) |

SHA-256 when downloaded (2026-10-26):

```
b3518c4e22f19e14781999fa74f9c849c8548d757a3c5c3d7c6b995371dbc94c  45431.xlsm
2cf7459faa9d15c1deef4c410fc05142c4f57ebee622668e2526d7b3e34e08fa  57181.xlsm
316aafa78433238ea38ecd39be718f3b64a81f54f95fa942a1038104ead99c5e  protected_passtika.xlsb
0e92c9bb018abd8a5f9121d65827c9e3bd280777219cb77a2efd70635143c00a  SimpleMacro.xls
f76c986f4ebc25c2cc57c088b2511a1269f4bd61d6223a2ab58db351da348ba6  SimpleMacro.xlsm
3fe72d16a8ffb870e47a5a6724329516661f5d7c1f6d98ee8e3bda20e2edd4a9  SquareMacro.xls
8108853394e1ab8964f13d0ee81c122ebabd0c5587a9dfeb934340e7a8d333f1  suffix-generator.xlsm
e9ac8d566a43e3c3b6949e7de538705211b22af3fa1d147762d6ff1383df8092  xlmmacro.xlsm
```

None of these contain a UserForm, so forms are still tested only with synthetic data.
