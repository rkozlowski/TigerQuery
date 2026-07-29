# Local TigerQuery documentation generation

This folder contains the DocFX configuration and conceptual pages for the
TigerQuery documentation site. DocFX extracts the public API from the three
published library projects and combines it with these pages.

## Included assemblies

- `ItTiger.TigerQuery`
- `ItTiger.TigerQuery.Core`
- `ItTiger.TigerQuery.CliCore`

`ItTiger.TigerSqlCmd`, tests, and internal implementation types are not included.

## Build locally

From the repository root:

```powershell
dotnet tool restore
dotnet restore TigerQuery.sln
dotnet build TigerQuery.sln -c Release
dotnet docfx docs/api-docfx/docfx.json
```

The last command generates API metadata in `docs/api-docfx/api/` and the site
in `docs/api-docfx/_site/`. Both directories are generated and ignored; do not
edit or commit them.

To build and serve the site locally:

```powershell
dotnet docfx docs/api-docfx/docfx.json --serve
```

Then open <http://localhost:8080>.

Invalid cross-reference warnings usually point to an unresolved `<see cref>`
in a source XML comment. Fix the source documentation rather than generated
files.
