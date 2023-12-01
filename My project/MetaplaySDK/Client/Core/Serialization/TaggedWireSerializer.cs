// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Stores some serialized along with the flags used to encode it. Useful for wrapping Model-level data inside
    /// <see cref="MetaMessage"/>s, useful for avoiding duplicate deserialization-reserialization when messages are
    /// handled in interim actors.
    /// </summary>
    /// <typeparam name="T">Type of serialized payload</typeparam>
    [MetaSerializable]
    public struct MetaSerialized<T>
    {
        [MetaMember(1)] public byte[]                   Bytes   { get; private set; }
        [MetaMember(2)] public MetaSerializationFlags   Flags   { get; private set; }

        public bool IsValid => Bytes != null;
        public bool IsEmpty => Bytes == null;

        public static readonly MetaSerialized<T> Empty = new MetaSerialized<T>(null, MetaSerializationFlags.IncludeAll);

        public MetaSerialized(T value, MetaSerializationFlags flags, int? logicVersion)
        {
            Bytes = MetaSerialization.SerializeTagged(value, flags, logicVersion);
            Flags = flags;
        }

        public MetaSerialized(byte[] bytes, MetaSerializationFlags flags)
        {
            Bytes = bytes;
            Flags = flags;
        }

        public T Deserialize(IGameConfigDataResolver resolver, int? logicVersion)
        {
            try
            {
                return MetaSerialization.DeserializeTagged<T>(Bytes, Flags, resolver, logicVersion);
            }
            catch (Exception ex)
            {
                int     bytesLen = Bytes?.Length ?? 0;
                string  bytesStr = (Bytes != null) ? Convert.ToBase64String(Bytes) : "null";

                if (typeof(MetaMessage).IsAssignableFrom(typeof(T)))
                {
                    string msgName = MetaSerializationUtil.PeekMessageName(Bytes, offset: 0);
                    throw new MetaSerializationException($"Exception while deserializing MetaSerialized<{typeof(T)}> (msgType={msgName}, flags={Flags}, size={bytesLen}): Bytes={bytesStr}.", ex);
                }
                else
                    throw new MetaSerializationException($"Exception while deserializing MetaSerialized<{typeof(T)}> (flags={Flags}, size={bytesLen}): Bytes={bytesStr}.", ex);
            }
        }

        public override string ToString()
        {
            if (Bytes != null)
                return Invariant($"MetaSerialized<{typeof(T).Name}>(size={Bytes.Length}, flags={Flags})");
            else
                return $"MetaSerialized<{typeof(T).Name}>(bytes=null, flag={Flags})";
        }
    }

    // TaggedWireSerializer

    public static class TaggedWireSerializer
    {
        static void WriteTagId(IOWriter writer, int tagId)
        {
            MetaDebug.Assert(tagId > 0, "Writing invalid tagId to stream {0}", tagId);
            //DebugLog.Verbose("@{0} Write tag: {1}", writer.Offset, tagId);
            writer.WriteVarInt(tagId);
        }

        public static int ReadTagId(IOReader reader)
        {
            //int pos = reader.Offset;
            int tagId = reader.ReadVarInt();
            //DebugLog.Verbose("@{0} Read tag: {1}", pos, tagId);
            return tagId;
        }

        public static void WriteWireType(IOWriter writer, WireDataType wireType)
        {
            //DebugLog.Verbose("@{0} Write type: {1}", writer.Offset, wireType);
            MetaDebug.Assert(wireType != WireDataType.Invalid, "Trying to write Invalid wire type");
            writer.WriteByte((byte)wireType);
        }

        public static WireDataType ReadWireType(IOReader reader)
        {
            //int pos = reader.Offset;
            WireDataType wireType = (WireDataType)reader.ReadByte();
            //DebugLog.Verbose("@{0} Read type: {1}", pos, wireType);
            return wireType;
        }

        public static void ExpectWireType(ref MetaSerializationContext context, IOReader reader, Type type, WireDataType encounteredWireType, WireDataType expectedWireType)
        {
            if (encounteredWireType != expectedWireType)
            {
                string expectedWireTypes = expectedWireType.ToString();

                if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType typeSpec)
                    && typeSpec.DeserializationConverters.Length > 0)
                {
                    foreach (MetaDeserializationConverter converter in typeSpec.DeserializationConverters)
                        expectedWireTypes += $", or {converter.AcceptedWireDataType} (accepted by converter {converter})";
                }

                string message = Invariant($"Unexpected WireType {encounteredWireType} when reading value of type {type.ToGenericTypeString()} at offset {reader.Offset}, expected {expectedWireTypes}");
                if (!string.IsNullOrWhiteSpace(context.MemberContext.MemberName))
                    message = Invariant($"Failed to deserialize {context.MemberContext.MemberName}, did you change the type?{Environment.NewLine}") + message;
                throw new MetaWireDataTypeMismatchDeserializationException(message, context.MemberContext.MemberName, attemptedType: type, expectedWireDataType: expectedWireType, encounteredWireDataType: encounteredWireType);
            }
        }

        static readonly Dictionary<Type, WireDataType> s_typeToWireType = new Dictionary<Type, WireDataType>
        {
            { typeof(bool),     WireDataType.VarInt },
            { typeof(bool?),    WireDataType.NullableVarInt },
            { typeof(sbyte),    WireDataType.VarInt },
            { typeof(sbyte?),   WireDataType.NullableVarInt },
            { typeof(byte),     WireDataType.VarInt },
            { typeof(byte?),    WireDataType.NullableVarInt },
            { typeof(short),    WireDataType.VarInt },
            { typeof(short?),   WireDataType.NullableVarInt },
            { typeof(ushort),   WireDataType.VarInt },
            { typeof(ushort?),  WireDataType.NullableVarInt },
            { typeof(char),     WireDataType.VarInt },
            { typeof(char?),    WireDataType.NullableVarInt },
            { typeof(int),      WireDataType.VarInt },
            { typeof(int?),     WireDataType.NullableVarInt },
            { typeof(uint),     WireDataType.VarInt },
            { typeof(uint?),    WireDataType.NullableVarInt },
            { typeof(long),     WireDataType.VarInt },
            { typeof(long?),    WireDataType.NullableVarInt },
            { typeof(ulong),    WireDataType.VarInt },
            { typeof(ulong?),   WireDataType.NullableVarInt },
            { typeof(MetaUInt128),  WireDataType.VarInt128 },
            { typeof(MetaUInt128?), WireDataType.NullableVarInt128 },
            { typeof(F32),      WireDataType.F32 },
            { typeof(F32?),     WireDataType.NullableF32 },
            { typeof(F32Vec2),  WireDataType.F32Vec2 },
            { typeof(F32Vec2?), WireDataType.NullableF32Vec2 },
            { typeof(F32Vec3),  WireDataType.F32Vec3 },
            { typeof(F32Vec3?), WireDataType.NullableF32Vec3 },
            { typeof(F64),      WireDataType.F64 },
            { typeof(F64?),     WireDataType.NullableF64 },
            { typeof(F64Vec2),  WireDataType.F64Vec2 },
            { typeof(F64Vec2?), WireDataType.NullableF64Vec2 },
            { typeof(F64Vec3),  WireDataType.F64Vec3 },
            { typeof(F64Vec3?), WireDataType.NullableF64Vec3 },
            { typeof(float),    WireDataType.Float32 },
            { typeof(float?),   WireDataType.NullableFloat32 },
            { typeof(double),   WireDataType.Float64 },
            { typeof(double?),  WireDataType.NullableFloat64 },
            { typeof(string),   WireDataType.String },
            { typeof(byte[]),   WireDataType.Bytes },
            { typeof(MetaGuid), WireDataType.MetaGuid },
            { typeof(MetaGuid?), WireDataType.NullableMetaGuid },
            { typeof(EntityKind), WireDataType.VarInt },
            { typeof(EntityKind?), WireDataType.NullableVarInt },
            { typeof(Guid), WireDataType.Bytes },
            { typeof(Guid?), WireDataType.Bytes },
            { typeof(TimeSpan), WireDataType.VarInt },
            { typeof(TimeSpan?), WireDataType.NullableVarInt },
        };

        public static bool IsBuiltinType(Type type)
        {
            if (s_typeToWireType.ContainsKey(type))
                return true;

            return false;
        }

        public static WireDataType GetWireType(Type type)
        {
            return GetWireType(type, MetaSerializerTypeRegistry.TypeInfo.Specs);
        }

        public static WireDataType GetWireType(Type type, Dictionary<Type, MetaSerializableType> typeRegistry)
        {
            if (s_typeToWireType.TryGetValue(type, out WireDataType wireType))
                return wireType;
            else if (type.ImplementsInterface<IGameConfigData>()) // IGameConfigData classes serialize with their ConfigKey type (note: checking for non-generic interface first to avoid garbage)
                return GetWireType(type.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0], typeRegistry); // \todo [petri] this calls Type.GetInterfaces() which allocates memory, cache the result?
            else if (typeRegistry.TryGetValue(type, out MetaSerializableType typeSpec) && typeSpec.WireType != WireDataType.Invalid)
                return typeSpec.WireType;
            else if (type.ImplementsInterface<IStringId>())
                return WireDataType.String;
#if NETCOREAPP // cloud
            else if (type == typeof(Akka.Actor.IActorRef))
                return WireDataType.String;
#endif
            else if (type.IsClass)
            {
                if (type.IsCollection())
                {
                    if (type.IsDictionary())
                        return WireDataType.KeyValueCollection;
                    else
                        return WireDataType.ValueCollection;
                }
            }

            throw new NotImplementedException($"Unable to resolve WireType for {type.ToGenericTypeString()}");
        }

        static void SkipStructMembers(IOReader reader)
        {
            // Iterate over members
            for (;;)
            {
                WireDataType memberWireType = ReadWireType(reader);
                if (memberWireType == WireDataType.EndStruct)
                    break;
                _ = ReadTagId(reader);
                SkipWireType(reader, memberWireType);
            }
        }

        public static void SkipWireType(IOReader reader, WireDataType wireType)
        {
            switch (wireType)
            {
                // Null value
                case WireDataType.Null:
                    break;

                // All integer types
                case WireDataType.VarInt:
                    reader.ReadVarLong();
                    break;

                case WireDataType.VarInt128:
                    reader.ReadVarUInt128();
                    break;

                case WireDataType.F32:
                    reader.ReadF32();
                    break;

                case WireDataType.F32Vec2:
                    reader.ReadF32Vec2();
                    break;

                case WireDataType.F32Vec3:
                    reader.ReadF32Vec3();
                    break;

                case WireDataType.F64:
                    reader.ReadF64();
                    break;

                case WireDataType.F64Vec2:
                    reader.ReadF64Vec2();
                    break;

                case WireDataType.F64Vec3:
                    reader.ReadF64Vec3();
                    break;

                case WireDataType.Float32:
                    reader.SkipBytes(4);
                    break;

                case WireDataType.Float64:
                    reader.SkipBytes(8);
                    break;

                // Nullable variants of primitives
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
                    WireDataType underlyingType = NullablePrimitiveWireTypeUnwrap(wireType);
                    if (reader.ReadVarInt() != 0)
                        SkipWireType(reader, underlyingType);
                    break;
                }

                case WireDataType.MetaGuid:
                    reader.SkipBytes(16);
                    break;

                // Size-prefixed (in bytes) bytes (byte[], string, etc.)
                case WireDataType.String:
                case WireDataType.Bytes:
                    int numBytes = reader.ReadVarInt();
                    if (numBytes > 0)
                        reader.SkipBytes(numBytes);
                    break;

                // Abstract class: TypeCode <> (FieldType <> FieldTagID <> FieldValue)* <> EndStruct
                case WireDataType.AbstractStruct:
                    int typeCode = reader.ReadVarInt();
                    if (typeCode != 0)
                        SkipStructMembers(reader);
                    break;

                // Nullable struct (class): IsNotNull <> (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
                case WireDataType.NullableStruct:
                    if (reader.ReadByte() != 0)
                        SkipStructMembers(reader);
                    break;

                // Struct (or class): (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
                case WireDataType.Struct:
                    SkipStructMembers(reader);
                    break;

                // List/set of values: Length <> ElemType <> Element0, ...
                case WireDataType.ValueCollection:
                {
                    // -1 represents null, in which case the types are not written
                    int count = reader.ReadVarInt();
                    if (count >= 0)
                    {
                        WireDataType elemType = ReadWireType(reader);
                        for (int ndx = 0; ndx < count; ndx++)
                            SkipWireType(reader, elemType);
                    }
                    break;
                }

                // Dictionary of values: Length <> KeyType <> ValueType <> Key0, Value0, Key1, ...
                case WireDataType.KeyValueCollection:
                {
                    // -1 represents null, in which case the types are not written
                    int count = reader.ReadVarInt();
                    if (count >= 0)
                    {
                        WireDataType keyType    = ReadWireType(reader);
                        WireDataType valueType  = ReadWireType(reader);

                        for (int ndx = 0; ndx < count; ndx++)
                        {
                            SkipWireType(reader, keyType);
                            SkipWireType(reader, valueType);
                        }
                    }
                    break;
                }

                default:
                    throw new MetaSerializationException($"Unknown wire type at offset {reader.Offset - 1}: {wireType}");
            }
        }

        public static void TestSkip(IOReader reader)
        {
            WireDataType wireType = ReadWireType(reader);
            SkipWireType(reader, wireType);
        }

        public static void TestSkip(byte[] serialized)
        {
            using (IOReader reader = new IOReader(serialized))
                TestSkip(reader);
        }

        public static void ToStringMembers(StringBuilder sb, IOReader reader, int depth)
        {
            string indent = new string(' ', depth * 2);
            for (;;)
            {
                int offset = reader.Offset;
                WireDataType memberWireType = ReadWireType(reader);
                if (memberWireType == WireDataType.EndStruct)
                    break;
                int tagId = ReadTagId(reader);
                sb.Append(Invariant($"{indent}@{offset} #{tagId} = "));
                ToString(sb, reader, depth, memberWireType);
                sb.Append("\n");
            }
        }

        public static void ToString(StringBuilder sb, IOReader reader, int depth, WireDataType wireType)
        {
            string indent = new string(' ', depth * 2);
            switch (wireType)
            {
                // Null value
                case WireDataType.Null:
                    sb.Append("null");
                    break;

                // All integer types
                case WireDataType.VarInt:
                    sb.Append(reader.ReadVarLong().ToString(CultureInfo.InvariantCulture));
                    break;

                case WireDataType.VarInt128:
                    sb.Append(reader.ReadVarUInt128());
                    break;

                case WireDataType.F32:
                    sb.Append(reader.ReadF32());
                    break;

                case WireDataType.F32Vec2:
                    sb.Append(reader.ReadF32Vec2());
                    break;

                case WireDataType.F32Vec3:
                    sb.Append(reader.ReadF32Vec3());
                    break;

                case WireDataType.F64:
                    sb.Append(reader.ReadF64());
                    break;

                case WireDataType.F64Vec2:
                    sb.Append(reader.ReadF64Vec2());
                    break;

                case WireDataType.F64Vec3:
                    sb.Append(reader.ReadF64Vec3());
                    break;

                case WireDataType.Float32:
                    sb.Append(reader.ReadFloat().ToString(CultureInfo.InvariantCulture));
                    break;

                case WireDataType.Float64:
                    sb.Append(reader.ReadDouble().ToString(CultureInfo.InvariantCulture));
                    break;

                // Nullable variants of primitives
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
                    WireDataType underlyingType = NullablePrimitiveWireTypeUnwrap(wireType);
                    if (reader.ReadVarInt() != 0)
                        ToString(sb, reader, depth, underlyingType);
                    else
                        sb.Append($"null ({underlyingType})");
                    break;
                }

                case WireDataType.MetaGuid:
                    sb.Append(new MetaGuid(reader.ReadUInt128()));
                    break;

                // Size-prefixed (in bytes) array of bytes (strings, etc.)
                case WireDataType.String:
                    string str = reader.ReadString(maxSize: 256 * 1024 /*MaxStringSize*/);
                    if (str != null)
                        sb.Append($"\"{str}\"");
                    else
                        sb.Append("null (string)");
                    break;

                case WireDataType.Bytes:
                    int numBytes = reader.ReadVarInt();
                    if (numBytes > 0)
                    {
                        byte[] bytes = new byte[numBytes];
                        reader.ReadBytes(bytes);
                        sb.Append(Invariant($"bytes[{numBytes}] [{Util.BytesToString(bytes, 64)}]"));
                    }
                    else
                        sb.Append("null (bytes[])");
                    break;

                // Abstract class or interface: TypeCode <> Struct <> StructEnd
                case WireDataType.AbstractStruct:
                    int typeCode = reader.ReadVarInt();
                    if (typeCode != 0)
                    {
                        sb.Append(Invariant($"<{typeCode}> {{\n"));
                        ToStringMembers(sb, reader, depth + 1);
                        sb.Append($"{indent}}}");
                    }
                    else
                        sb.Append("null (AbstractStruct)");
                    break;

                case WireDataType.NullableStruct:
                    // Handle nulls (0 byte in stream)
                    if (reader.ReadByte() != 0)
                    {
                        sb.Append($"nullable struct {{\n");
                        ToStringMembers(sb, reader, depth + 1);
                        sb.Append($"{indent}}}");
                    }
                    else
                        sb.Append($"nullable struct (null)");
                    break;

                // Struct or class: (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
                case WireDataType.Struct:
                    sb.Append($"struct {{\n");
                    ToStringMembers(sb, reader, depth + 1);
                    sb.Append($"{indent}}}");
                    break;

                // List/set of values: Length <> ElemType <> Element0, ...
                case WireDataType.ValueCollection:
                    {
                        // -1 represents null, in which case the types are not written
                        int count = reader.ReadVarInt();
                        if (count >= 0)
                        {
                            WireDataType elemType = ReadWireType(reader);
                            sb.Append(Invariant($"{elemType}[{count}] [\n"));

                            for (int ndx = 0; ndx < count; ndx++)
                            {
                                sb.Append(Invariant($"{indent}  #{ndx} = "));
                                ToString(sb, reader, depth + 1, elemType);
                                sb.Append("\n");
                            }

                            sb.Append($"{indent}]");
                        }
                        else
                            sb.Append($"null (ValueCollection)");
                        break;
                    }

                // Dictionary of values: Length <> KeyType <> ValueType <> Key0, Value0, Key1, ...
                case WireDataType.KeyValueCollection:
                    {
                        // -1 represents null, in which case the types are not written
                        int count = reader.ReadVarInt();
                        if (count >= 0)
                        {
                            WireDataType keyType    = ReadWireType(reader);
                            WireDataType valueType  = ReadWireType(reader);
                            sb.Append(Invariant($"Dict<{keyType}, {valueType}>[{count}] {{\n"));

                            for (int ndx = 0; ndx < count; ndx++)
                            {
                                sb.Append($"{indent}  ");
                                ToString(sb, reader, depth + 1, keyType);
                                sb.Append(" = ");
                                ToString(sb, reader, depth + 1, valueType);
                                sb.Append("\n");
                            }

                            sb.Append($"{indent}}}");
                        }
                        else
                            sb.Append($"null (KeyValueCollection)");

                        break;
                    }

                default:
                    throw new MetaSerializationException($"Unknown wire type at offset {reader.Offset - 1}: {wireType}");
            }
        }

        public static void ToString(StringBuilder sb, IOReader reader, int depth)
        {
            WireDataType wireType = ReadWireType(reader);
            ToString(sb, reader, depth, wireType);
        }

        public static string ToString(IOReader reader)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                ToString(sb, reader, 0);
            }
            catch (Exception ex)
            {
                sb.Append($"FAILED: {ex}");
            }
            return sb.ToString();

        }

        public static string ToString(byte[] serialized)
        {
            using (IOReader reader = new IOReader(serialized))
                return ToString(reader);
        }

        public static WireDataType NullablePrimitiveWireTypeUnwrap(WireDataType wireType)
        {
            switch (wireType)
            {
                case WireDataType.NullableVarInt:       return WireDataType.VarInt;
                case WireDataType.NullableVarInt128:    return WireDataType.VarInt128;
                case WireDataType.NullableF32:          return WireDataType.F32;
                case WireDataType.NullableF32Vec2:      return WireDataType.F32Vec2;
                case WireDataType.NullableF32Vec3:      return WireDataType.F32Vec3;
                case WireDataType.NullableF64:          return WireDataType.F64;
                case WireDataType.NullableF64Vec2:      return WireDataType.F64Vec2;
                case WireDataType.NullableF64Vec3:      return WireDataType.F64Vec3;
                case WireDataType.NullableFloat32:      return WireDataType.Float32;
                case WireDataType.NullableFloat64:      return WireDataType.Float64;
                case WireDataType.NullableMetaGuid:     return WireDataType.MetaGuid;

                default:
                    throw new ArgumentException($"{wireType} is not a nullable primitive type", nameof(wireType));
            }
        }

        public static WireDataType PrimitiveTypeWrapNullable(WireDataType wireType)
        {
            switch (wireType)
            {
                case WireDataType.VarInt:       return WireDataType.NullableVarInt;
                case WireDataType.VarInt128:    return WireDataType.NullableVarInt128;
                case WireDataType.F32:          return WireDataType.NullableF32;
                case WireDataType.F32Vec2:      return WireDataType.NullableF32Vec2;
                case WireDataType.F32Vec3:      return WireDataType.NullableF32Vec3;
                case WireDataType.F64:          return WireDataType.NullableF64;
                case WireDataType.F64Vec2:      return WireDataType.NullableF64Vec2;
                case WireDataType.F64Vec3:      return WireDataType.NullableF64Vec3;
                case WireDataType.Float32:      return WireDataType.NullableFloat32;
                case WireDataType.Float64:      return WireDataType.NullableFloat64;
                case WireDataType.MetaGuid:     return WireDataType.NullableMetaGuid;

                default:
                    throw new ArgumentException($"{wireType} is not a primitive type", nameof(wireType));
            }
        }
    }
}
