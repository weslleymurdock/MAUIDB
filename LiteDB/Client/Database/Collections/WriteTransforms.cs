using System;
using System.Collections.Generic;
using static LiteDB.Constants;

namespace LiteDB
{
    public sealed partial class LiteCollection<T>
    {
        private readonly List<Action<T, BsonDocument>> _writeTransforms = new List<Action<T, BsonDocument>>();

        internal void RegisterWriteTransform(Action<T, BsonDocument> transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            lock (_writeTransforms)
            {
                _writeTransforms.Add(transform);
            }
        }

        private void ApplyWriteTransforms(T entity, BsonDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            Action<T, BsonDocument>[] transforms;

            lock (_writeTransforms)
            {
                if (_writeTransforms.Count == 0)
                {
                    return;
                }

                transforms = _writeTransforms.ToArray();
            }

            foreach (var transform in transforms)
            {
                transform(entity, document);
            }
        }
    }
}
