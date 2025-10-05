using System;
using LiteDB.Spatial;

namespace LiteDB.Benchmarks.Models
{
    public class GeoRegion
    {
        public ObjectId Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPolygon Area { get; set; } = new GeoPolygon(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 0.1),
            new GeoPoint(0.1, 0.1),
            new GeoPoint(0.1, 0),
            new GeoPoint(0, 0)
        });

        internal double[] _mbb { get; set; } = Array.Empty<double>();
    }
}
