# Vendored third-party scripts

These files are copied unchanged from the npm package below and embedded into
`VbaDocumenter.Core.dll`. The generated HTML page inlines them, so it never loads
anything from the network. The licence file is written into the page as a comment
directly above the script it belongs to.

| File | Package | Licence | SHA-256 |
|---|---|---|---|
| `d3.min.js` | d3@7.9.0 (`dist/d3.min.js`) | ISC (`d3.LICENSE.txt`) | `f2094bbf6141b359722c4fe454eb6c4b0f0e42cc10cc7af921fc158fceb86539` |

The tarball was checked against the `dist.integrity` hash published by the npm registry. Mermaid was vendored here
until vbadoc 0.7.0, when the class diagram moved to d3.

## Upgrading

1. Download the new tarball from `https://registry.npmjs.org/<package>/-/<package>-<version>.tgz`.
2. Replace the `.js` and licence files, and update the table above.
3. Update the version constants in `VendoredScripts.cs`.
4. Run `dotnet test`. `VendoredScriptsTests` checks that each file is safe to inline in a `<script>` tag.
