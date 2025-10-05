using System;
using LiteDB.Spatial;

namespace LiteDB
{
    internal partial class BsonExpressionMethods
    {
        public static BsonValue SPATIAL_INTERSECTS(BsonValue bboxValue, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        {
            if (!TryCreateBoundingBox(bboxValue, out var bbox))
            {
                return new BsonValue(false);
            }

            var queryBox = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);

            return new BsonValue(bbox.Intersects(queryBox));
        }

        public static BsonValue SPATIAL_CONTAINS_POINT(BsonValue bboxValue, BsonValue lat, BsonValue lon)
        {
            if (!TryCreateBoundingBox(bboxValue, out var bbox))
            {
                return new BsonValue(false);
            }

            if (!lat.IsNumber || !lon.IsNumber)
            {
                return new BsonValue(false);
            }

            var point = new GeoPoint(lat.AsDouble, lon.AsDouble);

            return new BsonValue(bbox.Contains(point));
        }

        public static BsonValue SPATIAL_NEAR(BsonValue bboxValue, BsonValue lat, BsonValue lon, BsonValue radiusMeters, BsonValue formula = null)
        {
            if (!TryCreateBoundingBox(bboxValue, out var bbox))
            {
                return new BsonValue(false);
            }

            if (!lat.IsNumber || !lon.IsNumber || !radiusMeters.IsNumber)
            {
                return new BsonValue(false);
            }

            var center = new GeoPoint(lat.AsDouble, lon.AsDouble);
            var radius = radiusMeters.AsDouble;

            if (radius < 0d)
            {
                return new BsonValue(false);
            }

            var distanceFormula = GetFormula(formula);

            if (Math.Abs(bbox.MinLat - bbox.MaxLat) < GeoMath.EpsilonDegrees && Math.Abs(bbox.MinLon - bbox.MaxLon) < GeoMath.EpsilonDegrees)
            {
                var candidate = new GeoPoint(bbox.MinLat, bbox.MinLon);
                var distance = GeoMath.DistanceMeters(center, candidate, distanceFormula);
                return new BsonValue(distance <= radius);
            }

            var circleBox = GeoMath.BoundingBoxForCircle(center, radius);
            return new BsonValue(bbox.Intersects(circleBox));
        }

        private static bool TryCreateBoundingBox(BsonValue value, out GeoBoundingBox bbox)
        {
            bbox = default;

            if (value == null || value.IsNull || !value.IsArray)
            {
                return false;
            }

            var array = value.AsArray;

            if (array.Count < 4)
            {
                return false;
            }

            if (!array[0].IsNumber || !array[1].IsNumber || !array[2].IsNumber || !array[3].IsNumber)
            {
                return false;
            }

            bbox = new GeoBoundingBox(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble, array[3].AsDouble);
            return true;
        }

        private static DistanceFormula GetFormula(BsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return DistanceFormula.Haversine;
            }

            if (value.IsString && Enum.TryParse<DistanceFormula>(value.AsString, true, out var parsed))
            {
                return parsed;
            }

            if (value.IsNumber)
            {
                try
                {
                    return (DistanceFormula)value.AsInt32;
                }
                catch
                {
                    return DistanceFormula.Haversine;
                }
            }

            return DistanceFormula.Haversine;
        }
    }
}
