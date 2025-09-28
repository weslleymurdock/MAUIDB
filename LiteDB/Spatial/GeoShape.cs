using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Base class for all supported spatial shapes.
    /// </summary>
    public abstract record GeoShape
    {
        internal abstract BoundingBox GetBoundingBox();
    }

    /// <summary>
    /// Represents a single WGS84 coordinate in degrees.
    /// </summary>
    public sealed record GeoPoint : GeoShape
    {
        public double Lat { get; }

        public double Lon { get; }

        public GeoPoint(double lat, double lon)
        {
            if (double.IsNaN(lat) || lat < -90d || lat > 90d)
            {
                throw new ArgumentOutOfRangeException(nameof(lat), "Latitude must be in the [-90,90] range.");
            }

            if (double.IsNaN(lon) || lon < -180d || lon > 180d)
            {
                throw new ArgumentOutOfRangeException(nameof(lon), "Longitude must be in the [-180,180] range.");
            }

            Lat = lat;
            Lon = GeoMath.NormalizeLongitude(lon);
        }

        internal override BoundingBox GetBoundingBox()
        {
            return new BoundingBox(Lat, Lon, Lat, Lon);
        }
    }

    /// <summary>
    /// Represents a line string built from at least two points.
    /// </summary>
    public sealed record GeoLineString : GeoShape
    {
        public IReadOnlyList<GeoPoint> Points { get; }

        public GeoLineString(IReadOnlyList<GeoPoint> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Count < 2)
            {
                throw new ArgumentException("A line string requires at least two points.", nameof(points));
            }

            Points = new ReadOnlyCollection<GeoPoint>(points.Select(p => p ?? throw new ArgumentException("Null point.", nameof(points))).ToList());
        }

        internal override BoundingBox GetBoundingBox()
        {
            return BoundingBox.FromPoints(Points);
        }
    }

    /// <summary>
    /// Represents a polygon defined by an outer ring and optional holes.
    /// </summary>
    public sealed record GeoPolygon : GeoShape
    {
        public IReadOnlyList<GeoPoint> Outer { get; }

        public IReadOnlyList<IReadOnlyList<GeoPoint>>? Holes { get; }

        public GeoPolygon(IReadOnlyList<GeoPoint> outer, IReadOnlyList<IReadOnlyList<GeoPoint>>? holes = null)
        {
            if (outer == null)
            {
                throw new ArgumentNullException(nameof(outer));
            }

            if (outer.Count < 4)
            {
                throw new ArgumentException("A polygon outer ring must contain at least four points (closed).", nameof(outer));
            }

            ValidateRing(outer, nameof(outer));

            if (holes != null)
            {
                var normalized = new List<IReadOnlyList<GeoPoint>>(holes.Count);

                foreach (var hole in holes)
                {
                    if (hole == null)
                    {
                        throw new ArgumentException("Hole cannot be null.", nameof(holes));
                    }

                    if (hole.Count < 4)
                    {
                        throw new ArgumentException("Hole rings must contain at least four points (closed).", nameof(holes));
                    }

                    ValidateRing(hole, nameof(holes));
                    normalized.Add(new ReadOnlyCollection<GeoPoint>(hole.ToList()));
                }

                Holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(normalized);
            }

            Outer = new ReadOnlyCollection<GeoPoint>(outer.ToList());
        }

        private static void ValidateRing(IReadOnlyList<GeoPoint> points, string argumentName)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] == null)
                {
                    throw new ArgumentException("Polygon rings cannot contain null points.", argumentName);
                }
            }

            if (!points[0].Equals(points[points.Count - 1]))
            {
                throw new ArgumentException("Polygon rings must be closed (first point equals last point).", argumentName);
            }
        }

        internal override BoundingBox GetBoundingBox()
        {
            var points = new List<GeoPoint>(Outer.Count + (Holes?.Sum(h => h.Count) ?? 0));
            points.AddRange(Outer);

            if (Holes != null)
            {
                foreach (var hole in Holes)
                {
                    points.AddRange(hole);
                }
            }

            return BoundingBox.FromPoints(points);
        }
    }
}
