using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial
{
    internal readonly struct BoundingBox
    {
        public double MinLat { get; }

        public double MinLon { get; }

        public double MaxLat { get; }

        public double MaxLon { get; }

        public BoundingBox(double minLat, double minLon, double maxLat, double maxLon)
        {
            MinLat = minLat;
            MinLon = GeoMath.NormalizeLongitude(minLon);
            MaxLat = maxLat;
            MaxLon = GeoMath.NormalizeLongitude(maxLon);
        }

        public static BoundingBox FromPoints(IEnumerable<GeoPoint> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var minLat = double.PositiveInfinity;
            var minLon = double.PositiveInfinity;
            var maxLat = double.NegativeInfinity;
            var maxLon = double.NegativeInfinity;

            foreach (var point in points)
            {
                if (point == null)
                {
                    throw new ArgumentException("Points cannot contain null entries.", nameof(points));
                }

                minLat = Math.Min(minLat, point.Lat);
                maxLat = Math.Max(maxLat, point.Lat);

                minLon = Math.Min(minLon, point.Lon);
                maxLon = Math.Max(maxLon, point.Lon);
            }

            if (double.IsInfinity(minLat) || double.IsInfinity(minLon))
            {
                throw new ArgumentException("At least one point is required.", nameof(points));
            }

            return new BoundingBox(minLat, minLon, maxLat, maxLon);
        }

        public bool Contains(GeoPoint point)
        {
            if (point == null)
            {
                return false;
            }

            if (point.Lat < MinLat || point.Lat > MaxLat)
            {
                return false;
            }

            if (IsLongitudeWrapped())
            {
                return point.Lon >= MinLon || point.Lon <= MaxLon;
            }

            return point.Lon >= MinLon && point.Lon <= MaxLon;
        }

        public bool Intersects(BoundingBox other)
        {
            if (MaxLat < other.MinLat || MinLat > other.MaxLat)
            {
                return false;
            }

            if (IsLongitudeWrapped() || other.IsLongitudeWrapped())
            {
                return IntersectsWrapped(other);
            }

            return !(MaxLon < other.MinLon || MinLon > other.MaxLon);
        }

        private bool IntersectsWrapped(BoundingBox other)
        {
            var segments = ToLongitudeSegments();
            var otherSegments = other.ToLongitudeSegments();

            foreach (var segment in segments)
            {
                foreach (var otherSegment in otherSegments)
                {
                    if (segment.max >= otherSegment.min && otherSegment.max >= segment.min)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private (double min, double max)[] ToLongitudeSegments()
        {
            if (!IsLongitudeWrapped())
            {
            return new (double min, double max)[] { (MinLon, MaxLon) };
        }

            return new (double min, double max)[]
            {
                (MinLon, 180d),
                (-180d, MaxLon)
            };
        }

        private bool IsLongitudeWrapped() => MinLon > MaxLon;
    }
}
