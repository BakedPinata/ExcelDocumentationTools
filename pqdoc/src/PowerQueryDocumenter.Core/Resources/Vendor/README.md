# Vendored third-party scripts

This script is copied unchanged from the npm package below and embedded into `PowerQueryDocumenter.Core.dll`.
The generated HTML page inlines it, so it never loads anything from the network.

| File | Package | Licence | SHA-256 |
|---|---|---|---|
| `d3.min.js` | d3@7.9.0 (`dist/d3.min.js`) | ISC (`d3.LICENSE.txt`) | `f2094bbf6141b359722c4fe454eb6c4b0f0e42cc10cc7af921fc158fceb86539` |

It is byte-for-byte the same file as vbadoc's copy.

## Upgrading

1. Download the new tarball from `https://registry.npmjs.org/d3/-/d3-<version>.tgz`.
2. Replace the `.js` and licence files, and update the table above.
3. Update the version in `PowerQueryDocumenter.Core.csproj` and `HtmlExporter.cs`.
4. Run `dotnet test`.
