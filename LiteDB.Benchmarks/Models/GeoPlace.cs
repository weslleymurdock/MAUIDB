using System;
using LiteDB.Spatial;

namespace LiteDB.Benchmarks.Models
{
    public class GeoPlace
    {
        public ObjectId Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public GeoPoint Location { get; set; } = new GeoPoint(0, 0);

        internal long _gh { get; set; }

        internal double[] _mbb { get; set; } = Array.Empty<double>();
    }
}
