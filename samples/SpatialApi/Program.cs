using System.Linq;
using LiteDB;
using LiteDB.Spatial;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var database = new LiteDatabase("Filename=spatial-sample.db;Connection=shared");
var places = database.GetCollection<Place>("places");

Spatial.EnsurePointIndex(places, x => x.Location);

if (places.Count() == 0)
{
    var seed = SeedPlaces();
    places.Insert(seed);
}

app.MapGet("/places/near", (double lat, double lon, double radius, int? limit) =>
{
    var center = new GeoPoint(lat, lon);
    var results = Spatial.Near(places, x => x.Location, center, radius, limit);

    return results.Select(place => new
    {
        place.Name,
        Latitude = place.Location.Lat,
        Longitude = place.Location.Lon
    });
});

app.MapGet("/places/within", (double minLat, double minLon, double maxLat, double maxLon) =>
{
    return Spatial.WithinBoundingBox(places, x => x.Location, minLat, minLon, maxLat, maxLon)
        .Select(place => new
        {
            place.Name,
            Latitude = place.Location.Lat,
            Longitude = place.Location.Lon
        });
});

app.Lifetime.ApplicationStopping.Register(() => database.Dispose());

app.Run();

static IEnumerable<Place> SeedPlaces()
{
    var random = new Random(1234);
    var center = new GeoPoint(48.2082, 16.3738);

    for (var i = 0; i < 200; i++)
    {
        var latOffset = (random.NextDouble() - 0.5) * 0.6;
        var lonOffset = (random.NextDouble() - 0.5) * 0.6;

        yield return new Place
        {
            Name = $"Place {i + 1}",
            Location = new GeoPoint(center.Lat + latOffset, center.Lon + lonOffset)
        };
    }
}

public class Place
{
    public ObjectId Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GeoPoint Location { get; set; } = new GeoPoint(0, 0);

    internal long _gh { get; set; }

    internal double[] _mbb { get; set; } = Array.Empty<double>();
}
