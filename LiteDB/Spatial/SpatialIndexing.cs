using System;

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
