using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class Spatial
    {
        private static readonly object _mapperLock = new();
        private static readonly Dictionary<BsonMapper, bool> _registeredMappers = new();

        public static SpatialOptions Options { get; set; } = new SpatialOptions();

        public static void EnsurePointIndex<T>(ILiteCollection<T> collection, Expression<Func<T, GeoPoint>> selector, int precisionBits = 52)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var getter = selector.Compile();

            SpatialMapping.EnsureComputedMember(lite, "_gh", typeof(long), entity =>
            {
                var point = getter(entity);
                if (point == null)
                {
                    return null;
                }

                return GeoHash.Encode(point, precisionBits);
            });

            SpatialMapping.EnsureBoundingBox(lite, entity => getter(entity));

            lite.EnsureIndex(BsonExpression.Create("$._gh"));
        }

        public static void EnsureShapeIndex<T>(ILiteCollection<T> collection, Expression<Func<T, GeoShape>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            EnsureShapeIndexInternal(collection, selector.Compile());
        }

        public static void EnsureShapeIndex<T, TShape>(ILiteCollection<T> collection, Expression<Func<T, TShape>> selector)
            where TShape : GeoShape
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var compiled = selector.Compile();
            EnsureShapeIndexInternal(collection, entity => compiled(entity));
        }

        private static void EnsureShapeIndexInternal<T>(ILiteCollection<T> collection, Func<T, GeoShape> getter)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            SpatialMapping.EnsureBoundingBox(lite, getter);
        }

        public static IEnumerable<T> Near<T>(ILiteCollection<T> collection, Func<T, GeoPoint> selector, GeoPoint center, double radiusMeters, int? limit = null)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (center == null) throw new ArgumentNullException(nameof(center));
            if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var candidates = new List<(T item, double distance)>();

            foreach (var item in lite.FindAll())
            {
                var point = selector(item);
                if (point == null)
                {
                    continue;
                }

                var distance = GeoMath.DistanceMeters(center, point, Options.Distance);
                if (distance <= radiusMeters)
                {
                    candidates.Add((item, distance));
                }
            }

            if (Options.SortNearByDistance)
            {
                candidates.Sort((x, y) => x.distance.CompareTo(y.distance));
            }

            if (limit.HasValue)
            {
                return candidates.Take(limit.Value).Select(x => x.item).ToList();
            }

            return candidates.Select(x => x.item).ToList();
        }

        public static IEnumerable<T> WithinBoundingBox<T>(ILiteCollection<T> collection, Func<T, GeoPoint> selector, double minLat, double minLon, double maxLat, double maxLon)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var bbox = new BoundingBox(minLat, minLon, maxLat, maxLon);

            var results = new List<T>();

            foreach (var item in lite.FindAll())
            {
                var point = selector(item);
                if (point != null && bbox.Contains(point))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        public static IEnumerable<T> Within<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoPolygon area)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (area == null) throw new ArgumentNullException(nameof(area));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var results = new List<T>();

            foreach (var item in lite.FindAll())
            {
                var shape = selector(item);
                if (shape == null)
                {
                    continue;
                }

                if (SpatialGeometry.IsWithin(shape, area))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        public static IEnumerable<T> Intersects<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoShape query)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (query == null) throw new ArgumentNullException(nameof(query));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var results = new List<T>();

            foreach (var item in lite.FindAll())
            {
                var shape = selector(item);
                if (shape == null)
                {
                    continue;
                }

                if (SpatialGeometry.Intersects(shape, query))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        public static IEnumerable<T> Contains<T>(ILiteCollection<T> collection, Func<T, GeoShape> selector, GeoPoint point)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (point == null) throw new ArgumentNullException(nameof(point));

            var lite = GetLiteCollection(collection);
            var mapper = GetMapper(lite);
            EnsureMapperRegistration(mapper);

            var results = new List<T>();

            foreach (var item in lite.FindAll())
            {
                var shape = selector(item);
                if (shape == null)
                {
                    continue;
                }

                if (SpatialGeometry.Contains(shape, point))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        private static LiteCollection<T> GetLiteCollection<T>(ILiteCollection<T> collection)
        {
            if (collection is LiteCollection<T> liteCollection)
            {
                return liteCollection;
            }

            throw new NotSupportedException("Spatial helpers require LiteCollection<T> instances.");
        }

        private static BsonMapper GetMapper<T>(LiteCollection<T> liteCollection)
        {
            var mapperField = typeof(LiteCollection<T>).GetField("_mapper", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mapperField != null)
            {
                return (BsonMapper)mapperField.GetValue(liteCollection);
            }

            return BsonMapper.Global;
        }

        private static void EnsureMapperRegistration(BsonMapper mapper)
        {
            lock (_mapperLock)
            {
                if (_registeredMappers.ContainsKey(mapper))
                {
                    return;
                }

                SpatialMapping.RegisterGeoTypes(mapper);
                _registeredMappers[mapper] = true;
            }
        }
    }

    internal static class SpatialMapping
    {
        private static readonly Type EnumerableType = typeof(System.Collections.IEnumerable);

        public static void RegisterGeoTypes(BsonMapper mapper)
        {
            mapper.RegisterType<GeoShape>(ToBsonValue, FromBsonValue);
            mapper.RegisterType<GeoPoint>(SerializePoint, bson => (GeoPoint)FromBsonValue(bson));
            mapper.RegisterType<GeoLineString>(SerializeLineString, bson => (GeoLineString)FromBsonValue(bson));
            mapper.RegisterType<GeoPolygon>(SerializePolygon, bson => (GeoPolygon)FromBsonValue(bson));
        }

        public static void EnsureBoundingBox<T>(LiteCollection<T> collection, Func<T, GeoShape> getter)
        {
            EnsureComputedMember(collection, "_mbb", typeof(double[]), entity =>
            {
                var shape = getter(entity);
                if (shape == null)
                {
                    return null;
                }

                var bbox = shape.GetBoundingBox();
                return new[] { bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon };
            });
        }

        public static void EnsureComputedMember<T>(LiteCollection<T> collection, string fieldName, Type dataType, Func<T, object?> getter)
        {
            var entity = collection.EntityMapper;
            if (entity == null)
            {
                throw new InvalidOperationException("Entity mapper not available for collection.");
            }

            entity.WaitForInitialization();

            var member = entity.Members.FirstOrDefault(x => string.Equals(x.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

            if (member == null)
            {
                var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var memberInfo = (MemberInfo?)typeof(T).GetProperty(fieldName, bindingFlags) ?? typeof(T).GetField(fieldName, bindingFlags);

                GenericSetter setter = null;
                if (memberInfo != null)
                {
                    setter = Reflection.CreateGenericSetter(typeof(T), memberInfo);
                }

                member = new MemberMapper
                {
                    FieldName = fieldName,
                    MemberName = memberInfo?.Name ?? fieldName,
                    DataType = dataType,
                    UnderlyingType = dataType,
                    IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string),
                    Getter = obj => getter((T)obj),
                    Setter = setter
                };

                entity.Members.Add(member);
            }
            else
            {
                member.Getter = obj => getter((T)obj);
                member.DataType = dataType;
                member.UnderlyingType = dataType;
                member.IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string);
            }
        }

        internal static BsonValue ToBsonValue(GeoShape shape)
        {
            return shape switch
            {
                null => BsonValue.Null,
                GeoPoint point => SerializePoint(point),
                GeoLineString lineString => SerializeLineString(lineString),
                GeoPolygon polygon => SerializePolygon(polygon),
                _ => throw new LiteException(0, $"Unsupported GeoShape type '{shape.GetType().Name}'.")
            };
        }

        private static BsonValue SerializePoint(GeoPoint point)
        {
            return new BsonDocument
            {
                ["type"] = "Point",
                ["coordinates"] = new BsonArray { point.Lon, point.Lat }
            };
        }

        private static BsonValue SerializeLineString(GeoLineString line)
        {
            var coordinates = new BsonArray();
            foreach (var point in line.Points)
            {
                coordinates.Add(new BsonArray { point.Lon, point.Lat });
            }

            return new BsonDocument
            {
                ["type"] = "LineString",
                ["coordinates"] = coordinates
            };
        }

        private static BsonValue SerializePolygon(GeoPolygon polygon)
        {
            var rings = new BsonArray { ToRingArray(polygon.Outer) };

            if (polygon.Holes != null)
            {
                foreach (var hole in polygon.Holes)
                {
                    rings.Add(ToRingArray(hole));
                }
            }

            return new BsonDocument
            {
                ["type"] = "Polygon",
                ["coordinates"] = rings
            };
        }

        private static BsonArray ToRingArray(IReadOnlyList<GeoPoint> points)
        {
            var ring = new BsonArray();
            foreach (var point in points)
            {
                ring.Add(new BsonArray { point.Lon, point.Lat });
            }

            return ring;
        }

        internal static GeoShape FromBsonValue(BsonValue bson)
        {
            if (bson == null || bson.IsNull)
            {
                return null;
            }

            if (!bson.IsDocument)
            {
                throw new LiteException(0, "GeoJSON value must be a document.");
            }

            var doc = bson.AsDocument;
            var type = doc["type"].AsString;

            switch (type)
            {
                case "Point":
                    return DeserializePoint(doc["coordinates"]);
                case "LineString":
                    return DeserializeLineString(doc["coordinates"]);
                case "Polygon":
                    return DeserializePolygon(doc["coordinates"]);
                default:
                    throw new LiteException(0, $"Unsupported GeoJSON type '{type}'.");
            }
        }

        private static GeoPoint DeserializePoint(BsonValue value)
        {
            var array = value.AsArray;
            if (array.Count != 2)
            {
                throw new LiteException(0, "Point coordinates must contain two values.");
            }

            return new GeoPoint(array[1].AsDouble, array[0].AsDouble);
        }

        private static GeoLineString DeserializeLineString(BsonValue value)
        {
            var array = value.AsArray;
            var points = new List<GeoPoint>(array.Count);

            foreach (var item in array)
            {
                points.Add(DeserializePoint(item));
            }

            return new GeoLineString(points);
        }

        private static GeoPolygon DeserializePolygon(BsonValue value)
        {
            var array = value.AsArray;
            if (array.Count == 0)
            {
                throw new LiteException(0, "Polygon must contain at least one ring.");
            }

            var outer = array[0].AsArray.Select(DeserializePoint).ToList();
            var holes = new List<IReadOnlyList<GeoPoint>>();

            for (var i = 1; i < array.Count; i++)
            {
                holes.Add(array[i].AsArray.Select(DeserializePoint).ToList());
            }

            return new GeoPolygon(outer, holes);
        }
    }

    internal static class SpatialGeometry
    {
        public static bool IsWithin(GeoShape candidate, GeoPolygon area)
        {
            switch (candidate)
            {
                case GeoPoint point:
                    return ContainsPolygon(point, area);
                case GeoPolygon polygon:
                    return PolygonWithinPolygon(polygon, area);
                case GeoLineString line:
                    return LineWithinPolygon(line, area);
                default:
                    return false;
            }
        }

        public static bool Intersects(GeoShape left, GeoShape right)
        {
            return (left, right) switch
            {
                (GeoPoint p, GeoPolygon polygon) => ContainsPolygon(p, polygon),
                (GeoPolygon polygon, GeoPoint p) => ContainsPolygon(p, polygon),
                (GeoPolygon a, GeoPolygon b) => PolygonIntersectsPolygon(a, b),
                (GeoLineString line, GeoPolygon polygon) => LineIntersectsPolygon(line, polygon),
                (GeoPolygon polygon, GeoLineString line) => LineIntersectsPolygon(line, polygon),
                (GeoLineString a, GeoLineString b) => LineIntersectsLine(a, b),
                _ => false
            };
        }

        public static bool Contains(GeoShape shape, GeoPoint point)
        {
            return shape switch
            {
                GeoPolygon polygon => ContainsPolygon(point, polygon),
                GeoLineString line => LineContainsPoint(line, point),
                GeoPoint p => Math.Abs(p.Lat - point.Lat) < GeoMath.EpsilonDegrees && Math.Abs(p.Lon - point.Lon) < GeoMath.EpsilonDegrees,
                _ => false
            };
        }

        private static bool ContainsPolygon(GeoPoint point, GeoPolygon polygon)
        {
            if (!polygon.GetBoundingBox().Contains(point))
            {
                return false;
            }

            if (!PointInRing(point, polygon.Outer))
            {
                return false;
            }

            if (polygon.Holes != null)
            {
                foreach (var hole in polygon.Holes)
                {
                    if (PointInRing(point, hole))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool PointInRing(GeoPoint point, IReadOnlyList<GeoPoint> ring)
        {
            var inside = false;

            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var pi = ring[i];
                var pj = ring[j];

                var intersect = ((pi.Lat > point.Lat) != (pj.Lat > point.Lat)) &&
                                (point.Lon < (pj.Lon - pi.Lon) * (point.Lat - pi.Lat) / (pj.Lat - pi.Lat + double.Epsilon) + pi.Lon);

                if (intersect)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool PolygonWithinPolygon(GeoPolygon inner, GeoPolygon outer)
        {
            foreach (var point in inner.Outer)
            {
                if (!ContainsPolygon(point, outer))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LineWithinPolygon(GeoLineString line, GeoPolygon polygon)
        {
            foreach (var point in line.Points)
            {
                if (!ContainsPolygon(point, polygon))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PolygonIntersectsPolygon(GeoPolygon a, GeoPolygon b)
        {
            if (!a.GetBoundingBox().Intersects(b.GetBoundingBox()))
            {
                return false;
            }

            foreach (var point in a.Outer)
            {
                if (ContainsPolygon(point, b))
                {
                    return true;
                }
            }

            foreach (var point in b.Outer)
            {
                if (ContainsPolygon(point, a))
                {
                    return true;
                }
            }

            return RingsIntersect(a.Outer, b.Outer);
        }

        private static bool LineIntersectsPolygon(GeoLineString line, GeoPolygon polygon)
        {
            if (!polygon.GetBoundingBox().Intersects(line.GetBoundingBox()))
            {
                return false;
            }

            var outerSegments = ToSegments(polygon.Outer);
            var lineSegments = ToSegments(line.Points);

            foreach (var segment in lineSegments)
            {
                if (ContainsPolygon(segment.start, polygon) || ContainsPolygon(segment.end, polygon))
                {
                    return true;
                }

                foreach (var outer in outerSegments)
                {
                    if (SegmentsIntersect(segment.start, segment.end, outer.start, outer.end))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool LineIntersectsLine(GeoLineString a, GeoLineString b)
        {
            var aSegments = ToSegments(a.Points);
            var bSegments = ToSegments(b.Points);

            foreach (var sa in aSegments)
            {
                foreach (var sb in bSegments)
                {
                    if (SegmentsIntersect(sa.start, sa.end, sb.start, sb.end))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<(GeoPoint start, GeoPoint end)> ToSegments(IReadOnlyList<GeoPoint> points)
        {
            for (var i = 1; i < points.Count; i++)
            {
                yield return (points[i - 1], points[i]);
            }
        }

        private static bool RingsIntersect(IReadOnlyList<GeoPoint> a, IReadOnlyList<GeoPoint> b)
        {
            var aSegments = ToSegments(a).ToList();
            var bSegments = ToSegments(b).ToList();

            foreach (var sa in aSegments)
            {
                foreach (var sb in bSegments)
                {
                    if (SegmentsIntersect(sa.start, sa.end, sb.start, sb.end))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(GeoPoint a1, GeoPoint a2, GeoPoint b1, GeoPoint b2)
        {
            var d1 = Direction(a1, a2, b1);
            var d2 = Direction(a1, a2, b2);
            var d3 = Direction(b1, b2, a1);
            var d4 = Direction(b1, b2, a2);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            if (Math.Abs(d1) < double.Epsilon && OnSegment(a1, a2, b1)) return true;
            if (Math.Abs(d2) < double.Epsilon && OnSegment(a1, a2, b2)) return true;
            if (Math.Abs(d3) < double.Epsilon && OnSegment(b1, b2, a1)) return true;
            if (Math.Abs(d4) < double.Epsilon && OnSegment(b1, b2, a2)) return true;

            return false;
        }

        private static double Direction(GeoPoint a, GeoPoint b, GeoPoint c)
        {
            return (c.Lon - a.Lon) * (b.Lat - a.Lat) - (c.Lat - a.Lat) * (b.Lon - a.Lon);
        }

        private static bool OnSegment(GeoPoint a, GeoPoint b, GeoPoint c)
        {
            return c.Lon >= Math.Min(a.Lon, b.Lon) - GeoMath.EpsilonDegrees &&
                   c.Lon <= Math.Max(a.Lon, b.Lon) + GeoMath.EpsilonDegrees &&
                   c.Lat >= Math.Min(a.Lat, b.Lat) - GeoMath.EpsilonDegrees &&
                   c.Lat <= Math.Max(a.Lat, b.Lat) + GeoMath.EpsilonDegrees;
        }

        private static bool LineContainsPoint(GeoLineString line, GeoPoint point)
        {
            foreach (var segment in ToSegments(line.Points))
            {
                var start = segment.start;
                var end = segment.end;

                if (OnSegment(start, end, point) && Math.Abs(Direction(start, end, point)) < GeoMath.EpsilonDegrees)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
