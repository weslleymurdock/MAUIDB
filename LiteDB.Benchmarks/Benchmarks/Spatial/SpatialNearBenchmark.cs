using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LiteDB.Benchmarks.Models;
using LiteDB.Spatial;
using LiteDB;

namespace LiteDB.Benchmarks.Benchmarks.Spatial
{
    [BenchmarkCategory(Constants.Categories.SPATIAL)]
    public class SpatialNearBenchmark : BenchmarkBase
    {
        private ILiteCollection<SpatialPlace> _collection = null!;
        private GeoPoint _center = new GeoPoint(48.2082, 16.3738);
        private int _originalPrecision;

        [GlobalSetup]
        public void GlobalSetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _collection = DatabaseInstance.GetCollection<SpatialPlace>("places");

            _originalPrecision = Spatial.Spatial.Options.DefaultIndexPrecisionBits;
            Spatial.Spatial.Options.DefaultIndexPrecisionBits = 48;
            Spatial.Spatial.EnsurePointIndex(_collection, x => x.Location);

            var random = new Random(42);
            var data = new List<SpatialPlace>(DatasetSize);

            for (var i = 0; i < DatasetSize; i++)
            {
                var lat = 48.0 + random.NextDouble() * 0.5;
                var lon = 16.0 + random.NextDouble() * 0.5;

                data.Add(new SpatialPlace
                {
                    Name = $"Place_{i}",
                    Location = new GeoPoint(lat, lon)
                });
            }

            _collection.Insert(data);
            DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public List<SpatialPlace> ManualScan()
        {
            var results = new List<SpatialPlace>();

            foreach (var item in _collection.FindAll())
            {
                var distance = GeoMath.DistanceMeters(item.Location, _center, Spatial.Spatial.Options.Distance);
                if (distance <= 5_000)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        [Benchmark]
        public List<SpatialPlace> IndexedNear()
        {
            return Spatial.Spatial.Near(_collection, x => x.Location, _center, 5_000).ToList();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Spatial.Spatial.Options.DefaultIndexPrecisionBits = _originalPrecision;
            DatabaseInstance?.Dispose();
            DatabaseInstance = null;
            File.Delete(DatabasePath);
        }

        public class SpatialPlace
        {
            public ObjectId Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public GeoPoint Location { get; set; } = new GeoPoint(0, 0);

            internal long _gh { get; set; }

            internal double[] _mbb { get; set; } = Array.Empty<double>();
        }
    }
}
