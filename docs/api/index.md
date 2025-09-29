---
title: API Reference
---

# API Reference

The API reference is generated from the LiteDB assemblies and published alongside the conceptual articles. Each namespace and type includes the XML documentation summaries that ship with the NuGet packages.

To regenerate the reference locally:

1. Restore the .NET tool manifest with `dotnet tool restore`.
2. Build the site with `dotnet tool run docfx build docs/docfx.json`.
3. Browse the generated output in `docs/_site/api/` or run `dotnet tool run docfx serve docs/docfx.json` for a live preview.

> [!TIP]
> Use the search box or the namespace tree to navigate between database engine, mapper, and shell APIs.
