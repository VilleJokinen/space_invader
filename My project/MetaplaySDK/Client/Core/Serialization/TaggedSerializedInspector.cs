// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Math;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Serialization
{
    public class TaggedSerializedInspector : TaggedWirePushParser
    {
        public class ObjectInfo
        {
            public readonly struct MemberInfo
            {
                public readonly int TagId;

                /// <summary>
                /// Name of the member if available. <c>null</c> otherwise.
                /// </summary>
                public readonly string Name;

                public readonly ObjectInfo ObjectInfo;

                public MemberInfo(int tagId, string name, ObjectInfo objectInfo)
                {
                    TagId = tagId;
                    Name = name;
                    ObjectInfo = objectInfo;
                }
            }

            /// <summary>
            /// Wiretype of the object.
            /// </summary>
            public WireDataType WireType;

            public bool IsPrimitive
            {
                get
                {
                    switch (WireType)
                    {
                        case WireDataType.Null:
                        case WireDataType.VarInt:
                        case WireDataType.VarInt128:
                        case WireDataType.F32:
                        case WireDataType.F32Vec2:
                        case WireDataType.F32Vec3:
                        case WireDataType.F64:
                        case WireDataType.F64Vec2:
                        case WireDataType.F64Vec3:
                        case WireDataType.Float32:
                        case WireDataType.Float64:
                        case WireDataType.String:
                        case WireDataType.Bytes:
                        case WireDataType.MetaGuid:
                        case WireDataType.NullableVarInt:
                        case WireDataType.NullableVarInt128:
                        case WireDataType.NullableF32:
                        case WireDataType.NullableF32Vec2:
                        case WireDataType.NullableF32Vec3:
                        case WireDataType.NullableF64:
                        case WireDataType.NullableF64Vec2:
                        case WireDataType.NullableF64Vec3:
                        case WireDataType.NullableFloat32:
                        case WireDataType.NullableFloat64:
                        case WireDataType.NullableMetaGuid:
                            return true;

                        default:
                            return false;
                    }
                }
            }

            /// <summary>
            /// If this is a primitive wire value, then the corresponding CLR type, otherwise null.
            /// Mapping is as follows (Note the signedness of ints):
            /// <code>
            /// Null:       null
            /// Nullable*:  null | primitiveType
            /// VarInt:     Long
            /// VarInt128:  MetaUInt128
            /// F32:        F32
            /// F32Vec2:    F32Vec2
            /// F32Vec3:    F32Vec3
            /// F64:        F64
            /// F64Vec2:    F64Vec2
            /// F64Vec3:    F64Vec3
            /// Float32:    float
            /// Float64:    double
            /// String:     string
            /// Bytes:      byte[]
            /// MetaGuid:   MetaGuid
            /// </code>
            /// </summary>
            public object PrimitiveValue;

            /// <summary>
            /// TypeCode if AbstractStruct, 0 for null AbstractStruct, and null otherwise.
            /// </summary>
            public int? TypeCode;

            /// <summary>
            /// If of type *Struct and not null, members by tag. Otherwise null.
            /// </summary>
            public List<MemberInfo> Members;

            /// <summary>
            /// If ValueCollection and not null, values in order. Otherwise null.
            /// </summary>
            public List<ObjectInfo> ValueCollection;

            /// <summary>
            /// If KeyValueCollection and not null, keys and values in order. Otherwise null.
            /// </summary>
            public OrderedDictionary<ObjectInfo, ObjectInfo> KeyValueCollection;

            /// <summary>
            /// Start offset of the first byte of the wire object. This includes potential headers.
            /// </summary>
            public int EnvelopeStartOffset;

            /// <summary>
            /// End offset of the first byte of the wire object. This includes potential trailers.
            /// </summary>
            public int EnvelopeEndOffset;

            /// <summary>
            /// Start offset of the first byte of object payload.
            /// </summary>
            public int PayloadStartOffset;

            /// <summary>
            /// End offset of the first byte of object payload.
            /// </summary>
            public int PayloadEndOffset;

            /// <summary>
            /// The serialization metadata matching the wire stucture. If there is no serialization structure, or it does
            /// not match, this will be null. Additionally, this is null for PrimitiveTypes <see cref="TaggedWireSerializer.IsBuiltinType(Type)" />
            /// </summary>
            public MetaSerializableType SerializableType;

            /// <summary>
            /// The serialization metadata type for for the primitive type, or null if not a primitive type or type cannot be mapped.
            /// </summary>
            public Type ProjectedPrimitiveType;

            /// <summary>
            /// The privitive value projected into the serialization metadata type. For integer types (including booleans), this maps the wire representation
            /// into the signedness of the serialization metadata. Null, if not a primitive type or type cannot be mapped.
            /// </summary>
            public object ProjectedPrimitiveValue;

            public ObjectInfo(WireDataType wireType, MetaSerializableType typeSpec)
            {
                WireType = wireType;
                SerializableType = typeSpec;
            }

            // explicitly defined for clarity
            public override bool Equals(object obj) => ReferenceEquals(this, obj);
            public override int GetHashCode() => base.GetHashCode();
        }

        List<ObjectInfo> _stack = new List<ObjectInfo>();
        List<ObjectInfo> _keyStack = new List<ObjectInfo>();
        ObjectInfo _result;
        int _memberTagId;
        string _memberName;
        bool _parsingKvKey;
        int _valueIndex;
        WireDataType _currentNullablePrimitiveType;
        int _lastStructPayloadEnd;
        int _consumedUpTo; // we keep track of last consume end instead of previous begin since Member<Nullable<Primitive>> contains 2 envelopes before content and this makes collapsing headers easy.
        MetaSerializableType _nextTypeSpec;
        Type _nextRawType;

        TaggedSerializedInspector()
        {
        }

        /// <summary>
        /// Inspects wire object available on the Reader. If object cannot be read, throws an <see cref="ParseError"/>.
        /// If <paramref name="topLevelType"/> is given, the inspection attempts to fill in <see cref="ObjectInfo.SerializableType"/>
        /// fields.
        /// </summary>
        public static ObjectInfo Inspect(IOReader reader, Type topLevelType = null, bool checkReaderWasCompletelyConsumed = false)
        {
            TaggedSerializedInspector inspector = new TaggedSerializedInspector();
            inspector.SetNextType(topLevelType);
            inspector.Parse(reader);
            if (checkReaderWasCompletelyConsumed && !reader.IsFinished)
                throw new ParseError("Trailing data. Reader was not completely consumed.");
            return inspector._result;
        }

        void Push(WireDataType wireType, MetaSerializableType typeSpec)
        {
            ObjectInfo obj = new ObjectInfo(wireType, typeSpec);
            ObjectInfo parent = Top;

            // KV container keys become subobjects when Value is pushed
            // so don't push into parent object yet.
            if (_parsingKvKey)
            {
                _parsingKvKey = false;
            }
            // A non KV-container non-Key objects become subobjects immediately
            else if (parent != null)
            {
                if (parent.Members != null)
                    parent.Members.Add(new ObjectInfo.MemberInfo(_memberTagId, _memberName, obj));
                else if (parent.ValueCollection != null)
                {
                    if (parent.ValueCollection.Count != _valueIndex)
                        throw new ParseError("desync of ValueCollection elems");
                    parent.ValueCollection.Add(obj);
                }
                else if (parent.KeyValueCollection != null)
                {
                    if (parent.KeyValueCollection.Count != _valueIndex)
                        throw new ParseError("desync of KeyValueCollection elems");
                    if (_keyStack.Count == 0)
                        throw new ParseError("desync of KeyValueCollection keys");
                    parent.KeyValueCollection.Add(_keyStack[_keyStack.Count - 1], obj);
                    _keyStack.RemoveAt(_keyStack.Count - 1);
                }
            }
            else
            {
                // Root object is the result. Store in a separate value
                // so that we can Pop this object like the rest.
                _result = obj;
            }

            _stack.Add(obj);
        }

        void Pop()
        {
            if (_stack.Count == 0)
                throw new ParseError("stack underflow");
            _stack.RemoveAt(_stack.Count - 1);
        }

        ObjectInfo Top
        {
            get
            {
                if (_stack.Count == 0)
                    return null;
                return _stack[_stack.Count - 1];
            }
        }

        /// <summary>
        /// Returns the SerializationSpec of the top type. If top spec is Nullable<>, returns the concrete type. Otherwise
        /// returns null.
        /// </summary>
        MetaSerializableType TopSerializeableSpecAsConcreteSpec
        {
            get
            {
                ObjectInfo top = Top;
                if (top == null)
                    return null;
                if (top.SerializableType == null)
                    return null;

                if (!top.SerializableType.IsSystemNullable)
                    return top.SerializableType;

                Type elementType = top.SerializableType.Type.GetSystemNullableElementType();
                if (!MetaSerializerTypeRegistry.TryGetTypeSpec(elementType, out MetaSerializableType typeSpec))
                    return null;
                return typeSpec;
            }
        }

        void SetNextType(Type type)
        {
            if (type != null)
            {
                if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType typeSpec))
                {
                    _nextTypeSpec = typeSpec;
                    _nextRawType = null;
                }
                else
                {
                    _nextTypeSpec = null;
                    _nextRawType = type;
                }
            }
            else
            {
                _nextTypeSpec = null;
                _nextRawType = null;
            }
        }

        static object TryProjectValue(Type type, object wireObj)
        {
            ulong UnswizzleLong(long l)
            {
                return (ulong)((l >> 63) ^ (l << 1));
            }

            if (wireObj is long wireLong)
            {
                if (type == typeof(bool)        || type == typeof(bool?))       return wireLong != 0;
                if (type == typeof(sbyte)       || type == typeof(sbyte?))      return (sbyte)wireLong;
                if (type == typeof(byte)        || type == typeof(byte?))       return (byte)UnswizzleLong(wireLong);
                if (type == typeof(short)       || type == typeof(short?))      return (short)wireLong;
                if (type == typeof(ushort)      || type == typeof(ushort?))     return (ushort)UnswizzleLong(wireLong);
                if (type == typeof(char)        || type == typeof(char?))       return (char)wireLong;
                if (type == typeof(int)         || type == typeof(int?))        return (int)wireLong;
                if (type == typeof(uint)        || type == typeof(uint?))       return (uint)UnswizzleLong(wireLong);
                if (type == typeof(long)        || type == typeof(long?))       return wireLong;
                if (type == typeof(ulong)       || type == typeof(ulong?))      return UnswizzleLong(wireLong);
                if (type == typeof(EntityKind)  || type == typeof(EntityKind?)) return EntityKind.FromValue((int)wireLong);
            }
            else if (wireObj is MetaUInt128 wireU128)
            {
                if (type == typeof(MetaUInt128) || type == typeof(MetaUInt128?)) return wireU128;
            }
            return null;
        }

        protected override void OnBeginParsePrimitive(WireDataType wireType)
        {
            // Encode nullable-wrapper into the primitive.
            WireDataType typeWithWrapper = _currentNullablePrimitiveType == WireDataType.Invalid ? wireType : _currentNullablePrimitiveType;

            Push(typeWithWrapper, _nextTypeSpec);
            Top.EnvelopeStartOffset = _consumedUpTo;
            Top.PayloadStartOffset = Reader.Offset;
        }

        protected override void OnEndParsePrimitive(WireDataType wireType, object obj)
        {
            Top.PrimitiveValue = obj;
            Top.PayloadEndOffset = Reader.Offset;
            Top.EnvelopeEndOffset = Reader.Offset;
            _consumedUpTo = Reader.Offset;

            // Project primitive type & value back from wire format to metadata format.
            if (_nextRawType != null)
            {
                if (TaggedWireSerializer.IsBuiltinType(_nextRawType) && TaggedWireSerializer.GetWireType(_nextRawType) == wireType)
                {
                    Top.ProjectedPrimitiveType = _nextRawType;
                    Top.ProjectedPrimitiveValue = TryProjectValue(_nextRawType, obj);
                }
            }
        }

        protected override void OnBeginParseAbstractStruct(int typecode)
        {
            MetaSerializableType typeSpec = null;
            Type derivedType = null;
            if (typecode != 0
                && (_nextTypeSpec?.DerivedTypes?.TryGetValue(typecode, out derivedType) ?? false)
                && MetaSerializerTypeRegistry.TryGetTypeSpec(derivedType, out MetaSerializableType derivedSpec))
            {
                typeSpec = derivedSpec;
            }
            else
            {
                // default to baseclass
                typeSpec = _nextTypeSpec;
            }

            Push(WireDataType.AbstractStruct, typeSpec);
            Top.TypeCode = typecode;
            if (typecode != 0)
                Top.Members = new List<ObjectInfo.MemberInfo>();
            HandleStructStartCommon();
        }

        protected override void OnBeginParseNullableStruct(bool isNotNull)
        {
            Push(WireDataType.NullableStruct, _nextTypeSpec);
            if (isNotNull)
                Top.Members = new List<ObjectInfo.MemberInfo>();
            HandleStructStartCommon();
        }

        protected override void OnBeginParseStruct()
        {
            Push(WireDataType.Struct, _nextTypeSpec);
            Top.Members = new List<ObjectInfo.MemberInfo>();
            HandleStructStartCommon();
        }

        void HandleStructStartCommon()
        {
            Top.EnvelopeStartOffset = _consumedUpTo;
            Top.PayloadStartOffset = Reader.Offset;
            _lastStructPayloadEnd = Reader.Offset;

            // there is no consume here yet, but move marker already to avoid child entry marking this header as its own.
            _consumedUpTo = Reader.Offset;
        }

        protected override void OnEndParseStruct()
        {
            Top.PayloadEndOffset = _lastStructPayloadEnd;
            Top.EnvelopeEndOffset = Reader.Offset;
            _consumedUpTo = Reader.Offset;

            // values do not pop themselves
        }

        protected override void OnBeginParseValueCollection(int numElements, WireDataType valueType)
        {
            Push(WireDataType.ValueCollection, _nextTypeSpec);
            if (numElements >= 0)
                Top.ValueCollection = new List<ObjectInfo>(capacity: numElements);

            Top.EnvelopeStartOffset = _consumedUpTo;
            Top.PayloadStartOffset = Reader.Offset;

            // there is no consume here yet, but move marker already to avoid child entry marking this header as its own.
            _consumedUpTo = Reader.Offset;
        }

        protected override void OnEndParseValueCollection()
        {
            Top.PayloadEndOffset = Reader.Offset;
            Top.EnvelopeEndOffset = Reader.Offset;
            _consumedUpTo = Reader.Offset;

            // values do not pop themselves
        }

        protected override void OnBeginParseKeyValueCollection(int numElements, WireDataType keyType, WireDataType valueType)
        {
            Push(WireDataType.KeyValueCollection, _nextTypeSpec);
            if (numElements >= 0)
                Top.KeyValueCollection = new OrderedDictionary<ObjectInfo, ObjectInfo>(capacity: numElements);

            Top.EnvelopeStartOffset = _consumedUpTo;
            Top.PayloadStartOffset = Reader.Offset;

            // there is no consume here yet, but move marker already to avoid child entry marking this header as its own.
            _consumedUpTo = Reader.Offset;
        }

        protected override void OnEndParseKeyValueCollection()
        {
            Top.PayloadEndOffset = Reader.Offset;
            Top.EnvelopeEndOffset = Reader.Offset;
            _consumedUpTo = Reader.Offset;

            // values do not pop themselves
        }

        protected override void OnBeginParseStructMember(WireDataType wireType, int tagId)
        {
            _memberTagId = tagId;
            _memberName = null;

            MetaSerializableMember memberInfo = null;
            if (TopSerializeableSpecAsConcreteSpec?.MemberByTagId?.TryGetValue(tagId, out memberInfo) ?? false)
            {
                _memberName = memberInfo.Name;
                SetNextType(memberInfo.Type);
            }
            else
            {
                SetNextType(null);
            }
        }

        protected override void OnEndParseStructMember()
        {
            Pop();
            _lastStructPayloadEnd = Reader.Offset;
            // \note: parser will next call OnEndParseStruct
        }

        protected override void OnBeginParseCollectionElement(int ndx)
        {
            _valueIndex = ndx;

            if (TopSerializeableSpecAsConcreteSpec?.IsCollection ?? false)
            {
                Type elementType = TopSerializeableSpecAsConcreteSpec.Type.GetCollectionElementType();
                SetNextType(elementType);
            }
            else
            {
                SetNextType(null);
            }
        }

        protected override void OnEndParseCollectionElement()
        {
            Pop();
        }

        protected override void OnBeginParseCollectionKey(int ndx)
        {
            // Prevent KV key from being added into parent object yet
            _parsingKvKey = true;

            if (TopSerializeableSpecAsConcreteSpec?.Type.IsDictionary() ?? false)
            {
                Type keyType = TopSerializeableSpecAsConcreteSpec.Type.GetDictionaryKeyAndValueTypes().KeyType;
                SetNextType(keyType);
            }
            else
            {
                SetNextType(null);
            }
        }

        protected override void OnEndParseCollectionKey()
        {
            // Move Key into a separate stack
            _keyStack.Add(Top);
            Pop();
        }

        protected override void OnBeginParseCollectionValue(int ndx)
        {
            _valueIndex = ndx;

            if (TopSerializeableSpecAsConcreteSpec?.Type.IsDictionary() ?? false)
            {
                Type valueType = TopSerializeableSpecAsConcreteSpec.Type.GetDictionaryKeyAndValueTypes().ValueType;
                SetNextType(valueType);
            }
            else
            {
                SetNextType(null);
            }
        }

        protected override void OnEndParseCollectionValue()
        {
            Pop();
        }

        protected override void OnBeginParseNullablePrimitive(WireDataType dataType, bool isNotNull)
        {
            _currentNullablePrimitiveType = dataType;
        }

        protected override void OnEndParseNullablePrimitive()
        {
            _currentNullablePrimitiveType = WireDataType.Invalid;
        }
    }
}
