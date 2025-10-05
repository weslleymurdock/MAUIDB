# Spatial Indexing Quickstart

This guide walks through enabling LiteDB's spatial helpers on a collection, running indexed radius searches, and wiring the query into a minimal HTTP endpoint.

## 1. Configure the collection

```csharp
using LiteDB;
using LiteDB.Spatial;

var db = new LiteDatabase("Filename=places.db;Mode=Shared");
var places = db.GetCollection<Place>("places");

// Compute `_gh` (Morton hash) + `_mbb` (bounding box) automatically
Spatial.Spatial.EnsurePointIndex(places, p => p.Location);

// Optional: tune defaults once for the lifetime of the process
Spatial.Spatial.Options.DefaultIndexPrecisionBits = 48;
Spatial.Spatial.Options.BoundingBoxPaddingMeters = 25;
```

The call to `EnsurePointIndex` now records the precision used inside the `_spatial_indexes` metadata collection so subsequent sessions reuse the same settings automatically.

## 2. Insert data

```csharp
places.Insert(new []
{
    new Place("Central Park", new GeoPoint(40.7829, -73.9654)),
    new Place("Times Square", new GeoPoint(40.7580, -73.9855)),
    new Place("Brooklyn Museum", new GeoPoint(40.6712, -73.9636))
});
```

## 3. Run an indexed radius search

```csharp
var center = new GeoPoint(40.7580, -73.9855);
var results = Spatial.Spatial.Near(places, p => p.Location, center, radiusMeters: 2_000)
    .Select(p => new
    {
        p.Name,
        Distance = GeoMath.DistanceMeters(center, p.Location) / 1000d
    })
    .ToList();
```

The helper now converts the circle into `_gh` Morton windows and `_mbb` checks before falling back to geometry evaluation, avoiding a full collection scan.

## 4. Query from LINQ

```csharp
var linqResults = places
    .Query()
    .Where(p => SpatialExpressions.Near(p.Location, center, 2_000))
    .ToList();
```

`SpatialExpressions` translate into the new `$near`, `$within`, and `$intersects` expression operators so the query planner reuses the same index windows.

## 5. Minimal HTTP sample

```csharp
using LiteDB;
using LiteDB.Spatial;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new LiteDatabase("Filename=places.db;Mode=Shared"));

var app = builder.Build();

app.MapGet("/places/near", (LiteDatabase db, double lat, double lon, double radius) =>
{
    var collection = db.GetCollection<Place>("places");
    Spatial.Spatial.EnsurePointIndex(collection, p => p.Location);

    var center = new GeoPoint(lat, lon);
    return Spatial.Spatial.Near(collection, p => p.Location, center, radius)
        .Select(p => new { p.Name, p.Location.Lat, p.Location.Lon });
});

app.Run();
```

Start the API with `dotnet run` and invoke `GET /places/near?lat=40.758&lon=-73.9855&radius=2000` to retrieve the nearest entries sorted by distance.

## 6. Inspect benchmark output

Run the new BenchmarkDotNet suite to compare the indexed query with a brute-force scan:

```bash
dotnet run -c Release --project LiteDB.Benchmarks --filter "*SpatialNearBenchmark*"
```

## Sample entity

```csharp
public record Place(string Name, GeoPoint Location)
{
    internal long _gh { get; init; }
    internal double[] _mbb { get; init; } = Array.Empty<double>();
}
```

For more details see `docs/spatial-roadmap.md` for progress tracking and additional design notes.
