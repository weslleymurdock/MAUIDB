# Contributing to the documentation

LiteDB's documentation site is generated with [DocFX](https://dotnet.github.io/docfx/). The conceptual guides live in `docs/articles/` and the API reference is produced directly from the LiteDB assemblies. Use the following workflow when authoring or reviewing docs:

## Prerequisites

- Install the .NET 8 SDK (DocFX runs as a local .NET tool).
- Restore the repository's tool manifest once per clone:

  ```bash
  dotnet tool restore
  ```

## Local authoring

1. Edit or add markdown files in `docs/articles/`.
2. Regenerate the API metadata and build the site:

   ```bash
   dotnet tool run docfx metadata docs/docfx.json
   dotnet tool run docfx build docs/docfx.json
   ```

   The generated site will be available under `docs/_site/`.
3. Preview your changes in a browser:

   ```bash
   dotnet tool run docfx serve docs/docfx.json
   ```

   DocFX serves the static site on <http://localhost:8080> by default and watches for file changes.
4. Commit conceptual changes along with any updated API reference files (DocFX writes YAML outputs to `docs/api/`).

## Style conventions

- Each markdown file begins with YAML front matter specifying a `title`.
- Use DocFX cross-reference links (`~/articles/...`) instead of relative `../` paths.
- Prefer fenced code blocks with a language hint so syntax highlighting works in the generated site.
- Re-run `docfx metadata` followed by `docfx build` before submitting a pull request to catch broken links or metadata warnings.

## Continuous integration

The GitHub Actions CI pipeline restores the DocFX tool and runs both `docfx metadata docs/docfx.json` and `docfx build docs/docfx.json`. If a step fails, review the workflow logs for detailed warnings or errors.
