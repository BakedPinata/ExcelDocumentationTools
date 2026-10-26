# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a push,
and a pause for review before the next one starts.

**Current state:** stages 0–3 and 4a are done. **Next: stage 4b.**

| Stage | Scope | Status |
|---|---|---|
| 0 | Solution skeleton; vendored d3 and Mermaid; SampleVba fixture | Done |
| 1 | Extraction and export: workbook reader, MS-OVBA decompression, `dir`/`PROJECT` parsing, source file writer (code page, CRLF, class header, `.gitattributes`, stale-file deletion), CLI | Done |
| 2 | Parser: logical lines (continuations, `:` separators, comments, `Attribute` lines including those inside procedures), declarations (procedures with visibility, parameters and return types; `Const`, `Enum`, `Type`, module variables incl. `WithEvents`, `Event`, `Declare`, `Implements`), member line ranges. Add the `SampleVbaExtras` fixture. Console summary of modules and members (`--verbose` lists members) | Done |
| 3 | Reference resolution and dependency model: Uses / Implements / Events edges at module and member level; type-aware `obj.Member` resolution (locals, parameters, module variables, public globals, `Me`, module-qualified names, `With` blocks); late-bound count; unreferenced members. Console report; tests assert the expected edge list for SampleVba | Done |
| 4a | HTML page: tabs, light/dark theme (system default, remembered switch), search, source panel (load-folder and `--embed-source` modes, member-only or whole-module view, role/unreferenced/late-bound details), VBA highlighter, Content-Security-Policy blocking all network access. Headless browser tests (Playwright + Chromium) | Done |
| 4b | Dependency graph (d3) at module and member level: owning module obvious, three edge types with legend, click to highlight dependencies and dependents (direct/transitive toggle), background click clears, zoom/pan, Focus button, member-kind filters, selection shared with the source panel and other tab. Views plug into `window.vbadoc` (see the top of `page.js`) | Next |
| 5 | Class diagram: Mermaid generation, click handlers on the rendered SVG, focus mode, selection kept across tabs, theme re-render, large-project stress test (~300 synthetic modules) | |
| 6 | UserForms: read controls from the designer storage (MS-OFORMS), link handlers to controls, warn instead of failing on unreadable designer data | |
| 7 | Polish: whole-output determinism test, README, third-party notices | |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).
