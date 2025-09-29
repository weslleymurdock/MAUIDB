---
title: Overview
---

# Overview

LiteDB v5 is a single-file, serverless NoSQL database for .NET. It stores JSON-like documents, offers LINQ-friendly queries, and is designed to be fast, lightweight, and easy to embed in desktop, web, or mobile applications.

## Documentation map

Use the following guides to explore LiteDB's feature set:

- **Start here**
  - [Getting Started](~/articles/getting-started/index.md): Install LiteDB, connect to a database, and perform basic CRUD operations with strongly typed collections.
  - [How LiteDB Works](~/articles/how-litedb-works/index.md): Dive into the storage engine, the page allocator, journaling, and the recovery process.

- **Modeling data**
  - [Collections](~/articles/collections/index.md): Manage collections, control identity keys, and work with typed and dynamic documents.
  - [BsonDocument](~/articles/bsondocument/index.md): Work directly with `BsonDocument` when you need dynamic schemas or advanced document manipulation.
  - [Object Mapping](~/articles/object-mapping/index.md): Configure the mapper that turns your POCO classes into BSON documents, including custom type conversions and attributes.
  - [Repository Pattern](~/articles/repository-pattern/index.md): Wrap LiteDB with repositories for domain-driven designs and testability.

- **Querying and indexes**
  - [Queries](~/articles/queries/index.md): Explore the query API, including typed queries, aggregation, and filtering across indexes.
  - [Indexes](~/articles/indexes/index.md): Accelerate queries with single-field, compound, and expression-based indexes, including multikey support.
  - [Expressions](~/articles/expressions/index.md): Compose expressions to filter, project, and update documents with LiteDB's JSON-path-inspired language.

- **Configuration and operations**
  - [Connection String](~/articles/connection-string/index.md): Configure the LiteDB engine with connection string options such as file paths, timeouts, and journaling.
  - [Pragmas](~/articles/pragmas/index.md): Tune engine behavior at runtime with pragmas such as size limits, timeouts, and default collations.
  - [Collation](~/articles/collation/index.md): Control string comparison rules by customizing collations per database.
  - [Concurrency](~/articles/concurrency/index.md): Understand how LiteDB handles locking, transactions, and multi-threaded access.

- **Advanced scenarios**
  - [Data Structure](~/articles/data-structure/index.md): Learn how LiteDB organizes data internally, including pages, collections, and how documents are persisted on disk.
  - [Encryption](~/articles/encryption/index.md): Protect data files using password-based AES encryption and understand how keys are derived.
  - [DbRef](~/articles/dbref/index.md): Model relationships across collections with references while keeping documents denormalized when appropriate.
  - [FileStorage](~/articles/filestorage/index.md): Store and stream files alongside your documents with the GridFS-inspired FileStorage API.

- **Project lifecycle**
  - [Versioning](~/articles/versioning/index.md): See how LiteDB evolves, how semantic versioning applies, and where to find upgrade notes.
  - [Changelog](~/articles/changelog/index.md): Review notable changes across releases.

To dive into the API surface area, start with the [API reference](~/api/index.md) generated from the latest LiteDB assemblies.

---

*Made with ♥ by the LiteDB team – [@mbdavid](https://twitter.com/mbdavid) – MIT License.*
