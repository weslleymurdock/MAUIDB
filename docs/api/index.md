---
title: LiteDB API reference
summary: Discover namespaces, classes, and members that make up the LiteDB embedded database engine.
---

# LiteDB API reference

The API reference is generated from the LiteDB assemblies using DocFX. It complements the conceptual guides by providing detailed documentation for every public namespace, class, member, and extension method.

## How the reference is organized

- **Namespaces** outline the structure of the engine and surface areas such as `LiteDB`, `LiteDB.Engine`, and supporting modules.
- **Types** list constructors, properties, methods, and events with inherited members and extension methods grouped together.
- **Cross-references** let you jump from API topics back to relevant articles using the related content section at the bottom of each page.

## Updating the reference

Run the metadata build locally whenever public APIs change:

```bash
dotnet tool restore
dotnet tool run docfx metadata docs/docfx.json
```

The command refreshes the YAML files under `docs/api/` that the site build consumes.
