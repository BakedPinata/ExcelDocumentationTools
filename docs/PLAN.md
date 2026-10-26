# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a push,
and a pause for review before the next one starts.

**Current state:** stages 0–2 are done. **Next: stage 3.**

| Stage | Scope | Status |
|---|---|---|
| 0 | Solution skeleton; vendored d3 and Mermaid; SampleVba fixture | Done |
| 1 | Extraction and export: workbook reader, MS-OVBA decompression, `dir`/`PROJECT` parsing, source file writer (code page, CRLF, class header, `.gitattributes`, stale-file deletion), CLI | Done |
| 2 | Parser: logical lines (continuations, `:` separators, comments, `Attribute` lines including those inside procedures), declarations (procedures with visibility, parameters and return types; `Const`, `Enum`, `Type`, module variables incl. `WithEvents`, `Event`, `Declare`, `Implements`), member line ranges. Add the `SampleVbaExtras` fixture. Console summary of modules and members (`--verbose` lists members) | Done |
| 3 | Reference resolution and dependency model: Uses / Implements / Events edges at module and member level; type-aware `obj.Member` resolution (locals, parameters, module variables, public globals, `Me`, module-qualified names, `With` blocks); late-bound count; unreferenced members. Console report; tests assert the expected edge list for SampleVba. Starting point: `Resolver.cs` in the VbaSequenceProbe spike | Next |
| 4 | HTML page and dependency graph: tabs, light/dark theme, d3 graph at both levels, direct/transitive highlighting, focus, legend; source panel (load-folder and `--embed-source` modes) with VBA highlighter. Headless browser tests (Playwright + Chromium) start here | |
| 5 | Class diagram: Mermaid generation, click handlers on the rendered SVG, focus mode, selection kept across tabs, theme re-render, large-project stress test (~300 synthetic modules) | |
| 6 | UserForms: read controls from the designer storage (MS-OFORMS), link handlers to controls, warn instead of failing on unreadable designer data | |
| 7 | Polish: whole-output determinism test, README, third-party notices | |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).
