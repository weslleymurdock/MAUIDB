using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Spatial
{
    internal static class SpatialIndexing
    {
        public static long ComputeMorton(GeoPoint point, int precisionBits)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            if (precisionBits <= 0 || precisionBits > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be between 1 and 60 bits");
            }

            var normalized = point.Normalize();
            var bitsPerCoordinate = Math.Max(1, precisionBits / 2);
            var scaleLat = (1UL << bitsPerCoordinate) - 1UL;
            var scaleLon = (1UL << bitsPerCoordinate) - 1UL;

            var latNormalized = (normalized.Lat + 90d) / 180d;
            var lonNormalized = (normalized.Lon + 180d) / 360d;

            latNormalized = Math.Min(1d, Math.Max(0d, latNormalized));
            lonNormalized = Math.Min(1d, Math.Max(0d, lonNormalized));

            var latBits = (ulong)Math.Round(latNormalized * scaleLat);
            var lonBits = (ulong)Math.Round(lonNormalized * scaleLon);

            var morton = Interleave(lonBits, latBits, bitsPerCoordinate);

            return unchecked((long)morton);
        }

        public static IReadOnlyList<(long Min, long Max)> GetMortonRanges(GeoBoundingBox box, int precisionBits)
        {
            if (box.Equals(default(GeoBoundingBox)))
            {
                throw new ArgumentException("Bounding box must be defined", nameof(box));
            }

            var ranges = new List<(long Min, long Max)>();
            var lonRange = new LongitudeRange(box.MinLon, box.MaxLon);

            foreach (var (start, end) in lonRange.GetSegments())
            {
                var corners = new[]
                {
                    new GeoPoint(box.MinLat, start),
                    new GeoPoint(box.MinLat, end),
                    new GeoPoint(box.MaxLat, start),
                    new GeoPoint(box.MaxLat, end)
                };

                var min = corners.Min(c => ComputeMorton(c, precisionBits));
                var max = corners.Max(c => ComputeMorton(c, precisionBits));

                if (min > max)
                {
                    (min, max) = (max, min);
                }

                ranges.Add((min, max));
            }

            if (ranges.Count <= 1)
            {
                return ranges;
            }

            ranges.Sort((a, b) => a.Min.CompareTo(b.Min));

            var merged = new List<(long Min, long Max)> { ranges[0] };

            for (var i = 1; i < ranges.Count; i++)
            {
                var last = merged[^1];
                var current = ranges[i];

                if (current.Min <= last.Max + 1)
                {
                    merged[^1] = (last.Min, Math.Max(last.Max, current.Max));
                }
                else
                {
                    merged.Add(current);
                }
            }

            return merged;
        }

        private static ulong Interleave(ulong x, ulong y, int bits)
        {
            ulong result = 0;

            for (var i = 0; i < bits; i++)
            {
                var shift = (ulong)i;
                result |= ((x >> i) & 1UL) << (int)(2 * shift);
                result |= ((y >> i) & 1UL) << (int)(2 * shift + 1);
            }

            return result;
        }
    }
}
