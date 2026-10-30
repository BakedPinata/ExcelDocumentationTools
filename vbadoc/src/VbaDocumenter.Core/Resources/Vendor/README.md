# Vendored third-party scripts

These files are copied unchanged from the npm packages below and embedded into
`VbaDocumenter.Core.dll`. The generated HTML page inlines them, so it never loads
anything from the network. Each licence file is written into the page as a comment
directly above the script it belongs to.

| File | Package | Licence | SHA-256 |
|---|---|---|---|
| `d3.min.js` | d3@7.9.0 (`dist/d3.min.js`) | ISC (`d3.LICENSE.txt`) | `f2094bbf6141b359722c4fe454eb6c4b0f0e42cc10cc7af921fc158fceb86539` |
| `mermaid.min.js` | mermaid@11.17.2 (`dist/mermaid.min.js`) | MIT (`mermaid.LICENSE.txt`) | `581ed7d74bd9048d0e3a91363927d72ef22942d7722546b27f7cc29e35390eb8` |

Both tarballs were checked against the `dist.integrity` hash published by the npm registry.

## Upgrading

1. Download the new tarball from `https://registry.npmjs.org/<package>/-/<package>-<version>.tgz`.
2. Replace the `.js` and licence files, and update the table above.
3. Update the version constants in `VendoredScripts.cs`.
4. Run `dotnet test`. `VendoredScriptsTests` checks that each file is safe to inline in a `<script>` tag.
