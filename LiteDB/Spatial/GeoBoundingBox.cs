using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial
{
    internal readonly struct GeoBoundingBox
    {
        public double MinLat { get; }

        public double MinLon { get; }

        public double MaxLat { get; }

        public double MaxLon { get; }

        public GeoBoundingBox(double minLat, double minLon, double maxLat, double maxLon)
        {
            MinLat = minLat;
            MinLon = minLon;
            MaxLat = maxLat;
            MaxLon = maxLon;
        }

        public static GeoBoundingBox FromPoints(IEnumerable<GeoPoint> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var enumerable = points.ToList();

            if (enumerable.Count == 0)
            {
                throw new ArgumentException("Bounding box requires at least one point", nameof(points));
            }

            var minLat = enumerable.Min(p => p.Lat);
            var maxLat = enumerable.Max(p => p.Lat);

            var lons = enumerable.Select(p => p.Lon).ToList();
            var minLon = lons.Min();
            var maxLon = lons.Max();

            return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        }

        public double[] ToArray()
        {
            return new[] { MinLat, MinLon, MaxLat, MaxLon };
        }

        public bool Contains(GeoPoint point)
        {
            return point.Lat >= MinLat && point.Lat <= MaxLat && IsLongitudeWithin(point.Lon);
        }

        public bool Intersects(GeoBoundingBox other)
        {
            return !(other.MinLat > MaxLat || other.MaxLat < MinLat) && LongitudesOverlap(other);
        }

        private bool LongitudesOverlap(GeoBoundingBox other)
        {
            var lonRange = new LongitudeRange(MinLon, MaxLon);
            var otherRange = new LongitudeRange(other.MinLon, other.MaxLon);
            return lonRange.Intersects(otherRange);
        }

        private bool IsLongitudeWithin(double lon)
        {
            var lonRange = new LongitudeRange(MinLon, MaxLon);
            return lonRange.Contains(lon);
        }
    }
}
