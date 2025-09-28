using System;

namespace LiteDB.Spatial
{
    internal static class GeoHash
    {
        public static long Encode(GeoPoint point, int precisionBits)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            if (precisionBits <= 0 || precisionBits > 62)
            {
                throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be in (0,62].");
            }

            var halfBits = precisionBits / 2;
            var latNorm = Normalize(point.Lat, -90d, 90d);
            var lonNorm = Normalize(point.Lon, -180d, 180d);

            var latInt = ScaleToUInt(latNorm, halfBits);
            var lonInt = ScaleToUInt(lonNorm, precisionBits - halfBits);

            return (long)InterleaveBits(lonInt, latInt);
        }

        private static double Normalize(double value, double min, double max)
        {
            return (value - min) / (max - min);
        }

        private static ulong ScaleToUInt(double normalized, int bits)
        {
            if (bits <= 0)
            {
                return 0UL;
            }

            var maxValue = (1UL << bits) - 1UL;
            var scaled = normalized * maxValue;
            var rounded = Clamp(Math.Round(scaled, MidpointRounding.AwayFromZero), 0d, maxValue);
            return (ulong)rounded;
        }

        private static ulong InterleaveBits(ulong lon, ulong lat)
        {
            ulong result = 0UL;
            int bit = 0;

            while (lon != 0 || lat != 0)
            {
                result |= (lon & 1UL) << bit++;
                lon >>= 1;
                result |= (lat & 1UL) << bit++;
                lat >>= 1;
            }

            return result;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
