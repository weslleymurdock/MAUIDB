using System;
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

            var bson = SpatialMapping.ToBsonValue(shape);
            return JsonSerializer.Serialize(bson, indent: false);
        }

        public static T Deserialize<T>(string json) where T : GeoShape
        {
            var shape = Deserialize(json);

            if (shape is not T typed)
            {
                throw new LiteException(0, $"GeoJSON payload describes a '{shape?.GetType().Name}', not '{typeof(T).Name}'.");
            }

            return typed;
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
                throw new LiteException(0, "GeoJSON root must be a document.");
            }

            var doc = bson.AsDocument;
            if (doc.ContainsKey("crs"))
            {
                throw new LiteException(0, "Only the default WGS84 CRS is supported.");
            }

            return SpatialMapping.FromBsonValue(bson);
        }
    }
}
