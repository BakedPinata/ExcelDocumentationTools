# Build plan

The tool is built in small stages. Each stage ends with passing tests, a commit and a push,
and a pause for review before the next one starts.

**Current state:** stages 0–5 and 5b are done. **Next: stage 5c.**

| Stage | Scope | Status |
|---|---|---|
| 0 | Solution skeleton; vendored d3 and Mermaid; SampleVba fixture | Done |
| 1 | Extraction and export: workbook reader, MS-OVBA decompression, `dir`/`PROJECT` parsing, source file writer (code page, CRLF, class header, `.gitattributes`, stale-file deletion), CLI | Done |
| 2 | Parser: logical lines (continuations, `:` separators, comments, `Attribute` lines including those inside procedures), declarations (procedures with visibility, parameters and return types; `Const`, `Enum`, `Type`, module variables incl. `WithEvents`, `Event`, `Declare`, `Implements`), member line ranges. Add the `SampleVbaExtras` fixture. Console summary of modules and members (`--verbose` lists members) | Done |
| 3 | Reference resolution and dependency model: Uses / Implements / Events edges at module and member level; type-aware `obj.Member` resolution (locals, parameters, module variables, public globals, `Me`, module-qualified names, `With` blocks); late-bound count; unreferenced members. Console report; tests assert the expected edge list for SampleVba | Done |
| 4a | HTML page: tabs, light/dark theme (system default, remembered switch), search, source panel (load-folder and `--embed-source` modes, member-only or whole-module view, role/unreferenced/late-bound details), VBA highlighter, Content-Security-Policy blocking all network access. Headless browser tests (Playwright + Chromium) | Done |
| 4b | Dependency graph (d3) at module and member level: owning module obvious, three edge types with legend, click to highlight dependencies and dependents (direct/transitive toggle), background click clears, zoom/pan, Focus button, member-kind filters, selection shared with the source panel and other tab. Views plug into `window.vbadoc` (see the top of `page.js`). Revised after real-workbook testing: names never overlap and are always shown, drag nodes or whole modules with live rearranging, settings panel | Done |
| 5 | Class diagram: Mermaid generated in the page from `ClassDiagram` data (generated ids, entity escaping), click handlers on the rendered SVG for boxes and member lines, focus mode following the selection, selection shared across tabs, theme re-render, zoom/scroll, Show… options; dependencies auto-hidden over 150 in the full diagram. Large-project tests at 300 modules | Done |
| 5b | Graph legend and Show…: clicking legend entries highlights what they stand for (edge entries keep the nodes they connect lit), narrowing the selection's highlight when both are on; Show… gains module kinds, edge kinds, private members, All / Unreferenced only / Referenced only, and nodes with no connections, at both levels where they apply, remembered per browser | Done |
| 5c | "Uses / Used by" list: a collapsible Dependencies section in the source panel (closed by default), direct links at the selection's level, each entry selects its target | Next |
| 5d | Navigation: a Home node (one per workbook, remembered per browser, starred in the graph); a breadcrumb trail of the route walked from Home (loops collapse, click a step to go back); "Path from Home" highlights the shortest dependency chain; selections update the page address so browser Back/Forward work | |
| 6 | UserForms: read controls from the designer storage (MS-OFORMS), link handlers to controls, warn instead of failing on unreadable designer data | |
| 7 | Polish: whole-output determinism test, README, third-party notices | |

Decisions agreed along the way are in [DECISIONS.md](DECISIONS.md). The specification is [Requirements.md](../Requirements.md).
