// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Wrapper for generated tagged serializer and compiled using Roslyn.
    /// </summary>
    public class TaggedSerializerRoslyn
    {
        delegate void WriteObjectDelegate(ref MetaSerializationContext context, ref SpanWriter writer, Type type, object value);
        delegate object ReadObjectDelegate(ref MetaSerializationContext context, IOReader reader, Type type);
        delegate void WriteTableDelegate(ref MetaSerializationContext context, ref SpanWriter writer, Type type, object value, int maxCollectionSizeOverride);
        delegate object ReadTableDelegate(ref MetaSerializationContext context, IOReader reader, Type type, int maxCollectionSizeOverride);
        delegate void TraverseMetaRefsInObjectDelegate(ref MetaSerializationContext context, Type type, ref object value);
        delegate void TraverseMetaRefsInTableDelegate(ref MetaSerializationContext context, Type type, object value);

        readonly WriteObjectDelegate              _writeObjectDelegate;
        readonly ReadObjectDelegate               _readObjectDelegate;
        readonly TraverseMetaRefsInObjectDelegate _traverseMetaRefsInObjectDelegate;
        readonly WriteTableDelegate               _writeTableDelegate;
        readonly ReadTableDelegate                _readTableDelegate;
        readonly TraverseMetaRefsInTableDelegate  _traverseMetaRefsInTableDelegate;

        public TaggedSerializerRoslyn(Type generatedSerializer)
        {
            MethodInfo writeObjectMethod = generatedSerializer.GetMethod("SerializeObject", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(SpanWriter).MakeByRefType(), typeof(Type), typeof(object) });
            _writeObjectDelegate = (WriteObjectDelegate)writeObjectMethod?.CreateDelegate(typeof(WriteObjectDelegate));
            if (_writeObjectDelegate == null)
                throw new InvalidOperationException($"Unable to find valid SerializeObject() from generated serializer");

            MethodInfo readObjectMethod = generatedSerializer.GetMethod("DeserializeObject", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(IOReader), typeof(Type) });
            _readObjectDelegate = (ReadObjectDelegate)readObjectMethod?.CreateDelegate(typeof(ReadObjectDelegate));
            if (_readObjectDelegate == null)
                throw new InvalidOperationException($"Unable to find valid DeserializeObject() from generated serializer");

            MethodInfo traverseMetaRefsInObjectMethod = generatedSerializer.GetMethod("TraverseMetaRefsInObject", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(Type), typeof(object).MakeByRefType() });
            _traverseMetaRefsInObjectDelegate = (TraverseMetaRefsInObjectDelegate)traverseMetaRefsInObjectMethod?.CreateDelegate(typeof(TraverseMetaRefsInObjectDelegate));
            if (_traverseMetaRefsInObjectDelegate == null)
                throw new InvalidOperationException($"Unable to find valid TraverseMetaRefsInObject() from generated serializer");

            MethodInfo writeTableMethod = generatedSerializer.GetMethod("SerializeTable", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(SpanWriter).MakeByRefType(), typeof(Type), typeof(object), typeof(int) });
            _writeTableDelegate = (WriteTableDelegate)writeTableMethod?.CreateDelegate(typeof(WriteTableDelegate));
            if (_writeTableDelegate == null)
                throw new InvalidOperationException($"Unable to find valid SerializeTable() from generated serializer");

            MethodInfo readTableMethod = generatedSerializer.GetMethod("DeserializeTable", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(IOReader), typeof(Type), typeof(int) });
            _readTableDelegate = (ReadTableDelegate)readTableMethod?.CreateDelegate(typeof(ReadTableDelegate));
            if (_readTableDelegate == null)
                throw new InvalidOperationException($"Unable to find valid DeserializeTable() from generated serializer");

            MethodInfo traverseMetaRefsInTableMethod = generatedSerializer.GetMethod("TraverseMetaRefsInTable", new Type[] { typeof(MetaSerializationContext).MakeByRefType(), typeof(Type), typeof(object) });
            _traverseMetaRefsInTableDelegate = (TraverseMetaRefsInTableDelegate)traverseMetaRefsInTableMethod?.CreateDelegate(typeof(TraverseMetaRefsInTableDelegate));
            if (_traverseMetaRefsInTableDelegate == null)
                throw new InvalidOperationException($"Unable to find valid TraverseMetaRefsInTable() from generated serializer");
        }

        public void Serialize<T>(ref MetaSerializationContext context, IOWriter writer, T obj)
        {
            SpanWriter spanWriter = writer.GetSpanWriter();
            _writeObjectDelegate(ref context, ref spanWriter, typeof(T), obj);
            writer.ReleaseSpanWriter(ref spanWriter);
        }

        public void Serialize(ref MetaSerializationContext context, IOWriter writer, Type type, object obj)
        {
            SpanWriter spanWriter = writer.GetSpanWriter();
            _writeObjectDelegate(ref context, ref spanWriter, type, obj);
            writer.ReleaseSpanWriter(ref spanWriter);
        }

        public void SerializeTable<T>(ref MetaSerializationContext context, IOWriter writer, IReadOnlyList<T> table, int maxCollectionSizeOverride)
        {
            SpanWriter spanWriter = writer.GetSpanWriter();
            _writeTableDelegate(ref context, ref spanWriter, typeof(T), table, maxCollectionSizeOverride);
            writer.ReleaseSpanWriter(ref spanWriter);
        }

        public T Deserialize<T>(ref MetaSerializationContext context, IOReader reader)
        {
            return (T)_readObjectDelegate(ref context, reader, typeof(T));
        }

        public object Deserialize(ref MetaSerializationContext context, IOReader reader, Type type)
        {
            return _readObjectDelegate(ref context, reader, type);
        }

        public IReadOnlyList<T> DeserializeTable<T>(ref MetaSerializationContext context, IOReader reader, int maxCollectionSizeOverride, Type type)
        {
            return (IReadOnlyList<T>)_readTableDelegate(ref context, reader, type, maxCollectionSizeOverride);
        }

        public void TraverseMetaRefs(ref MetaSerializationContext context, Type type, ref object obj)
        {
            _traverseMetaRefsInObjectDelegate(ref context, type, ref obj);
        }

        public void TraverseMetaRefs<T>(ref MetaSerializationContext context, ref T value)
        {
            object obj = value;
            _traverseMetaRefsInObjectDelegate(ref context, typeof(T), ref obj);
            value = (T)obj;
        }

        public void TraverseMetaRefsInTable<T>(ref MetaSerializationContext context, List<T> table)
        {
            _traverseMetaRefsInTableDelegate(ref context, typeof(T), table);
        }
    }
}
