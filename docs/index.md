---
title: LiteDB documentation
description: Explore conceptual guides and API reference material for the LiteDB embedded NoSQL database.
---

# LiteDB documentation

LiteDB v5 is a single-file, serverless NoSQL database for .NET. It stores JSON-like documents, offers LINQ-friendly queries, and is designed to be fast, lightweight, and easy to embed in desktop, web, or mobile applications.

## Explore the docs

Use the following guides to dive into LiteDB's feature set:

| Section | Summary |
| --- | --- |
| [Getting started](articles/getting-started/index.md) | Install LiteDB, connect to a database, and perform basic CRUD operations with strongly typed collections. |
| [Model your data](articles/data-structure/index.md) | Learn how LiteDB organizes documents internally and how collections persist data on disk. |
| [Work with collections](articles/collections/index.md) | Manage identity keys, typed mappings, and dynamic documents. |
| [Query and index](articles/queries/index.md) | Build filters, projection pipelines, and efficient indexes for your workloads. |
| [Configure the engine](articles/connection-string/index.md) | Tune the database with connection string options, pragmas, and collation settings. |
| [Secure and extend](articles/encryption/index.md) | Protect data files, handle concurrency, and integrate file storage. |
| [Architecture](articles/how-litedb-works/index.md) | Dive into the engine design, including the page allocator, WAL file, and recovery process. |
| [Repository pattern](articles/repository-pattern/index.md) | Wrap LiteDB with repositories for domain-driven designs and testability. |
| [Release notes](articles/changelog/index.md) | Track changes and version history for each LiteDB release. |

## API reference

Generated API reference documentation is available in the [LiteDB API section](api/index.md). Use it alongside the conceptual guides above to cross-reference namespaces, types, and members.

## Contributing to the docs

See [Contributing](CONTRIBUTING.md) for instructions on building the site locally with DocFX and for authoring conventions that keep the documentation consistent.

---

*Made with ♥ by the LiteDB team – [@mbdavid](https://twitter.com/mbdavid) – MIT License.*
