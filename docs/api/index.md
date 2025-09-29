---
title: API Reference
---

# API Reference

DocFX generates the LiteDB API reference from the XML documentation comments that ship with the assemblies. The table below lists the assemblies that are currently published.

| Assembly | Description |
| --- | --- |
| `LiteDB.dll` | Core engine containing the document store, query processor, and storage services. |

## Building the Reference Locally

Run the metadata and site build steps to refresh the API pages after changing public surface area or XML comments:

```bash
# Restore the DocFX CLI (once per clone)
dotnet tool restore

# Rebuild metadata and the static site output
dotnet docfx docs/docfx.json
```

You can preview the result locally with:

```bash
dotnet docfx serve docs/_site
```

The generated YAML files live under `docs/api/` and are excluded from source control because they are regenerated during CI builds.
