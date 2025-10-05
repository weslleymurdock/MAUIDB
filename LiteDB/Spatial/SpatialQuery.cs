using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class SpatialQuery
    {
        private const string MortonField = "$._gh";
        private const string BoundingBoxField = "$._mbb";

        public static BsonExpression Near<T>(BsonExpression field, GeoPoint center, double radiusMeters, LiteCollection<T> collection = null, int? precisionBits = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (center == null) throw new ArgumentNullException(nameof(center));
            if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

            var precision = ResolvePrecision(collection, precisionBits);

            var normalizedCenter = center.Normalize();
            var queryBox = GeoMath.BoundingBoxForCircle(normalizedCenter, radiusMeters);
            queryBox = queryBox.Expand(Spatial.Options.BoundingBoxPaddingMeters);

            var args = new[]
            {
                new BsonValue(queryBox.MinLat),
                new BsonValue(queryBox.MinLon),
                new BsonValue(queryBox.MaxLat),
                new BsonValue(queryBox.MaxLon),
                new BsonValue(normalizedCenter.Lat),
                new BsonValue(normalizedCenter.Lon),
                new BsonValue(radiusMeters),
                new BsonValue(Spatial.Options.Distance.ToString())
            };

            var predicates = new List<BsonExpression>
            {
                BsonExpression.Create($"SPATIAL_MBB_INTERSECTS({BoundingBoxField}, @0, @1, @2, @3)", args[0], args[1], args[2], args[3]),
                BsonExpression.Create($"SPATIAL_NEAR({field.Source}, @4, @5, @6, @7)", args)
            };

            var ranges = SpatialIndexing.GetMortonRanges(queryBox, precision);

            if (ranges.Count == 1)
            {
                predicates.Add(Query.Between(MortonField, new BsonValue(ranges[0].Min), new BsonValue(ranges[0].Max)));
            }
            else if (ranges.Count > 1)
            {
                var orPredicates = ranges
                    .Select(range => Query.Between(MortonField, new BsonValue(range.Min), new BsonValue(range.Max)))
                    .ToArray();

                predicates.Add(Query.Or(orPredicates));
            }

            return Query.And(predicates.ToArray());
        }

        public static BsonExpression WithinBoundingBox(BsonExpression field, GeoBoundingBox box)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            return BsonExpression.Create($"SPATIAL_WITHIN_BOX({field.Source}, @0, @1, @2, @3)",
                box.MinLat, box.MinLon, box.MaxLat, box.MaxLon);
        }

        public static BsonExpression Within(BsonExpression field, GeoPolygon polygon)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var bson = GeoJson.ToBson(polygon);
            return BsonExpression.Create($"SPATIAL_WITHIN({field.Source}, @0)", bson);
        }

        public static BsonExpression Intersects(BsonExpression field, GeoShape shape)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            var bson = GeoJson.ToBson(shape);
            return BsonExpression.Create($"SPATIAL_INTERSECTS({field.Source}, @0)", bson);
        }

        public static BsonExpression Contains(BsonExpression field, GeoPoint point)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (point == null) throw new ArgumentNullException(nameof(point));

            var bson = GeoJson.ToBson(point);
            return BsonExpression.Create($"SPATIAL_CONTAINS({field.Source}, @0)", bson);
        }

        private static int ResolvePrecision<T>(LiteCollection<T> collection, int? precisionBits)
        {
            if (precisionBits.HasValue)
            {
                return precisionBits.Value;
            }

            if (collection != null)
            {
                return SpatialIndexMetadata.GetPrecision(collection, Spatial.Options.DefaultIndexPrecisionBits);
            }

            return Spatial.Options.DefaultIndexPrecisionBits;
        }
    }
}
