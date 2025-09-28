using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using LiteDB.Spatial;
using Xunit;
using SpatialApi = LiteDB.Spatial.Spatial;

namespace LiteDB.Tests.Geo
{
    public class SpatialTests
    {
        [Fact]
        public void Near_Returns_Ordered_Within_Radius()
        {
            using var db = new LiteDatabase(":memory:");
            var col = db.GetCollection<Place>("p");

            SpatialApi.EnsurePointIndex(col, x => x.Location);

            var center = new GeoPoint(48.2082, 16.3738);

            var a = new Place { Name = "A", Location = new GeoPoint(48.215, 16.355) };
            var b = new Place { Name = "B", Location = new GeoPoint(48.185, 16.380) };
            var c = new Place { Name = "C", Location = new GeoPoint(48.300, 16.450) };

            col.Insert(new[] { a, b, c });

            var result = SpatialApi.Near(col, x => x.Location, center, 5000).ToList();

            Assert.Equal(new[] { "A", "B" }, result.Select(x => x.Name).ToArray());
        }

        [Fact]
        public void BoundingBox_Crosses_Antimeridian()
        {
            using var db = new LiteDatabase(":memory:");
            var col = db.GetCollection<Place>("p");

            SpatialApi.EnsurePointIndex(col, x => x.Location);

            col.Insert(new Place { Name = "W", Location = new GeoPoint(0, -179.5) });
            col.Insert(new Place { Name = "E", Location = new GeoPoint(0, 179.5) });

            var result = SpatialApi.WithinBoundingBox(col, x => x.Location, -10, 170, 10, -170).ToList();

            Assert.Contains(result, p => p.Name == "W");
            Assert.Contains(result, p => p.Name == "E");
        }

        [Theory]
        [InlineData(89.9, 0, 89.9, 90)]
        [InlineData(-89.9, 0, -89.9, -90)]
        public void GreatCircle_Is_Stable_Near_Poles(double lat1, double lon1, double lat2, double lon2)
        {
            var d = GeoMath.DistanceMeters(new GeoPoint(lat1, lon1), new GeoPoint(lat2, lon2));
            Assert.InRange(d, 0, 20000);
        }

        [Fact]
        public void GeoJson_RoundTrip_Point()
        {
            var p = new GeoPoint(48.2082, 16.3738);
            var json = GeoJson.Serialize(p);
            var back = GeoJson.Deserialize<GeoPoint>(json);

            Assert.Equal(p.Lat, back.Lat, 10);
            Assert.Equal(p.Lon, back.Lon, 10);
        }

        [Fact]
        public void Within_Polygon_Excludes_Hole()
        {
            using var db = new LiteDatabase(":memory:");
            var col = db.GetCollection<Place>("p");

            SpatialApi.EnsureShapeIndex(col, x => x.Location);

            var outer = new[]
            {
                new GeoPoint(48.25, 16.30),
                new GeoPoint(48.25, 16.45),
                new GeoPoint(48.15, 16.45),
                new GeoPoint(48.15, 16.30),
                new GeoPoint(48.25, 16.30),
            };

            var hole = new[]
            {
                new GeoPoint(48.22, 16.36),
                new GeoPoint(48.22, 16.39),
                new GeoPoint(48.18, 16.39),
                new GeoPoint(48.18, 16.36),
                new GeoPoint(48.22, 16.36),
            };

            var poly = new GeoPolygon(outer, new[] { hole });

            var insideOutsideHole = new Place { Name = "Edge", Location = new GeoPoint(48.20, 16.37) };
            var outside = new Place { Name = "Outside", Location = new GeoPoint(48.30, 16.50) };

            col.Insert(new[] { insideOutsideHole, outside });

            var result = SpatialApi.Within(col, x => x.Location, poly).ToList();

            Assert.DoesNotContain(result, p => p.Name == "Edge");
            Assert.DoesNotContain(result, p => p.Name == "Outside");
        }

        [Fact]
        public void GeoHash_Recomputes_On_Update()
        {
            using var db = new LiteDatabase(":memory:");
            var col = db.GetCollection<Place>("p");

            SpatialApi.EnsurePointIndex(col, x => x.Location);

            var pl = new Place { Name = "P", Location = new GeoPoint(48.2, 16.37) };
            col.Insert(pl);

            var h1 = col.FindOne(x => x.Name == "P")._gh;

            pl.Location = new GeoPoint(48.21, 16.38);
            col.Update(pl);

            var h2 = col.FindById(pl.Id)._gh;

            Assert.NotEqual(h1, h2);
        }

        [Theory]
        [InlineData(91, 0)]
        [InlineData(-91, 0)]
        [InlineData(0, 181)]
        [InlineData(0, -181)]
        public void Invalid_Coordinates_Throw(double lat, double lon)
        {
            using var db = new LiteDatabase(":memory:");
            var col = db.GetCollection<Place>("p");

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                col.Insert(new Place { Name = "X", Location = new GeoPoint(lat, lon) }));
        }

        [Fact]
        public void Intersects_Line_Touches_Polygon_Edge()
        {
            using var db = new LiteDatabase(":memory:");
            var roads = db.GetCollection<Road>("roads");

            SpatialApi.EnsureShapeIndex(roads, r => r.Path);

            var line = new GeoLineString(new[]
            {
                new GeoPoint(0.5, -1),
                new GeoPoint(0.5, 2)
            });

            var polygon = new GeoPolygon(new[]
            {
                new GeoPoint(0, 0),
                new GeoPoint(0, 1),
                new GeoPoint(1, 1),
                new GeoPoint(1, 0),
                new GeoPoint(0, 0)
            });

            roads.Insert(new Road { Name = "Main", Path = line });

            var hits = SpatialApi.Intersects(roads, r => r.Path, polygon).ToList();

            Assert.NotEmpty(hits);
        }

        private sealed class Place
        {
            public ObjectId Id { get; set; }

            public string Name { get; set; }

            public GeoPoint Location { get; set; }

            public long _gh { get; set; }

            public double[] _mbb { get; set; }
        }

        private sealed class Road
        {
            public ObjectId Id { get; set; }

            public string Name { get; set; }

            public GeoLineString Path { get; set; }

            public double[] _mbb { get; set; }
        }
    }
}
