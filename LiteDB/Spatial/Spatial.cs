using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class Spatial
    {
        private static readonly object _initLock = new object();

        private static bool _initialized;

        public static SpatialOptions Options { get; set; } = new SpatialOptions();

        public static void EnsurePointIndex<T>(ILiteCollection<T> collection, Expression<Func<T, GeoPoint>> selector, int precisionBits = 52)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (collection is not LiteCollection<T> liteCollection)
            {
                throw new ArgumentException("Spatial indexing requires LiteCollection", nameof(collection));
            }

            var getter = selector.Compile();

            liteCollection.RegisterWriteTransform((entity, document) =>
            {
                var point = getter(entity);

                if (point == null)
                {
                    document["_gh"] = BsonValue.Null;
                    document["_mbb"] = BsonValue.Null;
                    return;
                }

                var normalized = new GeoPoint(point.Lat, point.Lon);
                var hash = SpatialIndexing.ComputeMorton(normalized, precisionBits);
                document["_gh"] = hash;

                var bbox = normalized.GetBoundingBox();
                document["_mbb"] = new BsonArray
                {
                    new BsonValue(bbox.MinLat),
                    new BsonValue(bbox.MinLon),
                    new BsonValue(bbox.MaxLat),
                    new BsonValue(bbox.MaxLon)
                };
            });

            collection.EnsureIndex("_gh", BsonExpression.Create("_gh"));

            RebuildComputedFields(collection);
        }

        public static void EnsureShapeIndex<T>(ILiteCollection<T> collection, Expression<Func<T, GeoShape>> selector)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (collection is not LiteCollection<T> liteCollection)
            {
                throw new ArgumentException("Spatial indexing requires LiteCollection", nameof(collection));
            }

            var getter = selector.Compile();

            liteCollection.RegisterWriteTransform((entity, document) =>
            {
                var shape = getter(entity);

                if (shape == null)
                {
                    document["_mbb"] = BsonValue.Null;
                    return;
                }

                if (shape is GeoPoint point)
                {
                    var hash = SpatialIndexing.ComputeMorton(point, 52);
                    document["_gh"] = hash;
                }

                var bbox = shape.GetBoundingBox();
                document["_mbb"] = new BsonArray
                {
                    new BsonValue(bbox.MinLat),
                    new BsonValue(bbox.MinLon),
                    new BsonValue(bbox.MaxLat),
                    new BsonValue(bbox.MaxLon)
                };
            });

            RebuildComputedFields(collection);
        }

        public static IEnumerable<T> Near<T>(ILiteCollection<T> collection, Func<T, GeoPoint> selector, GeoPoint center, double radiusMeters, int? limit = null)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (center == null)
            {
                throw new ArgumentNullException(nameof(center));
            }

            if (radiusMeters < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusMeters));
            }

            var results = new List<(T item, double distance)>();

            foreach (var entity in collection.FindAll())
            {
                var point = selector(entity);

                if (point == null)
                {
                    continue;
                }

                var distance = GeoMath.DistanceMeters(center, point);

                if (distance <= radiusMeters)
                {
                    results.Add((entity, distance));
                }
            }

            if (Options.SortNearByDistance)
            {
                results.Sort((a, b) => a.distance.CompareTo(b.distance));
            }

            if (limit.HasValue)
            {
                return results.Take(limit.Value).Select(r => r.item).ToList();
            }

            return results.Select(r => r.item).ToList();
        }

        public static IEnumerable<T> WithinBoundingBox<T>(ILiteCollection<T> collection, Func<T, GeoPoint> selector, double minLat, double minLon, double maxLat, double maxLon)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            var latitudeRange = (min: Math.Min(minLat, maxLat), max: Math.Max(minLat, maxLat));
            var lonRange = new LongitudeRange(minLon, maxLon);

            return collection.FindAll()
                .Where(entity =>
                {
                    var point = selector(entity);
                    if (point == null)
                    {
                        return false;
                    }

                    return point.Lat >= latitudeRange.min && point.Lat <= latitudeRange.max && lonRange.Contains(point.Lon);
                })
                .ToList();
        }

        public static IEnumerable<T> Within<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoPolygon area)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (area == null)
            {
                throw new ArgumentNullException(nameof(area));
            }

            return collection.FindAll().Where(entity =>
            {
                var shape = selector(entity);

                if (shape == null)
                {
                    return false;
                }

                return shape switch
                {
                    GeoPoint point => Geometry.ContainsPoint(area, point),
                    GeoPolygon polygon => Geometry.Intersects(area, polygon) && polygon.Outer.All(p => Geometry.ContainsPoint(area, p)),
                    GeoLineString line => line.Points.All(p => Geometry.ContainsPoint(area, p)),
                    _ => false
                };
            }).ToList();
        }

        public static IEnumerable<T> Intersects<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoShape query)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return collection.FindAll().Where(entity =>
            {
                var shape = selector(entity);

                if (shape == null)
                {
                    return false;
                }

                return shape switch
                {
                    GeoPoint point when query is GeoPolygon polygon => Geometry.ContainsPoint(polygon, point),
                    GeoLineString line when query is GeoPolygon polygon => Geometry.Intersects(line, polygon),
                    GeoPolygon polygon when query is GeoPolygon other => Geometry.Intersects(polygon, other),
                    GeoLineString line when query is GeoLineString other => IntersectsLineStrings(line, other),
                    _ => false
                };
            }).ToList();
        }

        public static IEnumerable<T> Contains<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoPoint point)
        {
            EnsureInitialized();

            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            return collection.FindAll().Where(entity =>
            {
                var shape = selector(entity);

                return shape switch
                {
                    GeoPolygon polygon => Geometry.ContainsPoint(polygon, point),
                    GeoPoint candidate => Math.Abs(candidate.Lat - point.Lat) < 1e-9 && Math.Abs(candidate.Lon - point.Lon) < 1e-9,
                    GeoLineString line => line.Points.Any(p => Math.Abs(p.Lat - point.Lat) < 1e-9 && Math.Abs(p.Lon - point.Lon) < 1e-9),
                    _ => false
                };
            }).ToList();
        }

        private static bool IntersectsLineStrings(GeoLineString a, GeoLineString b)
        {
            for (var i = 0; i < a.Points.Count - 1; i++)
            {
                for (var j = 0; j < b.Points.Count - 1; j++)
                {
                    if (Geometry.SegmentsIntersect(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (_initLock)
            {
                if (_initialized)
                {
                    return;
                }

                RegisterGeoTypes();
                _initialized = true;
            }
        }

        private static void RegisterGeoTypes()
        {
            void Register<TShape>(Func<TShape, BsonValue> serialize, Func<BsonValue, TShape> deserialize)
                where TShape : GeoShape
            {
                BsonMapper.Global.RegisterType(typeof(TShape), value => serialize((TShape)value), bson => deserialize(bson));
            }

            Register<GeoShape>(GeoJson.ToBson, bson => (GeoShape)GeoJson.FromBson(bson.AsDocument));
            Register<GeoPoint>(GeoJson.ToBson, bson => (GeoPoint)GeoJson.FromBson(bson.AsDocument));
            Register<GeoLineString>(GeoJson.ToBson, bson => (GeoLineString)GeoJson.FromBson(bson.AsDocument));
            Register<GeoPolygon>(GeoJson.ToBson, bson => (GeoPolygon)GeoJson.FromBson(bson.AsDocument));
        }

        private static void RebuildComputedFields<T>(ILiteCollection<T> collection)
        {
            foreach (var entity in collection.FindAll().ToList())
            {
                collection.Update(entity);
            }
        }
    }
}
