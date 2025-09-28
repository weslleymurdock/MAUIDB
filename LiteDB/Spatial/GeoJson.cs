using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;

namespace LiteDB.Spatial
{
    public static class GeoJson
    {
        public static string Serialize(GeoShape shape)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            var bson = ToBson(shape);
            return JsonSerializer.Serialize(bson, false);
        }

        public static T Deserialize<T>(string json)
            where T : GeoShape
        {
            var shape = Deserialize(json);

            if (shape is T typed)
            {
                return typed;
            }

            throw new ArgumentException($"GeoJSON represents {shape.GetType().Name} which cannot be cast to {typeof(T).Name}");
        }

        public static GeoShape Deserialize(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var bson = JsonSerializer.Deserialize(json);

            if (!bson.IsDocument)
            {
                throw new ArgumentException("GeoJSON payload must be a JSON object", nameof(json));
            }

            return FromBson(bson.AsDocument);
        }

        internal static BsonValue ToBson(GeoShape shape)
        {
            return shape switch
            {
                GeoPoint point => new BsonDocument
                {
                    ["type"] = "Point",
                    ["coordinates"] = new BsonArray { point.Lon, point.Lat }
                },
                GeoLineString line => new BsonDocument
                {
                    ["type"] = "LineString",
                    ["coordinates"] = new BsonArray(line.Points.Select(p => new BsonArray { p.Lon, p.Lat }))
                },
                GeoPolygon polygon => new BsonDocument
                {
                    ["type"] = "Polygon",
                    ["coordinates"] = BuildPolygonCoordinates(polygon)
                },
                _ => throw new ArgumentException($"Unsupported geometry type {shape.GetType().Name}")
            };
        }

        internal static GeoShape FromBson(BsonDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
            {
                throw new ArgumentException("GeoJSON requires a string 'type' property");
            }

            var type = typeValue.AsString;

            switch (type)
            {
                case "Point":
                    return ParsePoint(document);
                case "LineString":
                    return ParseLineString(document);
                case "Polygon":
                    return ParsePolygon(document);
                default:
                    throw new ArgumentException($"Unsupported GeoJSON geometry type '{type}'");
            }
        }

        private static BsonArray BuildPolygonCoordinates(GeoPolygon polygon)
        {
            var rings = new List<BsonArray>
            {
                new BsonArray(polygon.Outer.Select(p => new BsonArray { p.Lon, p.Lat }))
            };

            foreach (var hole in polygon.Holes)
            {
                rings.Add(new BsonArray(hole.Select(p => new BsonArray { p.Lon, p.Lat })));
            }

            return new BsonArray(rings);
        }

        private static GeoPoint ParsePoint(BsonDocument document)
        {
            var coordinates = ReadCoordinateArray(document);
            return new GeoPoint(coordinates.lat, coordinates.lon);
        }

        private static GeoLineString ParseLineString(BsonDocument document)
        {
            var array = EnsureArray(document, "coordinates");

            var points = array.Select(value => ToPoint(value)).ToList();

            return new GeoLineString(points);
        }

        private static GeoPolygon ParsePolygon(BsonDocument document)
        {
            var array = EnsureArray(document, "coordinates");

            if (array.Count == 0)
            {
                throw new ArgumentException("Polygon must have at least one ring");
            }

            var rings = array.Select(ToRing).ToList();

            var outer = rings[0];
            var holes = rings.Skip(1).Select(r => (IReadOnlyList<GeoPoint>)r).ToList();

            return new GeoPolygon(outer, holes);
        }

        private static (double lon, double lat) ReadCoordinateArray(BsonDocument document)
        {
            var array = EnsureArray(document, "coordinates");

            if (array.Count < 2)
            {
                throw new ArgumentException("Coordinate array must have at least two elements");
            }

            return (array[0].AsDouble, array[1].AsDouble);
        }

        private static List<GeoPoint> ToRing(BsonValue value)
        {
            if (!value.IsArray)
            {
                throw new ArgumentException("Ring must be an array of positions");
            }

            return value.AsArray.Select(ToPoint).ToList();
        }

        private static GeoPoint ToPoint(BsonValue value)
        {
            if (!value.IsArray)
            {
                throw new ArgumentException("Point must be an array");
            }

            var coords = value.AsArray;

            if (coords.Count < 2)
            {
                throw new ArgumentException("Point array must contain longitude and latitude");
            }

            return new GeoPoint(coords[1].AsDouble, coords[0].AsDouble);
        }

        private static BsonArray EnsureArray(BsonDocument document, string key)
        {
            if (!document.TryGetValue(key, out var value) || !value.IsArray)
            {
                throw new ArgumentException($"GeoJSON requires '{key}' to be an array");
            }

            return value.AsArray;
        }
    }
}
