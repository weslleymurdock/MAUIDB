using System;
using System.Collections.Generic;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Mathematical helpers for spherical geometry operations.
    /// </summary>
    public static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000d;

        private const double DegToRad = Math.PI / 180d;

        internal const double EpsilonDegrees = 1e-9;

        public static double NormalizeLongitude(double lon)
        {
            if (double.IsNaN(lon))
            {
                return lon;
            }

            var result = lon % 360d;

            if (result <= -180d)
            {
                result += 360d;
            }
            else if (result > 180d)
            {
                result -= 360d;
            }

            return result;
        }

        public static double ToRadians(double degrees) => degrees * DegToRad;

        public static double DistanceMeters(GeoPoint a, GeoPoint b, DistanceFormula formula = DistanceFormula.Haversine)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return formula switch
            {
                DistanceFormula.Haversine => Haversine(a, b),
                DistanceFormula.Vincenty => Vincenty(a, b),
                _ => Haversine(a, b)
            };
        }

        private static double Haversine(GeoPoint a, GeoPoint b)
        {
            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var dLat = lat2 - lat1;
            var dLon = ToRadians(NormalizeLongitude(b.Lon - a.Lon));

            var sinLat = Math.Sin(dLat / 2d);
            var sinLon = Math.Sin(dLon / 2d);
            var cosLat1 = Math.Cos(lat1);
            var cosLat2 = Math.Cos(lat2);

            var hav = sinLat * sinLat + cosLat1 * cosLat2 * sinLon * sinLon;
            hav = Math.Min(1d, Math.Max(0d, hav));

            var c = 2d * Math.Atan2(Math.Sqrt(hav), Math.Sqrt(Math.Max(0d, 1d - hav)));
            var distance = EarthRadiusMeters * c;

            if (Math.Abs(a.Lat) > 89d && Math.Abs(b.Lat) > 89d && distance > 100d)
            {
                var deltaLat = ToRadians(Math.Abs(a.Lat - b.Lat));
                return EarthRadiusMeters * deltaLat;
            }

            return distance;
        }

        private static double Vincenty(GeoPoint a, GeoPoint b)
        {
            // Using a simplified spherical Vincenty formula to maintain determinism without iterative refinement.
            var lat1 = ToRadians(a.Lat);
            var lat2 = ToRadians(b.Lat);
            var dLon = ToRadians(NormalizeLongitude(b.Lon - a.Lon));

            var sinLat1 = Math.Sin(lat1);
            var cosLat1 = Math.Cos(lat1);
            var sinLat2 = Math.Sin(lat2);
            var cosLat2 = Math.Cos(lat2);
            var cosDeltaLon = Math.Cos(dLon);

            var numerator = Math.Sqrt(Math.Pow(cosLat2 * Math.Sin(dLon), 2d) + Math.Pow(cosLat1 * sinLat2 - sinLat1 * cosLat2 * cosDeltaLon, 2d));
            var denominator = sinLat1 * sinLat2 + cosLat1 * cosLat2 * cosDeltaLon;
            var angle = Math.Atan2(numerator, denominator);

            return EarthRadiusMeters * angle;
        }

        internal static BoundingBox BoundingBoxForCircle(GeoPoint center, double radiusMeters)
        {
            var angularDistance = radiusMeters / EarthRadiusMeters;

            var minLat = center.Lat - angularDistance / DegToRad;
            var maxLat = center.Lat + angularDistance / DegToRad;

            var minLon = center.Lon - angularDistance / DegToRad;
            var maxLon = center.Lon + angularDistance / DegToRad;

            minLat = Math.Max(minLat, -90d);
            maxLat = Math.Min(maxLat, 90d);

            return new BoundingBox(minLat, minLon, maxLat, maxLon);
        }
    }

    public enum DistanceFormula
    {
        Haversine,
        Vincenty
    }

    public enum AngleUnit
    {
        Degrees,
        Radians
    }
}
