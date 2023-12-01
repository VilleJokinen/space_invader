// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using System;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Push parser for tagged wire serialization data. Parser parses the input in one go, pushing the parse
    /// events to callbacks as it parses. This is essentially a SAX parser but for Tagged Serialization format.
    /// </summary>
    public abstract class TaggedWirePushParser
    {
        public class ParseError : Exception
        {
            public ParseError(string message) : base(message)
            {
            }
        }

        protected IOReader Reader { get; private set; }

        /// <summary>
        /// Parses the data with the reader
        /// </summary>
        protected void Parse(IOReader reader)
        {
            Reader = reader;

            try
            {
                ParseTopLevel();
            }
            catch (Exception ex)
            {
                if (!OnError(ex))
                    throw;
            }
        }

        protected virtual void OnEnd() { }

        /// <summary>
        /// Returns true if error was handled. If this returns false, the exception is thrown from <see cref="Parse"/>.
        /// </summary>
        protected virtual bool OnError(Exception ex) => false;
        protected virtual void OnBeginParsePrimitive(WireDataType wireType) { }
        protected virtual void OnEndParsePrimitive(WireDataType wireType, object obj) { }
        protected virtual void OnBeginParseAbstractStruct(int typecode) { }
        protected virtual void OnBeginParseNullableStruct(bool isNotNull) { }
        protected virtual void OnBeginParseStruct() { }
        protected virtual void OnEndParseStruct() { }
        protected virtual void OnBeginParseValueCollection(int numElements, WireDataType valueType) { }
        protected virtual void OnEndParseValueCollection() { }
        protected virtual void OnBeginParseKeyValueCollection(int numElements, WireDataType keyType, WireDataType valueType) { }
        protected virtual void OnEndParseKeyValueCollection() { }
        protected virtual void OnBeginParseStructMember(WireDataType wireType, int tagId) { }
        protected virtual void OnEndParseStructMember() { }
        protected virtual void OnBeginParseCollectionElement(int ndx) { }
        protected virtual void OnEndParseCollectionElement() { }
        protected virtual void OnBeginParseCollectionKey(int ndx) { }
        protected virtual void OnEndParseCollectionKey() { }
        protected virtual void OnBeginParseCollectionValue(int ndx) { }
        protected virtual void OnEndParseCollectionValue() { }
        protected virtual void OnBeginParseNullablePrimitive(WireDataType dataType, bool isNotNull) { }
        protected virtual void OnEndParseNullablePrimitive() { }

        void ParseTopLevel()
        {
            WireDataType topLevelWireType = TaggedWireSerializer.ReadWireType(Reader);
            ParseWireObjectContents(topLevelWireType);
            OnEnd();
        }

        void ParseWireObjectContents(WireDataType wireType)
        {
            switch (wireType)
            {
                case WireDataType.Null:         OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, null);                               break;
                case WireDataType.VarInt:       OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadVarLong());               break;
                case WireDataType.VarInt128:    OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadVarUInt128());            break;
                case WireDataType.F32:          OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF32());                   break;
                case WireDataType.F32Vec2:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF32Vec2());               break;
                case WireDataType.F32Vec3:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF32Vec3());               break;
                case WireDataType.F64:          OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF64());                   break;
                case WireDataType.F64Vec2:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF64Vec2());               break;
                case WireDataType.F64Vec3:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadF64Vec3());               break;
                case WireDataType.Float32:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadFloat());                 break;
                case WireDataType.Float64:      OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadDouble());                break;
                case WireDataType.String:       OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadString(64 * 1024 * 1024));        break;
                case WireDataType.Bytes:        OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, Reader.ReadByteString(64 * 1024 * 1024));    break;
                case WireDataType.MetaGuid:     OnBeginParsePrimitive(wireType); OnEndParsePrimitive(wireType, new MetaGuid(Reader.ReadUInt128())); break;

                case WireDataType.AbstractStruct:
                {
                    int typeCode = Reader.ReadVarInt();
                    OnBeginParseAbstractStruct(typeCode);
                    if (typeCode == 0)
                    {
                        // null
                    }
                    else if (typeCode > 0)
                        ParseStructContents();
                    else
                        throw new ParseError($"Invalid AbstractStruct typecode. Must be non-negative, got {typeCode}");
                    OnEndParseStruct();
                    break;
                }

                case WireDataType.NullableStruct:
                {
                    bool isNotNull = ParseIsNotNullFlag();
                    OnBeginParseNullableStruct(isNotNull);
                    if (isNotNull)
                        ParseStructContents();
                    OnEndParseStruct();
                    break;
                }
                case WireDataType.Struct:
                {
                    OnBeginParseStruct();
                    ParseStructContents();
                    OnEndParseStruct();
                    break;
                }

                case WireDataType.ValueCollection:
                {
                    int numElements = Reader.ReadVarInt();
                    if (numElements == -1)
                    {
                        OnBeginParseValueCollection(numElements, WireDataType.Invalid);
                    }
                    else if (numElements >= 0)
                    {
                        WireDataType elementType = TaggedWireSerializer.ReadWireType(Reader);
                        OnBeginParseValueCollection(numElements, elementType);
                        for (int ndx = 0; ndx < numElements; ndx++)
                        {
                            OnBeginParseCollectionElement(ndx);
                            ParseWireObjectContents(elementType);
                            OnEndParseCollectionElement();
                        }
                    }
                    else
                        throw new ParseError($"Invalid ValueCollection count. Must be non-negative, got {numElements}");
                    OnEndParseValueCollection();
                    break;
                }

                case WireDataType.KeyValueCollection:
                {
                    int numElements = Reader.ReadVarInt();
                    if (numElements == -1)
                    {
                        OnBeginParseKeyValueCollection(numElements, WireDataType.Invalid, WireDataType.Invalid);
                    }
                    else if (numElements >= 0)
                    {
                        WireDataType keyType = TaggedWireSerializer.ReadWireType(Reader);
                        WireDataType valueType = TaggedWireSerializer.ReadWireType(Reader);
                        OnBeginParseKeyValueCollection(numElements, keyType, valueType);
                        for (int ndx = 0; ndx < numElements; ndx++)
                        {
                            OnBeginParseCollectionKey(ndx);
                            ParseWireObjectContents(keyType);
                            OnEndParseCollectionKey();

                            OnBeginParseCollectionValue(ndx);
                            ParseWireObjectContents(valueType);
                            OnEndParseCollectionValue();
                        }
                    }
                    else
                        throw new ParseError($"Invalid KeyValueCollection count. Must be non-negative, got {numElements}");
                    OnEndParseKeyValueCollection();
                    break;
                }

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
                {
                    WireDataType type = TaggedWireSerializer.NullablePrimitiveWireTypeUnwrap(wireType);
                    bool isNotNull = ParseIsNotNullFlag();

                    OnBeginParseNullablePrimitive(wireType, isNotNull);
                    if (isNotNull)
                        ParseWireObjectContents(type);
                    else
                    {
                        OnBeginParsePrimitive(wireType);
                        OnEndParsePrimitive(wireType, null);
                    }
                    OnEndParseNullablePrimitive();
                    break;
                }

                default:
                    throw new ParseError($"invalid wireformat: got {wireType}");
            }
        }

        void ParseStructContents()
        {
            for (;;)
            {
                WireDataType memberType = TaggedWireSerializer.ReadWireType(Reader);
                if (memberType == WireDataType.EndStruct)
                    break;

                int tagId = Reader.ReadVarInt();
                if (tagId < 0)
                    throw new ParseError($"Invalid struct member tagId. Must be non-negative, got {tagId}");

                OnBeginParseStructMember(memberType, tagId);
                ParseWireObjectContents(memberType);
                OnEndParseStructMember();
            }
        }

        bool ParseIsNotNullFlag()
        {
            int isNotNull = Reader.ReadByte();
            if (isNotNull == 0)
                return false;
            else if (isNotNull == 2) // true == -1 => swizzled into uint => 2
                return true;
            else
                throw new ParseError($"Invalid null-flag. Must be bool, got {isNotNull}");
        }
    }
}
