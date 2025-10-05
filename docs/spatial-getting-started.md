# Spatial Queries & Indexing Guide

LiteDB 6 introduces a spatial subsystem that now supports index-aware execution, queryable operators, and ready-to-run samples. This guide walks through the new capabilities and how to compose them in your applications.

## Configure Spatial Defaults

The global `LiteDB.Spatial.Spatial.Options` object now exposes additional knobs:

- `IndexPrecisionBits` controls the number of Morton hash bits used for point indexes (default `52`).
- `ToleranceDegrees` defines the angular tolerance used when comparing coordinates and bounding boxes (default `1e-9`).
- `Distance` still selects the great-circle formula (Haversine or Vincenty).

```csharp
Spatial.Options.IndexPrecisionBits = 48;
Spatial.Options.ToleranceDegrees = 1e-8;
```

These values are persisted with `_gh` indexes so re-opening the database continues to use the same precision automatically.【F:LiteDB/Spatial/Spatial.cs†L33-L55】【F:LiteDB/Engine/Engine/Index.cs†L14-L120】

## Ensuring Spatial Indexes

`Spatial.EnsurePointIndex` and `Spatial.EnsureShapeIndex` register computed members (`_gh` for Morton hashes and `_mbb` for bounding boxes) and now record metadata alongside the index definition. Index-aware queries reuse this precision when building Morton ranges.【F:LiteDB/Spatial/Spatial.cs†L15-L122】【F:LiteDB/Spatial/SpatialQueryHelper.cs†L1-L97】

```csharp
var places = db.GetCollection<Place>("places");
Spatial.EnsurePointIndex(places, x => x.Location); // precision pulled from Spatial.Options.IndexPrecisionBits
```

## Bson Expression Operators

Three spatial operators are available inside `BsonExpression` strings and the SQL-like API:

- `SPATIAL_INTERSECTS($._mbb, minLat, minLon, maxLat, maxLon)` filters documents whose bounding boxes intersect the query window.
- `SPATIAL_CONTAINS_POINT($._mbb, lat, lon)` checks whether a bounding box encloses a point.
- `SPATIAL_NEAR($._mbb, lat, lon, radiusMeters [, formula])` approximates radius searches using bounding boxes or exact distances for points.【F:LiteDB/Document/Expression/Methods/Spatial.cs†L1-L79】

You can mix these functions with the standard query API:

```csharp
var candidates = places.Query()
    .Where("SPATIAL_INTERSECTS($._mbb, @0, @1, @2, @3)", minLat, minLon, maxLat, maxLon)
    .ToEnumerable();
```

## Index-Aware Spatial Helpers

The high-level helpers (`Near`, `WithinBoundingBox`, `Within`, `Intersects`, `Contains`) now translate shape constraints into `_gh` range scans plus `_mbb` filters. They reuse the registered metadata, deduplicate matches, and apply tolerances before final geometry checks.【F:LiteDB/Spatial/Spatial.cs†L57-L221】【F:LiteDB/Spatial/SpatialQueryHelper.cs†L1-L97】

```csharp
var results = Spatial.Near(
    places,
    x => x.Location,
    new GeoPoint(48.2082, 16.3738),
    radiusMeters: 1500,
    limit: 25);
```

## REST Sample

A minimal API showcasing radius searches and bounding-box filters lives in `samples/SpatialApi`. Run it with:

```bash
dotnet run --project samples/SpatialApi/SpatialApi.csproj
```

It seeds random points, exposes `/places/near` and `/places/within`, and demonstrates how the spatial helpers plug into HTTP handlers.【F:samples/SpatialApi/Program.cs†L1-L71】

## Benchmarks

`LiteDB.Benchmarks` now includes `SpatialQueryBenchmark`, a BenchmarkDotNet suite that measures `Near`, `WithinBoundingBox`, and `Intersects` operations across dataset sizes. Each run seeds random points and polygons to stress the new index-aware pipeline.【F:LiteDB.Benchmarks/Benchmarks/Spatial/SpatialQueryBenchmark.cs†L1-L97】

Invoke the benchmark harness as usual:

```bash
dotnet run -c Release --project LiteDB.Benchmarks
```

This produces allocation and wall-clock measurements for the spatial scenarios next to the existing query suites.

## Next Steps

- Combine spatial expressions with other query predicates (e.g., category filters) to narrow result sets further.
- Explore the `Spatial.Options` tolerance values when working near the anti-meridian or poles.
- Contribute additional samples or benchmarks (e.g., nearest-neighbour paging) as your scenarios evolve.
