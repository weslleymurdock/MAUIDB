using System;

namespace LiteDB.Spatial
{
    internal static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000d;

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180d;
        }

        public static double ClampLatitude(double latitude)
        {
            return Math.Max(-90d, Math.Min(90d, latitude));
        }

        public static double NormalizeLongitude(double longitude)
        {
            var lon = longitude % 360d;

            if (lon < -180d)
            {
                lon += 360d;
            }
            else if (lon >= 180d)
            {
                lon -= 360d;
            }

            return lon;
        }

        public static double DistanceMeters(GeoPoint a, GeoPoint b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            var lat1 = DegreesToRadians(a.Lat);
            var lat2 = DegreesToRadians(b.Lat);
            var dLat = DegreesToRadians(b.Lat - a.Lat);
            var dLon = DegreesToRadians(DeltaLongitude(a.Lon, b.Lon));

            var sinLat = Math.Sin(dLat / 2d);
            var sinLon = Math.Sin(dLon / 2d);

            var aTerm = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;

            aTerm = Math.Min(1d, Math.Max(0d, aTerm));

            var c = 2d * Math.Asin(Math.Sqrt(aTerm));

            return EarthRadiusMeters * c;
        }

        private static double DeltaLongitude(double lon1, double lon2)
        {
            var diff = NormalizeLongitude(lon2) - NormalizeLongitude(lon1);

            if (diff > 180d)
            {
                diff -= 360d;
            }
            else if (diff < -180d)
            {
                diff += 360d;
            }

            return diff;
        }
    }
}
