using System;
using System.Collections.Concurrent;
using System.Reflection;
using LiteDB.Engine;

using LiteDB;

namespace LiteDB.Spatial
{
    internal static class SpatialIndexMetadata
    {
        private const string MetadataCollection = "_spatial_indexes";

        private static readonly ConcurrentDictionary<string, int> _precisionCache = new();

        public static int GetPrecision<T>(LiteCollection<T> collection, int fallback)
        {
            var name = GetCollectionName(collection);

            if (_precisionCache.TryGetValue(name, out var precision))
            {
                return precision;
            }

            var engine = GetEngine(collection);
            var key = new BsonValue($"{name}/_gh");

            var query = new Query();
            query.Where.Add(BsonExpression.Create("_id = @0", key));

            using var reader = engine.Query(MetadataCollection, query);
            var doc = reader.FirstOrDefault();

            if (doc != null && doc.IsDocument && doc.AsDocument.TryGetValue("precisionBits", out var value) && value.IsInt32)
            {
                precision = value.AsInt32;
                _precisionCache[name] = precision;
                return precision;
            }

            _precisionCache[name] = fallback;
            return fallback;
        }

        public static void RecordPrecision<T>(LiteCollection<T> collection, int precision)
        {
            var name = GetCollectionName(collection);

            _precisionCache[name] = precision;

            var engine = GetEngine(collection);

            var doc = new BsonDocument
            {
                ["_id"] = $"{name}/_gh",
                ["collection"] = name,
                ["index"] = "_gh",
                ["precisionBits"] = precision,
                ["updatedUtc"] = DateTime.UtcNow
            };

            engine.Upsert(MetadataCollection, new[] { doc }, BsonAutoId.ObjectId);
        }

        private static ILiteEngine GetEngine<T>(LiteCollection<T> collection)
        {
            var field = typeof(LiteCollection<T>).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new InvalidOperationException("Unable to access LiteCollection engine instance.");
            }

            return (ILiteEngine)field.GetValue(collection);
        }

        private static string GetCollectionName<T>(LiteCollection<T> collection)
        {
            var field = typeof(LiteCollection<T>).GetField("_collection", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new InvalidOperationException("Unable to access LiteCollection name.");
            }

            return (string)field.GetValue(collection);
        }
    }
}
