using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Spatial
{
    internal readonly struct SpatialQuerySegment
    {
        public GeoBoundingBox BoundingBox { get; }

        public (long Start, long End)? MortonRange { get; }

        public SpatialQuerySegment(GeoBoundingBox boundingBox, (long Start, long End)? mortonRange)
        {
            BoundingBox = boundingBox;
            MortonRange = mortonRange;
        }
    }

    internal static class SpatialQueryHelper
    {
        public static IEnumerable<SpatialQuerySegment> CreateSegments(GeoBoundingBox boundingBox, int? precisionBits)
        {
            if (precisionBits.HasValue && (precisionBits.Value <= 0 || precisionBits.Value > 60))
            {
                throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be between 1 and 60 bits");
            }

            foreach (var split in SplitBoundingBox(boundingBox))
            {
                (long Start, long End)? range = null;

                if (precisionBits.HasValue)
                {
                    range = ComputeMortonRange(split, precisionBits.Value);
                }

                yield return new SpatialQuerySegment(split, range);
            }
        }

        public static IEnumerable<T> QueryCandidates<T>(LiteCollection<T> collection, IEnumerable<SpatialQuerySegment> segments, Func<T, BsonValue> idAccessor)
        {
            var seen = new HashSet<BsonValue>();

            foreach (var segment in segments)
            {
                var predicate = BuildPredicate(segment, out var args);

                foreach (var candidate in collection.Query().Where(predicate, args).ToEnumerable())
                {
                    var id = idAccessor(candidate);

                    if (!seen.Add(id))
                    {
                        continue;
                    }

                    yield return candidate;
                }
            }
        }

        private static IEnumerable<GeoBoundingBox> SplitBoundingBox(GeoBoundingBox box)
        {
            if (box.MinLon <= box.MaxLon)
            {
                yield return box;
                yield break;
            }

            yield return new GeoBoundingBox(box.MinLat, box.MinLon, box.MaxLat, 180d);
            yield return new GeoBoundingBox(box.MinLat, -180d, box.MaxLat, box.MaxLon);
        }

        private static (long Start, long End) ComputeMortonRange(GeoBoundingBox box, int precisionBits)
        {
            var corners = new[]
            {
                new GeoPoint(box.MinLat, box.MinLon),
                new GeoPoint(box.MinLat, box.MaxLon),
                new GeoPoint(box.MaxLat, box.MinLon),
                new GeoPoint(box.MaxLat, box.MaxLon)
            };

            long min = long.MaxValue;
            long max = long.MinValue;

            foreach (var corner in corners)
            {
                var morton = SpatialIndexing.ComputeMorton(corner, precisionBits);

                if (morton < min)
                {
                    min = morton;
                }

                if (morton > max)
                {
                    max = morton;
                }
            }

            return (min, max);
        }

        private static string BuildPredicate(SpatialQuerySegment segment, out BsonValue[] parameters)
        {
            if (segment.MortonRange.HasValue)
            {
                parameters = new[]
                {
                    new BsonValue(segment.MortonRange.Value.Start),
                    new BsonValue(segment.MortonRange.Value.End),
                    new BsonValue(segment.BoundingBox.MinLat),
                    new BsonValue(segment.BoundingBox.MinLon),
                    new BsonValue(segment.BoundingBox.MaxLat),
                    new BsonValue(segment.BoundingBox.MaxLon)
                };

                return "($._gh BETWEEN @0 AND @1) AND SPATIAL_INTERSECTS($._mbb, @2, @3, @4, @5)";
            }

            parameters = new[]
            {
                new BsonValue(segment.BoundingBox.MinLat),
                new BsonValue(segment.BoundingBox.MinLon),
                new BsonValue(segment.BoundingBox.MaxLat),
                new BsonValue(segment.BoundingBox.MaxLon)
            };

            return "SPATIAL_INTERSECTS($._mbb, @0, @1, @2, @3)";
        }
    }
}
