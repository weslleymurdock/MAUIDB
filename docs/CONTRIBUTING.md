---
title: Contributing to the LiteDB documentation
summary: Learn how to build the DocFX site locally and follow the style conventions for conceptual and API content.
---

# Contributing to the LiteDB documentation

Thank you for helping keep the LiteDB docs accurate and approachable. This guide walks through the tooling you need, how to preview changes, and the conventions we follow when writing new articles.

## Prerequisites

- [.NET SDK 8.0 or later](https://dotnet.microsoft.com/en-us/download) – required for restoring the DocFX tool and for building the LiteDB projects that produce XML documentation.
- [Git](https://git-scm.com/) – for cloning and branching.

## Install DocFX

The repository ships with a local tool manifest. Restore the tools once per clone:

```bash
dotnet tool restore
```

The command installs DocFX (used for documentation generation) and GitVersion (used elsewhere in the repository).

## Build the site locally

1. Ensure the LiteDB project builds so that XML documentation files are up to date:

   ```bash
   dotnet build LiteDB/LiteDB.csproj -c Release
   ```

2. Regenerate API metadata (only needed when public APIs change):

   ```bash
   dotnet tool run docfx metadata docs/docfx.json
   ```

3. Build the static site output:

   ```bash
   dotnet tool run docfx build docs/docfx.json
   ```

4. Preview the site locally:

   ```bash
   dotnet tool run docfx serve docs/_site
   ```

   DocFX starts a local web server (default: `http://localhost:8080`) with live reload support.

## Authoring guidelines

- **Front matter** – Start each article with YAML front matter that sets a `title` (and optionally `summary`). This powers navigation labels and search results.
- **Headings** – Use sentence case for headings (`## Configure connection strings`), and keep only one `#` heading per file.
- **Links** – Use relative DocFX links with the `~/` prefix when pointing to other docs (e.g., `~/articles/queries/index.md`). Links to API members can use `xref` syntax such as `<xref:LiteDB.LiteDatabase.Insert>`.
- **Code samples** – Prefer fenced code blocks with a language hint (` ```csharp `). Keep samples runnable when possible.
- **Alerts** – Use Markdig alerts for tips and caveats (e.g., `> [!NOTE]`).

## Pull request checklist

- Run `dotnet tool run docfx build docs/docfx.json` and address any warnings or broken links.
- Include screenshots if you change the look and feel of the site (navigation, branding, etc.).
- Update the relevant `toc.yml` file when you add or rename articles.
- Keep commit messages concise and in present tense.

By following these steps, contributors can confidently iterate on the documentation while keeping the DocFX build healthy.
