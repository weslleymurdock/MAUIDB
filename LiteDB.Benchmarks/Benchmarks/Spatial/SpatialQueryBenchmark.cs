using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LiteDB;
using LiteDB.Benchmarks.Models;
using LiteDB.Spatial;
using SpatialApi = LiteDB.Spatial.Spatial;

namespace LiteDB.Benchmarks.Benchmarks.Spatial
{
    [BenchmarkCategory(Constants.Categories.QUERIES, Constants.Categories.SPATIAL)]
    public class SpatialQueryBenchmark : BenchmarkBase
    {
        private ILiteCollection<GeoPlace> _places = null!;
        private ILiteCollection<GeoRegion> _regions = null!;
        private GeoPoint _nearCenter = null!;
        private double _nearRadius;
        private GeoBoundingBox _boundingBox;
        private GeoPolygon _queryPolygon = null!;

        [GlobalSetup]
        public void Setup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _places = DatabaseInstance.GetCollection<GeoPlace>("places");
            _regions = DatabaseInstance.GetCollection<GeoRegion>("regions");

            _nearCenter = new GeoPoint(48.2082, 16.3738);
            _nearRadius = 1_500d;

            SpatialApi.EnsurePointIndex(_places, x => x.Location);
            SpatialApi.EnsureShapeIndex(_regions, x => x.Area);

            var places = new List<GeoPlace>(DatasetSize);
            var regions = new List<GeoRegion>(DatasetSize);
            var random = new Random(2024);

            for (var i = 0; i < DatasetSize; i++)
            {
                var latOffset = (random.NextDouble() - 0.5) * 0.6;
                var lonOffset = (random.NextDouble() - 0.5) * 0.6;
                var location = new GeoPoint(_nearCenter.Lat + latOffset, _nearCenter.Lon + lonOffset);

                places.Add(new GeoPlace
                {
                    Name = $"P{i}",
                    Location = location
                });

                regions.Add(CreateRegion(location, 0.02 + random.NextDouble() * 0.02, i));
            }

            _places.Insert(places);
            _regions.Insert(regions);

            DatabaseInstance.Checkpoint();

            _boundingBox = GeoMath.BoundingBoxForCircle(_nearCenter, _nearRadius * 1.5d);
            _queryPolygon = CreateRegion(_nearCenter, 0.15d, -1).Area;
        }

        [Benchmark(Baseline = true)]
        public int NearByMorton()
        {
            return SpatialApi.Near(_places, x => x.Location, _nearCenter, _nearRadius).Count();
        }

        [Benchmark]
        public int WithinBoundingBox()
        {
            return SpatialApi.WithinBoundingBox(_places, x => x.Location, _boundingBox.MinLat, _boundingBox.MinLon, _boundingBox.MaxLat, _boundingBox.MaxLon).Count();
        }

        [Benchmark]
        public int IntersectsPolygon()
        {
            return SpatialApi.Intersects(_regions, x => x.Area, _queryPolygon).Count();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            DatabaseInstance?.Checkpoint();
            DatabaseInstance?.Dispose();
            DatabaseInstance = null;

            File.Delete(DatabasePath);
        }

        private static GeoRegion CreateRegion(GeoPoint center, double sizeDegrees, int index)
        {
            var half = sizeDegrees / 2d;

            var points = new[]
            {
                new GeoPoint(center.Lat + half, center.Lon - half),
                new GeoPoint(center.Lat + half, center.Lon + half),
                new GeoPoint(center.Lat - half, center.Lon + half),
                new GeoPoint(center.Lat - half, center.Lon - half),
                new GeoPoint(center.Lat + half, center.Lon - half)
            };

            return new GeoRegion
            {
                Name = index >= 0 ? $"R{index}" : "Query",
                Area = new GeoPolygon(points)
            };
        }
    }
}
