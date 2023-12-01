// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Type of the data structure on the serialized data stream (i.e. wire).
    /// </summary>
    public enum WireDataType
    {
        Invalid             = 0,    // Invalid, must not be used
        Null                = 1,    // Null value
        VarInt              = 2,    // All integer types up to 64 bits
        VarInt128           = 3,    // 128-bit integer
        F32                 = 4,    // Signed 16.16 fixed-point value (F32)
        F32Vec2             = 5,    // Two-element vector of s16.16 fixed-point
        F32Vec3             = 6,    // Three-element vetor of s16.16 fixed-point
        F64                 = 7,    // Signed 32.32 fixed-point value
        F64Vec2             = 8,    // Two-element vector of s32.32 fixed-point
        F64Vec3             = 9,    // Three-element vector of s32.32 fixed-point
        Float32             = 10,   // 32-bit floating point (float)
        Float64             = 11,   // 64-bit floating point (double)
        String              = 12,   // Size-prefixed (in bytes) UTF8 string
        Bytes               = 13,   // Size-prefixed (in bytes) array of bytes (strings have their own type)
        AbstractStruct      = 14,   // Abstract class or interface: TypeCode <> (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
        NullableStruct      = 15,   // Class: IsNotNull <> (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
        Struct              = 16,   // Struct: (FieldType <> FieldTagId <> FieldValue)* <> EndStruct
        EndStruct           = 17,   // End of struct
        ValueCollection     = 18,   // List/set of values: Length <> ElemType <> Element0 <> ...
        KeyValueCollection  = 19,   // Dictionary of values: Length <> KeyType <> ValueType <> Key0 <> Value0 <> Key1 <> ...
        ObjectTable         = 20,   // Table of non-null objects: NumItems <> ItemType <> ItemMembers (all items must be non-null, there is no IsNotNull)
        NullableVarInt      = 21,   // Nullable VarInt: IsNotNull (<> VarIntValue)
        NullableVarInt128   = 22,   // Nullable VarInt128: IsNotNull (<> VarInt128Value)
        NullableF32         = 23,   // Nullable F32: IsNotNull (<> F32Value)
        NullableF32Vec2     = 24,   // Nullable F32Vec2: IsNotNull (<> F32Vec2Value)
        NullableF32Vec3     = 25,   // Nullable F32Vec3: IsNotNull (<> F32Vec3Value)
        NullableF64         = 26,   // Nullable F64: IsNotNull (<> F64)
        NullableF64Vec2     = 27,   // Nullable F64Vec2: IsNotNull (<> F64Vec2Value)
        NullableF64Vec3     = 28,   // Nullable F64Vec3: IsNotNull (<> F64Vec3Value)
        NullableFloat32     = 29,   // Nullable Float32: IsNotNull (<> Float32)
        NullableFloat64     = 30,   // Nullable Float64: IsNotNull (<> Float64)
        MetaGuid            = 31,   // MetaGuid: UInt128
        NullableMetaGuid    = 32,   // Nullable MetaGuid: IsNotNull (<> UInt128)
    }
}
