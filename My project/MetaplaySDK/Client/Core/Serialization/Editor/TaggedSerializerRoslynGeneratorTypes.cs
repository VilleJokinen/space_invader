// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    public static partial class TaggedSerializerRoslynGenerator
    {
        static readonly string s_UncheckedWriteSuffix = "Unchecked";
        static string GetWriteFuncSuffix(bool spaceEnsured) => spaceEnsured ? s_UncheckedWriteSuffix : "";

        static ITypeInfo[] s_primitiveInfos = new ITypeInfo[]
        {
            SimpleTypeInfo.FromExprs(typeof(bool),       1,                             (s, v) => $"writer.WriteByte{s}((byte)({v} ? 1 : 0))",   "reader.ReadByte() != 0"),
            SimpleTypeInfo.FromExprs(typeof(sbyte),      VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarInt{s}({v})",                 "checked((sbyte)reader.ReadVarInt())"),
            SimpleTypeInfo.FromExprs(typeof(byte),       VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarUInt{s}({v})",                "checked((byte)reader.ReadVarUInt())"),
            SimpleTypeInfo.FromExprs(typeof(short),      VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarInt{s}({v})",                 "checked((short)reader.ReadVarInt())"),
            SimpleTypeInfo.FromExprs(typeof(ushort),     VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarUInt{s}({v})",                "checked((ushort)reader.ReadVarUInt())"),
            SimpleTypeInfo.FromExprs(typeof(char),       VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarUInt{s}({v})",                "checked((char)reader.ReadVarUInt())"),
            SimpleTypeInfo.FromExprs(typeof(int),        VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarInt{s}({v})",                 "reader.ReadVarInt()"),
            SimpleTypeInfo.FromExprs(typeof(uint),       VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarUInt{s}({v})",                "reader.ReadVarUInt()"),
            SimpleTypeInfo.FromExprs(typeof(long),       VarIntUtils.VarLongMaxBytes,   (s, v) => $"writer.WriteVarLong{s}({v})",                "reader.ReadVarLong()"),
            SimpleTypeInfo.FromExprs(typeof(ulong),      VarIntUtils.VarLongMaxBytes,   (s, v) => $"writer.WriteVarULong{s}({v})",               "reader.ReadVarULong()"),
            SimpleTypeInfo.FromExprs(typeof(MetaUInt128), VarIntUtils.VarInt128MaxBytes, (s, v) => $"writer.WriteVarUInt128{s}({v})",             "reader.ReadVarUInt128()"),
            SimpleTypeInfo.FromExprs(typeof(F32),        4,                             (s, v) => $"writer.WriteF32{s}({v})",                    "reader.ReadF32()"),
            SimpleTypeInfo.FromExprs(typeof(F32Vec2),    8,                             (s, v) => $"writer.WriteF32Vec2{s}({v})",                "reader.ReadF32Vec2()"),
            SimpleTypeInfo.FromExprs(typeof(F32Vec3),    12,                            (s, v) => $"writer.WriteF32Vec3{s}({v})",                "reader.ReadF32Vec3()"),
            SimpleTypeInfo.FromExprs(typeof(F64),        8,                             (s, v) => $"writer.WriteF64{s}({v})",                    "reader.ReadF64()"),
            SimpleTypeInfo.FromExprs(typeof(F64Vec2),    16,                            (s, v) => $"writer.WriteF64Vec2{s}({v})",                "reader.ReadF64Vec2()"),
            SimpleTypeInfo.FromExprs(typeof(F64Vec3),    24,                            (s, v) => $"writer.WriteF64Vec3{s}({v})",                "reader.ReadF64Vec3()"),
            SimpleTypeInfo.FromExprs(typeof(float),      4,                             (s, v) => $"writer.WriteFloat{s}({v})",                  "reader.ReadFloat()"),
            SimpleTypeInfo.FromExprs(typeof(double),     8,                             (s, v) => $"writer.WriteDouble{s}({v})",                 "reader.ReadDouble()"),
            SimpleTypeInfo.FromExprs(typeof(MetaGuid),   16,                            (s, v) => $"writer.WriteUInt128{s}({v}.Value)",          "new MetaGuid(reader.ReadUInt128())"),
            SimpleTypeInfo.FromExprs(typeof(EntityKind), VarIntUtils.VarIntMaxBytes,    (s, v) => $"writer.WriteVarInt{s}({v}.Value)",           "EntityKind.FromValue(reader.ReadVarInt())"),
            SimpleTypeInfo.FromExprs(typeof(string),     null,                          (s, v) => $"writer.WriteString({v})",                    "reader.ReadString(context.MaxStringSize)"),
            SimpleTypeInfo.FromExprs(typeof(byte[]),     null,                          (s, v) => $"writer.WriteByteString({v})",                "reader.ReadByteString(context.MaxByteArraySize)"),
            SimpleTypeInfo.FromExprs(typeof(TimeSpan),     VarIntUtils.VarLongMaxBytes,                          (s, v) => $"writer.WriteVarLong{s}({v}.Ticks)", "new TimeSpan(reader.ReadVarLong())"),

#if NETCOREAPP // cloud
            new SimpleTypeInfo(
                typeof(Akka.Actor.IActorRef), null,
                emitWriteCode:  (sb, _, valueExpr) => sb.AppendLine($"writer.WriteString(({valueExpr} != null) ? Akka.Serialization.Serialization.SerializedActorPath({valueExpr}) : null);"),
                emitReadCode:   (sb, varName) =>
                {
                    sb.AppendLine("string str = reader.ReadString(2048);");
                    sb.AppendLine($"{varName} = (str != null) ? context.ActorSystem.Provider.ResolveActorRef(str) : null;");
                }),
#endif
        };

        class SimpleTypeInfo : ITypeInfo
        {
            public Type Type { get; }
            public string GlobalNamespaceQualifiedTypeString => Type.ToGlobalNamespaceQualifiedTypeString();
            public IEnumerable<Type> DirectlyContainedTypes => Enumerable.Empty<Type>();
            public bool WriteToDebugStreamAtSerializationStart => false;

            public delegate void EmitWriteCodeFunc(IndentedStringBuilder sb, string suffix, string valueExpr);
            public delegate void EmitReadCodeFunc(IndentedStringBuilder sb, string varName);

            int?                _maxSerializedBytes;
            EmitWriteCodeFunc   _emitWriteCode;
            EmitReadCodeFunc    _emitReadCode;

            public SimpleTypeInfo(Type type, int? maxSerializedBytes, EmitWriteCodeFunc emitWriteCode, EmitReadCodeFunc emitReadCode)
            {
                Type = type;
                _maxSerializedBytes = maxSerializedBytes;
                _emitWriteCode = emitWriteCode ?? throw new ArgumentNullException(nameof(emitWriteCode));
                _emitReadCode = emitReadCode ?? throw new ArgumentNullException(nameof(emitReadCode));
            }

            public delegate string CreateWriteExprFunc(string suffix, string valueExpr);

            public static SimpleTypeInfo FromExprs(Type type, int? maxSerializedBytes, CreateWriteExprFunc createWriteExpression, string readExpression)
            {
                return new SimpleTypeInfo(
                    type,
                    maxSerializedBytes,
                    emitWriteCode: (sb, suffix, valueExpr) => sb.AppendLine($"{createWriteExpression(suffix, valueExpr)};"),
                    emitReadCode:  (sb, varName) => sb.AppendLine($"{varName} = {readExpression};"));
            }

            public void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                _emitWriteCode(sb, GetWriteFuncSuffix(spaceEnsured), valueExpr);
            }

            public void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                _emitReadCode(sb, varName);
            }

            public void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // Simple types (primitives etc) do not contain MetaRefs.
            }

            public void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitSimple(Type, obj);
            }

            public int? MaxSerializedBytesStatic() => _maxSerializedBytes;
        }

        class GuidInfo : ITypeInfo
        {
            Type _type;

            public GuidInfo(Type type)
            {
                _type = type ?? throw new ArgumentNullException(nameof(type));

                IsNullable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

                var innerType = type;
                if (IsNullable)
                {
                    innerType              = type.GetSystemNullableElementType();
                    DirectlyContainedTypes = new[] {type.GetSystemNullableElementType()};
                }
                else
                    DirectlyContainedTypes = Array.Empty<Type>();

                if (innerType != typeof(Guid))
                    throw new ArgumentException("Parameter must be a Guid", nameof(type));
            }

            public Type              Type                                   => _type;
            public string            GlobalNamespaceQualifiedTypeString     => Type.ToGlobalNamespaceQualifiedTypeString();
            public IEnumerable<Type> DirectlyContainedTypes                 { get; }
            public bool              WriteToDebugStreamAtSerializationStart => false;
            public bool              IsNullable                             { get; }

            public void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                if (IsNullable)
                    sb.AppendLine($"writer.WriteNullableGuidAsByteString{GetWriteFuncSuffix(spaceEnsured)}({valueExpr});");
                else
                    sb.AppendLine($"writer.WriteGuidAsByteString{GetWriteFuncSuffix(spaceEnsured)}({valueExpr});");
            }

            public void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("byte[] bytes = reader.ReadByteString(context.MaxByteArraySize);");
                sb.AppendLine("if (bytes != null)");
                sb.Indent("{");

                sb.AppendLine("if (bytes.Length != 16)");
                sb.Indent("{");
                sb.AppendLine($"throw new InvalidOperationException(\"Found {{bytes.Length}} length byte array for Guid while expecting array of length 16.\");");
                sb.Unindent("}");

                sb.AppendLine($"{varName} = new global::System.Guid(bytes);");

                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");

                if (IsNullable)
                    sb.AppendLine($"{varName} = null;");
                else
                    sb.AppendLine($"throw new InvalidOperationException(\"Found null byte array for Guid while expecting array of length 16.\");");

                sb.Unindent("}");
            }

            public void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // Primitives, and nullable primitives, do not contain MetaRefs.
            }

            public void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                if(IsNullable)
                    visitor.VisitNullablePrimitive(Type, obj);
                else
                    visitor.VisitSimple(Type, obj);
            }

            public int? MaxSerializedBytesStatic()
            {
                return 17;
            }
        }

        class NullablePrimitiveInfo : ITypeInfo
        {
            Type _type;
            ITypeInfo _primitive;

            public NullablePrimitiveInfo(Type type, ITypeInfo primitive)
            {
                _type = type ?? throw new ArgumentNullException(nameof(type));
                _primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
                UnderlyingType = _type.GetSystemNullableElementType();
            }

            readonly Type UnderlyingType;

            public Type Type => _type;
            public string GlobalNamespaceQualifiedTypeString => Type.ToGlobalNamespaceQualifiedTypeString();
            public IEnumerable<Type> DirectlyContainedTypes => new Type[]{ UnderlyingType };
            public bool WriteToDebugStreamAtSerializationStart => false;

            public void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"if ({valueExpr}.HasValue)");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.One);");
                sb.AppendLine($"{UnderlyingType.ToGlobalNamespaceQualifiedTypeString()} underlyingValue = {valueExpr}.Value;");
                _primitive.AppendSerializationCode(sb, "underlyingValue", useTrampolines, spaceEnsured);
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.Zero);");
                sb.Unindent("}");
            }

            public void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("int hasValue = reader.ReadVarInt();");
                sb.AppendLine();
                sb.AppendLine("if (hasValue != 0)");
                sb.Indent("{");
                sb.AppendLine($"{UnderlyingType.ToGlobalNamespaceQualifiedTypeString()} underlyingValue;");
                _primitive.AppendDeserializationCode(sb, "underlyingValue", useTrampolines);
                sb.AppendLine($"{varName} = underlyingValue;");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }

            public void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // Primitives, and nullable primitives, do not contain MetaRefs.
            }

            public void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitNullablePrimitive(Type, obj);
            }

            public int? MaxSerializedBytesStatic()
            {
                int? primitiveMaxBytes = _primitive.MaxSerializedBytesStatic();
                if (primitiveMaxBytes.HasValue)
                    return primitiveMaxBytes.Value + 1;
                return null;
            }
        }

        abstract class MetaSerializableTypeInfo : ITypeInfo
        {
            protected MetaSerializableType _typeSpec { get; }

            protected MetaSerializableTypeInfo(MetaSerializableType typeSpec)
            {
                _typeSpec = typeSpec ?? throw new ArgumentNullException(nameof(typeSpec));
            }

            public Type Type => _typeSpec.Type;
            public string GlobalNamespaceQualifiedTypeString => _typeSpec.GlobalNamespaceQualifiedTypeString;
            public abstract IEnumerable<Type> DirectlyContainedTypes { get; }
            public virtual bool WriteToDebugStreamAtSerializationStart => true;

            public abstract void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured);
            public abstract void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines);
            public abstract void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines);
            public abstract void Traverse(object obj, DynamicTraversal.IVisitor visitor);

            protected virtual int? MaxSerializedBytes() => null;

            bool _maxSerializedBytesComputed = false;
            int? _maxSerializedBytes;

            int? ITypeInfo.MaxSerializedBytesStatic()
            {
                if (!_maxSerializedBytesComputed)
                {
                    // initialize cached value first to break cycles
                    _maxSerializedBytesComputed = true;
                    _maxSerializedBytes = null;
                    _maxSerializedBytes = MaxSerializedBytes();
                }
                return _maxSerializedBytes;
            }
        }

        class ValueCollectionInfo : MetaSerializableTypeInfo
        {
            public ValueCollectionInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                ElementType = _typeSpec.Type.GetCollectionElementType();
                IsArray = _typeSpec.Type.IsArray;
            }

            readonly bool IsArray;
            readonly Type ElementType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ ElementType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                WireDataType elemWireType = TaggedWireSerializer.GetWireType(ElementType);

                sb.AppendLine($"if ({valueExpr} != null)");
                sb.Indent("{");
                if (IsArray)
                    sb.AppendLine($"int count = {valueExpr}.Length;");
                else
                    sb.AppendLine($"int count = {valueExpr}.Count;");

                sb.AppendLine($"if (count > context.MemberContext.MaxCollectionSize)");
                sb.AppendLine($"    throw new InvalidOperationException($\"Invalid value collection size {{count}} (maximum allowed is {{context.MemberContext.MaxCollectionSize}})\");");

                sb.AppendLine($"writer.WriteVarInt(count);");
                sb.AppendLine();
                sb.AppendLine($"WriteWireType(ref writer, WireDataType.{elemWireType});");
                sb.AppendLine();
                sb.AppendLine($"foreach (var elem in {valueExpr})");
                sb.Indent("{");
                sb.AppendLine($"{SerializationExpression(ElementType, "elem", false)};");
                sb.Unindent("}");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine("writer.WriteVarIntConst(VarIntConst.MinusOne);");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                WireDataType elemWireType = TaggedWireSerializer.GetWireType(ElementType);

                string elemTypeStr = ElementType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine("int count = reader.ReadVarInt();");
                sb.AppendLine("if (count < -1 || count > context.MemberContext.MaxCollectionSize)");
                sb.AppendLine("    throw new InvalidOperationException($\"Invalid value collection size {count} (maximum allowed is {context.MemberContext.MaxCollectionSize})\");");

                sb.AppendLine();
                sb.AppendLine("if (count != -1)");
                sb.Indent("{");
                sb.AppendLine($"WireDataType elemWireType = ReadWireType(reader);");

                string expectPrimaryElemWireTypeExpr = $"ExpectWireType(ref context, reader, typeof({elemTypeStr}), elemWireType, WireDataType.{elemWireType});";

                sb.AppendLine();
                if (TryGetConverterWireTypesPredicateExpression(ElementType, "elemWireType", out string elemWireTypesPredicate))
                {
                    sb.AppendLine($"bool elemWireTypeIsAcceptedByConverter = {elemWireTypesPredicate};");
                    sb.AppendLine($"if (!elemWireTypeIsAcceptedByConverter)");
                    sb.Indent();
                    sb.AppendLine($"{expectPrimaryElemWireTypeExpr};");
                    sb.Unindent();
                }
                else
                    sb.AppendLine($"{expectPrimaryElemWireTypeExpr};");

                sb.AppendLine();

                Type typeToCreate         = _typeSpec.Type;
                bool isReadOnlyCollection = false;
                if (typeToCreate.IsGenericTypeOf(typeof(ReadOnlyCollection<>)))
                {
                    typeToCreate = typeof(List<>).MakeGenericType(_typeSpec.Type.GenericTypeArguments);
                    isReadOnlyCollection = true;
                }

                bool hasCapacityCtor = typeToCreate.GetConstructor(new Type[] { typeof(int) }) != null;
                if (IsArray)
                    sb.AppendLine($"var temp = new {ReplaceArrayCount(typeToCreate.ToGlobalNamespaceQualifiedTypeString(), "[count]")};");
                else if (hasCapacityCtor)
                    sb.AppendLine($"var temp = new {typeToCreate.ToGlobalNamespaceQualifiedTypeString()}(count);");
                else
                    sb.AppendLine($"var temp = new {typeToCreate.ToGlobalNamespaceQualifiedTypeString()}();");
                sb.AppendLine();

                sb.AppendLine("for (int ndx = 0; ndx < count; ndx++)");
                sb.Indent("{");

                sb.AppendLine($"{DeserializationExpression(ElementType, "elem", "elemWireType")};");
                if (IsArray)
                    sb.AppendLine($"temp[ndx] = elem;");
                else if (typeToCreate.IsGenericTypeOf(typeof(Queue<>)))
                    sb.AppendLine($"temp.Enqueue(elem);");
                else
                    sb.AppendLine($"temp.Add(elem);");

                sb.Unindent("}");

                if(isReadOnlyCollection)
                    sb.AppendLine($"{varName} = new {_typeSpec.GlobalNamespaceQualifiedTypeString}(temp);");
                else
                    sb.AppendLine($"{varName} = temp;");

                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                string elemTypeStr = ElementType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine($"if ({varName} != null)");
                sb.Indent("{");

                if (IsArray)
                    sb.AppendLine($"int count = {varName}.Length;");
                else
                    sb.AppendLine($"int count = {varName}.Count;");
                sb.AppendLine();

                if (IsArray || typeof(IList).IsAssignableFrom(_typeSpec.Type))
                {
                    // Array and IList are indexable, so they can be updated in-place.

                    sb.AppendLine("for (int ndx = 0; ndx < count; ndx++)");
                    sb.Indent("{");
                    sb.AppendLine($"{elemTypeStr} elem = {varName}[ndx];");
                    sb.AppendLine($"{MetaRefTraverseExpression(ElementType, "elem")};");
                    sb.AppendLine($"if (context.MetaRefTraversal.IsMutatingOperation)");
                    sb.Indent("{");
                    sb.AppendLine($"{varName}[ndx] = elem;");
                    sb.Unindent("}");
                    sb.Unindent("}");
                }
                else
                {
                    // Other collections are not generally indexable, so we cannot
                    // update them in-place. Create a new collection and add
                    // the new elements there.
                    // \todo Could do some cleverness where the updating is done
                    //       in-place so long as the updating does not change the
                    //       identity of the element. Might not be worth it.

                    bool hasCapacityCtor = _typeSpec.Type.GetConstructor(new Type[] { typeof(int) }) != null;
                    string collectionCtorStr;
                    if (hasCapacityCtor)
                        collectionCtorStr = $"new {_typeSpec.GlobalNamespaceQualifiedTypeString}(count)";
                    else
                        collectionCtorStr = $"new {_typeSpec.GlobalNamespaceQualifiedTypeString}()";

                    sb.AppendLine($"{_typeSpec.GlobalNamespaceQualifiedTypeString} newCollection = context.MetaRefTraversal.IsMutatingOperation ? {collectionCtorStr} : null;");

                    sb.AppendLine($"foreach ({elemTypeStr} elem in {varName})");
                    sb.Indent("{");
                    sb.AppendLine($"{elemTypeStr} updatedElem = elem;");
                    sb.AppendLine($"{MetaRefTraverseExpression(ElementType, "updatedElem")};");

                    sb.AppendLine("if (context.MetaRefTraversal.IsMutatingOperation)");
                    sb.Indent("{");
                    if (_typeSpec.Type.IsGenericTypeOf(typeof(Queue<>)))
                        sb.AppendLine("newCollection.Enqueue(updatedElem);");
                    else
                        sb.AppendLine("newCollection.Add(updatedElem);");

                    sb.Unindent("}");

                    sb.Unindent("}");
                    sb.AppendLine();
                    sb.AppendLine("if (context.MetaRefTraversal.IsMutatingOperation)");
                    sb.Indent("{");
                    sb.AppendLine($"{varName} = newCollection;");
                    sb.Unindent("}");
                }

                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                IEnumerable enumerable = (IEnumerable)obj;
                visitor.BeginTraverseValueCollection(Type, enumerable);
                if (enumerable != null)
                {
                    int index = 0;
                    foreach (object elem in enumerable)
                    {
                        visitor.BeginTraverseValueCollectionElement(Type, enumerable, index, ElementType, elem);
                        visitor.Traverse(ElementType, elem);
                        visitor.EndTraverseValueCollectionElement(Type, enumerable, index, ElementType, elem);
                        index++;
                    }
                }
                visitor.EndTraverseValueCollection(Type, enumerable);
            }

            /// <summary>
            /// Replaces the '[]' of a (potentially nested) array type with <paramref name="newValue"/>.
            /// The replaced '[]' is the first occurrence of the (potentially many) at the end of the string.
            /// Eg, 'List&lt;int[][]&gt;[][]' would return 'List&lt;[][]&gt[count][]' with '[count]' as newValue.
            /// </summary>
            /// <param name="input">Stringified type to insert the count into</param>
            /// <param name="newValue">Replacement for the matching brackets, eg, '[count']</param>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            public string ReplaceArrayCount(string input, string newValue)
            {
                // Find the index of the first '[]' in a string suffixed with potentially multiple '[]'
                if (input.Substring(input.Length - 2) != "[]")
                    throw new ArgumentException($"Array type must end with one or more '[]', got '{input}'");
                int firstEmptyBracketsNdx = input.Length - 2;
                for (; ;)
                {
                    if (firstEmptyBracketsNdx < 2)
                        break;
                    else if (input.Substring(firstEmptyBracketsNdx - 2, 2) == "[]")
                        firstEmptyBracketsNdx -= 2;
                    else
                        break;
                }

                return $"{input.Substring(0, firstEmptyBracketsNdx)}{newValue}{input.Substring(firstEmptyBracketsNdx + "[]".Length)}";
            }
        }

        class KeyValueCollectionInfo : MetaSerializableTypeInfo
        {
            public KeyValueCollectionInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                (Type keyType, Type valueType) = _typeSpec.Type.GetDictionaryKeyAndValueTypes();
                KeyType = keyType;
                ValueType = valueType;
            }

            readonly Type KeyType;
            readonly Type ValueType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ KeyType, ValueType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"if ({valueExpr} != null)");
                sb.Indent("{");

                sb.AppendLine($"if ({valueExpr}.Count > context.MemberContext.MaxCollectionSize)");
                sb.AppendLine($"    throw new InvalidOperationException($\"Invalid value collection size {{{valueExpr}.Count}} (maximum allowed is {{context.MemberContext.MaxCollectionSize}})\");");

                sb.AppendLine($"writer.WriteVarInt({valueExpr}.Count);");
                sb.AppendLine();

                WireDataType keyWireType = TaggedWireSerializer.GetWireType(KeyType);
                WireDataType valueWireType = TaggedWireSerializer.GetWireType(ValueType);

                sb.AppendLine($"WriteWireType(ref writer, WireDataType.{keyWireType});");
                sb.AppendLine($"WriteWireType(ref writer, WireDataType.{valueWireType});");
                sb.AppendLine();

                string keyTypeStr = KeyType.ToGlobalNamespaceQualifiedTypeString();
                string valueTypeStr = ValueType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine($"foreach (({keyTypeStr} key, {valueTypeStr} value) in {valueExpr})");
                sb.Indent("{");
                sb.AppendLine($"{SerializationExpression(KeyType, "key", false)};");
                sb.AppendLine($"{SerializationExpression(ValueType, "value", false)};");
                sb.Unindent("}");

                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine("writer.WriteVarIntConst(VarIntConst.MinusOne);");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                WireDataType keyWireType = TaggedWireSerializer.GetWireType(KeyType);
                WireDataType valueWireType = TaggedWireSerializer.GetWireType(ValueType);

                string keyTypeStr = KeyType.ToGlobalNamespaceQualifiedTypeString();
                string valueTypeStr = ValueType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine("int count = reader.ReadVarInt();");

                sb.AppendLine("if (count < -1 || count > context.MemberContext.MaxCollectionSize)");
                sb.AppendLine("    throw new InvalidOperationException($\"Invalid value collection size {count} (maximum allowed is {context.MemberContext.MaxCollectionSize})\");");

                sb.AppendLine();
                sb.AppendLine("if (count != -1)");
                sb.Indent("{");
                sb.AppendLine($"WireDataType keyWireType = ReadWireType(reader);");
                sb.AppendLine($"WireDataType valueWireType = ReadWireType(reader);");

                string expectPrimaryKeyWireTypeExpr = $"ExpectWireType(ref context, reader, typeof({keyTypeStr}), keyWireType, WireDataType.{keyWireType})";
                string expectPrimaryValueWireTypeExpr = $"ExpectWireType(ref context, reader, typeof({valueTypeStr}), valueWireType, WireDataType.{valueWireType})";

                sb.AppendLine();
                if (TryGetConverterWireTypesPredicateExpression(KeyType, "keyWireType", out string keyWireTypesPredicate))
                {
                    sb.AppendLine($"bool keyWireTypeIsAcceptedByConverter = {keyWireTypesPredicate};");
                    sb.AppendLine($"if (!keyWireTypeIsAcceptedByConverter)");
                    sb.Indent();
                    sb.AppendLine($"{expectPrimaryKeyWireTypeExpr};");
                    sb.Unindent();
                }
                else
                    sb.AppendLine($"{expectPrimaryKeyWireTypeExpr};");

                sb.AppendLine();
                if (TryGetConverterWireTypesPredicateExpression(ValueType, "valueWireType", out string valueWireTypesPredicate))
                {
                    sb.AppendLine($"bool valueWireTypeIsAcceptedByConverter = {valueWireTypesPredicate};");
                    sb.AppendLine($"if (!valueWireTypeIsAcceptedByConverter)");
                    sb.Indent();
                    sb.AppendLine($"{expectPrimaryValueWireTypeExpr};");
                    sb.Unindent();
                }
                else
                    sb.AppendLine($"{expectPrimaryValueWireTypeExpr};");

                sb.AppendLine();

                Type typeToCreate    = _typeSpec.Type;
                bool isReadOnlyDictionary = false;
                if (typeToCreate.IsGenericTypeOf(typeof(ReadOnlyDictionary<,>)))
                {
                    typeToCreate         = typeof(OrderedDictionary<,>).MakeGenericType(_typeSpec.Type.GenericTypeArguments);
                    isReadOnlyDictionary = true;
                }

                bool hasCapacityCtor = typeToCreate.GetConstructor(new Type[] { typeof(int) }) != null;
                if (hasCapacityCtor)
                    sb.AppendLine($"var temp = new {typeToCreate.ToGlobalNamespaceQualifiedTypeString()}(count);");
                else
                    sb.AppendLine($"var temp = new {typeToCreate.ToGlobalNamespaceQualifiedTypeString()}();");
                sb.AppendLine();

                sb.AppendLine("for (int ndx = 0; ndx < count; ndx++)");
                sb.Indent("{");
                sb.AppendLine($"{DeserializationExpression(KeyType, "key", "keyWireType")};");
                sb.AppendLine($"{DeserializationExpression(ValueType, "value", "valueWireType")};");
                sb.AppendLine($"temp.Add(key, value);");
                sb.Unindent("}");

                if(isReadOnlyDictionary)
                    sb.AppendLine($"{varName} = new {_typeSpec.GlobalNamespaceQualifiedTypeString}(temp);");
                else
                    sb.AppendLine($"{varName} = temp;");

                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }
            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"if ({varName} != null)");
                sb.Indent("{");
                sb.AppendLine($"int count = {varName}.Count;");
                sb.AppendLine();

                // A dictionary cannot be easily updated in-place without
                // additional allocations. For simplicity, this current
                // implementation always creates a new dictionary and
                // puts the updated keys-and-values there.
                //
                // \todo [nuutti] There are some potential optimizations that could be
                //                done in certain cases, for example if only values need
                //                to be updated and they can be updated in-place, then
                //                the dictionary could be updated in-place.

                bool hasCapacityCtor = _typeSpec.Type.GetConstructor(new Type[] { typeof(int) }) != null;
                string collectionCtorStr;
                if (hasCapacityCtor)
                    collectionCtorStr = $"new {_typeSpec.GlobalNamespaceQualifiedTypeString}(count)";
                else
                    collectionCtorStr = $"new {_typeSpec.GlobalNamespaceQualifiedTypeString}()";

                sb.AppendLine($"{_typeSpec.GlobalNamespaceQualifiedTypeString} newKVCollection = context.MetaRefTraversal.IsMutatingOperation ? {collectionCtorStr} : null;");
                sb.AppendLine();

                string keyTypeStr = KeyType.ToGlobalNamespaceQualifiedTypeString();
                string valueTypeStr = ValueType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine($"foreach (({keyTypeStr} key, {valueTypeStr} value) in {varName})");
                sb.Indent("{");

                sb.AppendLine($"{keyTypeStr} updatedKey = key;");
                if (metaRefContainingTypes.Contains(KeyType))
                    sb.AppendLine($"{MetaRefTraverseExpression(KeyType, "updatedKey")};");
                else
                    sb.AppendLine("// Key type does not contain MetaRefs");

                sb.AppendLine($"{valueTypeStr} updatedValue = value;");
                if (metaRefContainingTypes.Contains(ValueType))
                    sb.AppendLine($"{MetaRefTraverseExpression(ValueType, "updatedValue")};");
                else
                    sb.AppendLine("// Value type does not contain MetaRefs");

                sb.AppendLine("if (context.MetaRefTraversal.IsMutatingOperation)");
                sb.Indent("{");
                sb.AppendLine("newKVCollection.Add(updatedKey, updatedValue);");
                sb.Unindent("}");
                sb.Unindent("}");
                sb.AppendLine();
                sb.AppendLine("if (context.MetaRefTraversal.IsMutatingOperation)");
                sb.Indent("{");
                sb.AppendLine($"{varName} = newKVCollection;");
                sb.Unindent("}");

                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                IDictionary dictionary = (IDictionary)obj;
                visitor.BeginTraverseKeyValueCollection(Type, dictionary);
                if (dictionary != null)
                {
                    int index = 0;
                    IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        DictionaryEntry entry = enumerator.Entry;
                        visitor.BeginTraverseKeyValueCollectionKey(Type, dictionary, index, KeyType, entry.Key);
                        visitor.Traverse(KeyType, entry.Key);
                        visitor.EndTraverseKeyValueCollectionKey(Type, dictionary, index, KeyType, entry.Key);
                        visitor.BeginTraverseKeyValueCollectionValue(Type, dictionary, index, KeyType, entry.Key, ValueType, entry.Value);
                        visitor.Traverse(ValueType, entry.Value);
                        visitor.EndTraverseKeyValueCollectionValue(Type, dictionary, index, KeyType, entry.Key, ValueType, entry.Value);
                        index++;
                    }
                }
                visitor.EndTraverseKeyValueCollection(Type, dictionary);
            }
        }

        class ConcreteClassInfo : MetaSerializableTypeInfo
        {
            public ConcreteClassInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
            }

            public override IEnumerable<Type> DirectlyContainedTypes => new ConcreteClassOrStructMembersInfo(_typeSpec).DirectlyContainedTypes;

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"if ({valueExpr} != null)");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.One);");
                sb.AppendLine();
                sb.AppendLine($"{MembersSerializationExpression(_typeSpec.Type, valueExpr, spaceEnsured)};");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.Zero);");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("int hasValue = reader.ReadVarInt();");
                sb.AppendLine("if (hasValue != 0)");
                sb.Indent("{");
                sb.AppendLine($"{MembersDeserializationExpression(_typeSpec.Type, "result")};");
                sb.AppendLine($"{varName} = result;");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"if ({varName} != null)");
                sb.Indent("{");
                sb.AppendLine($"{MembersMetaRefTraverseExpression(_typeSpec.Type, varName)};");
                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.BeginTraverseConcreteClass(Type, obj);
                if (obj != null)
                    visitor.TraverseMembers(Type, obj);
                visitor.EndTraverseConcreteClass(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                int? membersMaxBytes = new ConcreteClassOrStructMembersInfo(_typeSpec).MaxSerializedBytesStatic();
                if (membersMaxBytes.HasValue)
                    return membersMaxBytes.Value + 1; // nullable
                return null;
            }
        }

        class StructInfo : MetaSerializableTypeInfo
        {
            public StructInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
            }

            public override IEnumerable<Type> DirectlyContainedTypes => new ConcreteClassOrStructMembersInfo(_typeSpec).DirectlyContainedTypes;

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"{MembersSerializationExpression(_typeSpec.Type, valueExpr, spaceEnsured)};");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine($"{MembersDeserializationExpression(_typeSpec.Type, "result")};");
                sb.AppendLine($"{varName} = result;");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"{MembersMetaRefTraverseExpression(_typeSpec.Type, varName)};");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.BeginTraverseStruct(Type, obj);
                visitor.TraverseMembers(Type, obj);
                visitor.EndTraverseStruct(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                return new ConcreteClassOrStructMembersInfo(_typeSpec).MaxSerializedBytesStatic();
            }
        }

        class NullableStructInfo : MetaSerializableTypeInfo
        {
            public NullableStructInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                UnderlyingType = _typeSpec.Type.GetSystemNullableElementType();
            }

            readonly Type UnderlyingType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ UnderlyingType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"if ({valueExpr}.HasValue)");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.One);");
                sb.AppendLine($"{MembersSerializationExpression(UnderlyingType, $"{valueExpr}.Value", spaceEnsured)};");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"writer.WriteVarIntConst{GetWriteFuncSuffix(spaceEnsured)}(VarIntConst.Zero);");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("int hasValue = reader.ReadVarInt();");
                sb.AppendLine("if (hasValue != 0)");
                sb.Indent("{");

                sb.AppendLine($"{MembersDeserializationExpression(UnderlyingType, "result")};");
                sb.AppendLine($"{varName} = result;");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"if ({varName}.HasValue)");
                sb.Indent("{");
                sb.AppendLine($"{UnderlyingType.ToGlobalNamespaceQualifiedTypeString()} updatedValue = {varName}.Value;");
                sb.AppendLine($"{MembersMetaRefTraverseExpression(UnderlyingType, "updatedValue")};");
                sb.AppendLine("if (context.MetaRefTraversal.IsMutatingOperation)");
                sb.Indent("{");
                sb.AppendLine($"{varName} = updatedValue;");
                sb.Unindent("}");
                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.BeginTraverseNullableStruct(Type, obj);
                if (obj != null)
                    visitor.TraverseMembers(UnderlyingType, obj);
                visitor.EndTraverseNullableStruct(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                int? underlyingMaxBytes = s_typeInfoCache[UnderlyingType].MaxSerializedBytesStatic();
                if (underlyingMaxBytes.HasValue)
                    return underlyingMaxBytes.Value + 1;
                return null;
            }
        }

        class AbstractClassOrInterfaceInfo : MetaSerializableTypeInfo
        {
            public AbstractClassOrInterfaceInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
            }

            public override IEnumerable<Type> DirectlyContainedTypes => _typeSpec.DerivedTypes.Values;

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"switch ({valueExpr})");
                sb.Indent("{");

                foreach ((int typeCode, Type derivedType) in _typeSpec.DerivedTypes)
                {
                    MetaSerializableType derivedTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(derivedType);
                    MetaDebug.Assert(!derivedTypeSpec.IsAbstract, $"{nameof(_typeSpec.DerivedTypes)} of {{0}} contains abstract type {{1}}", _typeSpec.Name, derivedTypeSpec.Name);
                    sb.AppendLine($"case {derivedTypeSpec.GlobalNamespaceQualifiedTypeString} concreteTypedValue:");
                    sb.AppendLine(Invariant($"    writer.WriteVarInt({typeCode}); // typecode"));
                    sb.AppendLine($"    {MembersSerializationExpression(derivedTypeSpec.Type, "concreteTypedValue", false)};");
                    sb.AppendLine("    break;");
                }

                sb.AppendLine("case null:");
                sb.AppendLine("    writer.WriteVarIntConst(VarIntConst.Zero);");
                sb.AppendLine("    break;");

                sb.AppendLine("default:");
                sb.AppendLine($"    throw new MetaSerializationException($\"Unknown derived type of {_typeSpec.Name}: {{{valueExpr}.GetType().ToGenericTypeString()}}. The derived type must have the [MetaSerializableDerived] attribute.\");");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("int typeCode = reader.ReadVarInt();");
                sb.AppendLine("if (typeCode != 0)");
                sb.Indent("{");
                sb.AppendLine("switch (typeCode)");
                sb.Indent("{");

                foreach ((int typeCode, Type derivedType) in _typeSpec.DerivedTypes)
                {
                    MetaSerializableType derivedTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(derivedType);
                    MetaDebug.Assert(!derivedTypeSpec.IsAbstract, $"{nameof(_typeSpec.DerivedTypes)} of {{0}} contains abstract type {{1}}", _typeSpec.Name, derivedTypeSpec.Name);
                    sb.AppendLine(Invariant($"case {typeCode}: {{ {MembersDeserializationExpression(derivedType, "result")}; {varName} = result; break; }}"));
                }

                sb.AppendLine("default:");
                sb.AppendLine($"    throw new MetaUnknownDerivedTypeDeserializationException($\"Unknown derived type of {_typeSpec.Name}: {{typeCode}}\", typeof({_typeSpec.GlobalNamespaceQualifiedTypeString}), typeCode);");

                sb.Unindent("}");

                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = null;");
                sb.Unindent("}");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"switch ({varName})");
                sb.Indent("{");

                foreach ((int typeCode, Type derivedType) in _typeSpec.DerivedTypes)
                {
                    MetaSerializableType derivedTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(derivedType);
                    MetaDebug.Assert(!derivedTypeSpec.IsAbstract, $"{nameof(_typeSpec.DerivedTypes)} of {{0}} contains abstract type {{1}}", _typeSpec.Name, derivedTypeSpec.Name);
                    sb.AppendLine($"case {derivedTypeSpec.GlobalNamespaceQualifiedTypeString} concreteTypedValue:");
                    if (metaRefContainingTypes.Contains(derivedType))
                    {
                        sb.AppendLine($"    {MembersMetaRefTraverseExpression(derivedTypeSpec.Type, "concreteTypedValue")};");
                        sb.AppendLine("    if (context.MetaRefTraversal.IsMutatingOperation)");
                        sb.Indent("    {");
                        sb.AppendLine($"    {varName} = concreteTypedValue;");
                        sb.Unindent("    }");
                    }
                    else
                    {
                        sb.AppendLine($"    // This concrete type does not contain MetaRefs");
                    }
                    sb.AppendLine("    break;");
                }

                sb.AppendLine("case null:");
                sb.AppendLine("    break;");

                sb.AppendLine("default:");
                sb.AppendLine($"    throw new MetaSerializationException($\"Unknown derived type of {_typeSpec.Name}: {{{varName}.GetType().ToGenericTypeString()}}. The derived type must have the [MetaSerializableDerived] attribute.\");");
                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.BeginTraverseAbstractClass(Type, obj);
                if (obj != null)
                {
                    Type derivedType = obj.GetType();
                    visitor.BeginTraverseDerivedClass(Type, obj, derivedType);
                    visitor.Traverse(derivedType, obj);
                    visitor.EndTraverseDerivedClass(Type, obj, derivedType);
                }
                visitor.EndTraverseAbstractClass(Type, obj);
            }
        }

        class ConcreteClassOrStructMembersInfo : IMembersInfo
        {
            struct SpanWriteBlock
            {
                public int MaxBytes;
                public int NumMembers;
                public bool LastMemberHasDynamicSize;
            }

            MetaSerializableType _typeSpec;

            public ConcreteClassOrStructMembersInfo(MetaSerializableType typeSpec)
            {
                _typeSpec = typeSpec ?? throw new ArgumentNullException(nameof(typeSpec));
            }

            public Type Type => _typeSpec.Type;
            public string GlobalNamespaceQualifiedTypeString => _typeSpec.GlobalNamespaceQualifiedTypeString;
            public IEnumerable<Type> DirectlyContainedTypes => _typeSpec.Members.Select(member => member.Type).Distinct();

            static void AppendWriteWireTypeAndTag(IndentedStringBuilder sb, WireDataType wireType, int tagId)
            {
                byte[] bytes = new byte[VarIntUtils.VarIntMaxBytes];
                int numBytes = VarIntUtils.FormatVarInt(bytes, tagId);
                string s = Invariant($"writer.WriteBytesUnchecked{numBytes + 1}({(byte)wireType}");

                for (int i = 0; i < numBytes; ++i)
                    s += Invariant($", {bytes[i]}");

                s += ");";
                sb.AppendLine(s);
            }

            public int? MaxSerializedBytesStatic()
            {
                int maxBytes = 1; // EndStruct
                foreach (MetaSerializableMember member in _typeSpec.Members)
                {
                    Type memberType = member.Type;
                    int? memberMaxBytesMaybe = s_typeInfoCache[memberType].MaxSerializedBytesStatic();
                    if (!memberMaxBytesMaybe.HasValue)
                        return null;
                    int memberMaxBytes = memberMaxBytesMaybe.Value;
                    // Add contribution of wiretype and tag
                    memberMaxBytes += 1 + VarIntUtils.CountVarIntBytes(member.TagId);
                    // Check if member fits in span
                    if (memberMaxBytes > MaxSpanSize)
                        return null;
                    maxBytes += memberMaxBytes;
                }
                return maxBytes;
            }

            void AppendSerializeMember(IndentedStringBuilder sb, MetaSerializableMember member, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                Type memberType = member.Type;
                WireDataType wireType = TaggedWireSerializer.GetWireType(memberType);

                int? addedInVersion = member.CodeGenInfo.AddedInLogicVersion;
                int? removedInVersion = member.CodeGenInfo.RemovedInLogicVersion;
                bool hasVersionInfo = (addedInVersion != null) || (removedInVersion != null);
                bool hasFlags = member.Flags != MetaMemberFlags.None;
                List<string> conditions = hasVersionInfo || hasFlags ? new List<string>() : null;

                // Handle member versioning (if has version attributes)
                if (hasVersionInfo)
                {
                    string minVersion = addedInVersion?.ToString(CultureInfo.InvariantCulture) ?? "0";
                    string maxVersion = removedInVersion?.ToString(CultureInfo.InvariantCulture) ?? "int.MaxValue";
                    conditions.Add($"IsVersionInRange(ref context, {minVersion}, {maxVersion})");
                }

                // Handle member flags (if has any)
                if (hasFlags)
                {
                    string flagsStr = MemberFlagsToMask(member.Flags);
                    conditions.Add($"((context.ExcludeFlags & {flagsStr}) == 0)");
                }

                if (conditions != null)
                {
                    sb.AppendLine($"if ({string.Join(" && ", conditions)})");
                    sb.Indent("{");
                }

                if (sb.DebugEnabled)
                    sb.AppendDebugLine(Invariant($"context.DebugStream?.AppendLine($\"@{{writer.Offset}} {_typeSpec.Name}.{member.Name} (wireType={wireType}, tagId={member.TagId})\");"));

                AppendWriteWireTypeAndTag(sb, wireType, member.TagId);
                string getMemberStr      = useTrampolines ? $"Trampolines.Get_{MethodSuffixForType(_typeSpec.Type)}_{member.Name}(ref {valueExpr})" : $"{valueExpr}.{member.Name}";
                sb.AppendLine($"{SerializationExpression(memberType, getMemberStr, spaceEnsured)};");

                if (conditions != null)
                {
                    sb.Unindent("}");
                }
            }

            public void AppendMembersSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                if (sb.DebugEnabled)
                {
                    sb.AppendDebugLine($"context.DebugStream?.AppendLine($\"@{{writer.Offset}} write {_typeSpec.Name} members\");");
                    sb.AppendDebugLine();
                }

                List<SpanWriteBlock> blocks = new List<SpanWriteBlock>();
                SpanWriteBlock current = new SpanWriteBlock();

                // Form blocks of members that can be written into a single contiguous span. Compute the max serialized size for each
                // block so that we can do a single EnsureSpace call for the entire block of members. A block can be split due to
                //   1. a dynamic size member being encountered, or
                //   2. the MaxSpanSize limit being met
                //
                // In case 1. the dynamic size member is added as the final member of the block and the fact that it is dynamic
                // is recorded by setting `LastMemberHasDynamicSize`.
                foreach (MetaSerializableMember member in _typeSpec.Members)
                {
                    Type memberType = member.Type;
                    int? maxPayloadBytes = s_typeInfoCache[memberType].MaxSerializedBytesStatic();
                    int bytesForMember = 1 + VarIntUtils.CountVarIntBytes(member.TagId);
                    bool useStaticSize = maxPayloadBytes.HasValue && ((maxPayloadBytes.Value + bytesForMember) <= MaxSpanSize);
                    if (useStaticSize)
                        bytesForMember += maxPayloadBytes.Value;

                    // Split block by MaxSpanSize
                    if (current.NumMembers > 0 && current.MaxBytes + bytesForMember > MaxSpanSize)
                    {
                        blocks.Add(current);
                        current = new SpanWriteBlock();
                    }

                    // Add to current block
                    current.NumMembers++;
                    current.MaxBytes += bytesForMember;

                    // Flush block if current member max bytes not known => must be written with dynamic size
                    if (!useStaticSize)
                    {
                        current.LastMemberHasDynamicSize = true;
                        blocks.Add(current);
                        current = new SpanWriteBlock();
                    }
                }

                // Add EndStruct, into separate block if would exceed span size
                if (current.MaxBytes + 1 > MaxSpanSize)
                {
                    blocks.Add(current);
                    current = new SpanWriteBlock();
                }
                current.MaxBytes += 1;
                blocks.Add(current);

                MetaDebug.Assert(!spaceEnsured || blocks.Count == 1, "Writing members directly to span requires single block");

                // Write out blocks
                bool isFirst = true;
                int memberIndex = 0;

                foreach (SpanWriteBlock block in blocks)
                {
                    if (!spaceEnsured)
                    {
                        if (isFirst)
                            isFirst = false;
                        else
                            sb.AppendLine();

                        sb.AppendLine(Invariant($"writer.EnsureSpace({block.MaxBytes});"));
                    }

                    int numMembers = block.NumMembers;
                    while (numMembers-- > 0)
                    {
                        MetaSerializableMember member = _typeSpec.Members[memberIndex++];
                        bool spaceEnsuredForMember = numMembers > 0 || !block.LastMemberHasDynamicSize;
                        sb.AppendLine(Invariant($"context.MemberContext.Update(\"{member.DeclaringType.Name}.{member.Name}\", {member.CodeGenInfo.MaxCollectionSize});"));
                        AppendSerializeMember(sb, member, valueExpr, useTrampolines, spaceEnsuredForMember);
                    }
                }
                sb.AppendLine("writer.WriteByteUnchecked((byte)WireDataType.EndStruct);");
            }

            public void AppendMembersDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                if (_typeSpec.UsesConstructorDeserialization)
                {
                    foreach (MetaSerializableMember member in _typeSpec.Members)
                    {
                        sb.AppendLine(member.Type.ToGlobalNamespaceQualifiedTypeString() + " " + member.Name + $" = default({member.Type.ToGlobalNamespaceQualifiedTypeString()});");
                    }
                }
                else
                {
                    sb.AppendLine($"{varName} = {CreateInstanceExpression(_typeSpec, useTrampolines)};");
                }

                sb.AppendLine("for (;;)");
                sb.Indent("{");

                // EndStruct terminates the struct
                sb.AppendLine("WireDataType memberWireType = ReadWireType(reader);");
                sb.AppendLine("if (memberWireType == WireDataType.EndStruct)");
                sb.AppendLine("    break;");
                sb.AppendLine();

                // Read member based on tag (or skip if unknown)
                sb.AppendLine("int tagId = ReadTagId(reader);");
                sb.AppendLine("switch (tagId)");
                sb.Indent("{");
                foreach (MetaSerializableMember member in _typeSpec.Members)
                {
                    // If member has flags, perform flags check before deserializing
                    bool hasFlags = member.Flags != MetaMemberFlags.None;
                    string flagsCheckPrefix = "";
                    string flagsCheckSuffix = "";
                    if (hasFlags)
                    {
                        string flagsStr = MemberFlagsToMask(member.Flags);
                        flagsCheckPrefix = $"if ((context.ExcludeFlags & {flagsStr}) == 0) {{ ";
                        flagsCheckSuffix = " } else SkipWireType(reader, memberWireType);";
                    }

                    string deserializeStr = $"{DeserializationExpression(member.Type, "member", "memberWireType")};";
                    string setMemberStr;
                    if (!_typeSpec.UsesConstructorDeserialization)
                        setMemberStr = useTrampolines ? $"Trampolines.Set_{MethodSuffixForType(_typeSpec.Type)}_{member.Name}(ref {varName}, member);" : $"{varName}.{member.Name} = member;";
                    else
                        setMemberStr = $"{member.Name} = member;";

                    if (member.OnDeserializationFailureMethod == null)
                        sb.AppendLine(Invariant($"case {member.TagId}: {{ context.MemberContext.Update(\"{member.DeclaringType.Name}.{member.Name}\", {member.CodeGenInfo.MaxCollectionSize}); {flagsCheckPrefix}{deserializeStr} {setMemberStr}{flagsCheckSuffix} break; }}"));
                    else
                    {
                        string onFailureMethodStr;
                        if (useTrampolines)
                            onFailureMethodStr = $"Trampolines.InvokeOnMemberDeserializationFailure_{MethodSuffixForType(_typeSpec.Type)}_{member.Name}";
                        else
                            onFailureMethodStr = member.OnDeserializationFailureMethod.ToMemberWithGlobalNamespaceQualifiedTypeString();

                        sb.AppendLine(Invariant($"case {member.TagId}:"));
                        sb.Indent("{");

                        if (hasFlags)
                            sb.Indent(flagsCheckPrefix);
                        else
                            MetaDebug.Assert(flagsCheckPrefix == "", "!hasFlags, but check prefix is nonempty");

                        sb.AppendLine("int offsetAtMemberStart = reader.Offset;");
                        sb.AppendLine("try");
                        sb.Indent("{");
                        sb.AppendLine(deserializeStr);
                        sb.AppendLine(setMemberStr);
                        sb.Unindent("}");
                        sb.AppendLine("catch (Exception exception)");
                        sb.Indent("{");
                        sb.AppendLine("byte[] memberPayload = ReadBytesForWireObjectStartingAt(reader, memberWireType, offsetAtMemberStart);");
                        sb.AppendLine($"MetaMemberDeserializationFailureParams failureParams = new MetaMemberDeserializationFailureParams(memberPayload, exception);");
                        sb.AppendLine($"{member.CodeGenInfo.GlobalNamespaceQualifiedTypeString} member = {onFailureMethodStr}(failureParams);");
                        sb.AppendLine(setMemberStr);
                        sb.Unindent("}");

                        if (hasFlags)
                            sb.Unindent(flagsCheckSuffix);
                        else
                            MetaDebug.Assert(flagsCheckSuffix == "", "!hasFlags, but check suffix is nonempty");

                        sb.AppendLine("break;");
                        sb.Unindent("}");
                    }
                }
                sb.AppendLine("default: SkipWireType(reader, memberWireType); break;");
                sb.Unindent("}"); // switch

                sb.Unindent("}"); // for loop

                if (_typeSpec.UsesConstructorDeserialization)
                {
                    ConstructorInfo bestMatchingConstructor = _typeSpec.DeserializationConstructor;
                    string          parameters              = string.Join(", ", bestMatchingConstructor.GetParameters().Select(x => _typeSpec.Members.First(member => member.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase)).Name));

                    if (useTrampolines)
                        sb.AppendLine($"{varName} = Trampolines.CreateInstance_{MethodSuffixForType(_typeSpec.Type)}(new object[]{{{parameters}}});");
                    else
                        sb.AppendLine($"{varName} = new {_typeSpec.Type.ToGlobalNamespaceQualifiedTypeString()}({parameters});");
                }

                // Invoke [MetaOnDeserialized]-marked methods
                if (_typeSpec.OnDeserializedMethods.Count > 0)
                {
                    sb.AppendLine();
                    foreach (MethodInfo onDeserializedMethod in _typeSpec.OnDeserializedMethods)
                    {
                        bool takesParams = onDeserializedMethod.GetParameters().Any();

                        if (useTrampolines)
                        {
                            if (takesParams)
                                sb.AppendLine($"Trampolines.InvokeOnDeserializedWithParams_{MethodSuffixForType(_typeSpec.Type)}_{onDeserializedMethod.Name}(ref {varName}, new MetaOnDeserializedParams(ref context));");
                            else
                                sb.AppendLine($"Trampolines.InvokeOnDeserialized_{MethodSuffixForType(_typeSpec.Type)}_{onDeserializedMethod.Name}(ref {varName});");
                        }
                        else
                        {
                            if (takesParams)
                                sb.AppendLine($"{varName}.{onDeserializedMethod.Name}(new MetaOnDeserializedParams(ref context));");
                            else
                                sb.AppendLine($"{varName}.{onDeserializedMethod.Name}();");
                        }
                    }
                }
            }

            public void AppendMembersMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                bool isFirst = true;
                foreach (MetaSerializableMember member in _typeSpec.Members)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        sb.AppendLine();

                    Type memberType = member.Type;

                    if (metaRefContainingTypes.Contains(memberType))
                    {
                        // Handle member versioning (if has version attributes)
                        int? addedInVersion = member.CodeGenInfo.AddedInLogicVersion;
                        int? removedInVersion = member.CodeGenInfo.RemovedInLogicVersion;
                        bool hasVersionInfo = (addedInVersion != null) || (removedInVersion != null);
                        if (hasVersionInfo)
                        {
                            string minVersion = addedInVersion?.ToString(CultureInfo.InvariantCulture) ?? "0";
                            string maxVersion = removedInVersion?.ToString(CultureInfo.InvariantCulture) ?? "int.MaxValue";
                            sb.AppendLine($"if (IsVersionInRange(ref context, {minVersion}, {maxVersion}))");
                            sb.Indent("{");
                        }

                        // Handle member flags (if has any)
                        bool hasFlags = member.Flags != MetaMemberFlags.None;
                        if (hasFlags)
                        {
                            string flagsStr = MemberFlagsToMask(member.Flags);
                            sb.AppendLine($"if ((context.ExcludeFlags & {flagsStr}) == 0)");
                            sb.Indent("{");
                        }

                        sb.AppendLine("{");
                        string getMemberStr = useTrampolines ? $"Trampolines.Get_{MethodSuffixForType(_typeSpec.Type)}_{member.Name}(ref {varName})" : $"{varName}.{member.Name}";
                        string setMemberStr = useTrampolines ? $"Trampolines.Set_{MethodSuffixForType(_typeSpec.Type)}_{member.Name}(ref {varName}, updatedMember);" : $"{varName}.{member.Name} = updatedMember;";
                        sb.AppendLine($"{member.CodeGenInfo.GlobalNamespaceQualifiedTypeString} updatedMember = {getMemberStr};");
                        sb.AppendLine($"{MetaRefTraverseExpression(memberType, "updatedMember")};");

                        sb.AppendLine($"if (context.MetaRefTraversal.IsMutatingOperation)");
                        sb.Indent("{");
                        sb.AppendLine(setMemberStr);
                        sb.Unindent("}");
                        sb.AppendLine("}");

                        if (hasFlags)
                            sb.Unindent("}");

                        if (hasVersionInfo)
                            sb.Unindent("}");
                    }
                    else
                        sb.AppendLine($"// Type {member.CodeGenInfo.GlobalNamespaceQualifiedTypeString} of member {member.Name} does not contain MetaRefs");
                }
            }

            public void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                foreach (MetaSerializableMember member in _typeSpec.Members)
                {
                    object memberObj = member.GetValue(obj);
                    visitor.BeginTraverseMember(Type, obj, member, memberObj);
                    visitor.Traverse(member.Type, memberObj);
                    visitor.EndTraverseMember(Type, obj, member, memberObj);
                }
            }
        }

        class EnumInfo : MetaSerializableTypeInfo
        {
            public EnumInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                UnderlyingType = _typeSpec.Type.GetEnumUnderlyingType();
            }

            readonly Type UnderlyingType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ UnderlyingType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                string underlyingTypeName = UnderlyingType.ToGlobalNamespaceQualifiedTypeString();
                sb.AppendLine($"{SerializationExpression(UnderlyingType, $"({underlyingTypeName}){valueExpr}", spaceEnsured)};");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                WireDataType underlyingWireType = TaggedWireSerializer.GetWireType(UnderlyingType);
                sb.AppendLine($"{DeserializationExpression(UnderlyingType, "underlyingValue", $"WireDataType.{underlyingWireType}")};");
                sb.AppendLine($"{varName} = ({_typeSpec.GlobalNamespaceQualifiedTypeString})underlyingValue;");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // Enums do not contain MetaRefs.
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitEnum(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                return s_typeInfoCache[UnderlyingType].MaxSerializedBytesStatic();
            }
        }

        class NullableEnumInfo : MetaSerializableTypeInfo
        {
            public NullableEnumInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                EnumType = _typeSpec.Type.GetSystemNullableElementType();
                EnumUnderlyingType = EnumType.GetEnumUnderlyingType();
            }

            readonly Type EnumType;
            readonly Type EnumUnderlyingType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ NullableType(EnumUnderlyingType) };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                string enumUnderlyingTypeName = EnumUnderlyingType.ToGlobalNamespaceQualifiedTypeString();
                sb.AppendLine($"{SerializationExpression(NullableType(EnumUnderlyingType), $"({enumUnderlyingTypeName}?){valueExpr}", spaceEnsured)};");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                WireDataType nullableUnderlyingWireType = TaggedWireSerializer.PrimitiveTypeWrapNullable(TaggedWireSerializer.GetWireType(EnumUnderlyingType));
                sb.AppendLine($"{DeserializationExpression(NullableType(EnumUnderlyingType), "nullableUnderlyingValue", $"WireDataType.{nullableUnderlyingWireType}")};");
                sb.AppendLine($"{varName} = ({_typeSpec.GlobalNamespaceQualifiedTypeString})nullableUnderlyingValue;");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // Nullable enums do not contain MetaRefs.
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitNullableEnum(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                return s_typeInfoCache[NullableType(EnumUnderlyingType)].MaxSerializedBytesStatic();
            }
        }

        class StringIdInfo : MetaSerializableTypeInfo
        {
            public StringIdInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
            }

            public override IEnumerable<Type> DirectlyContainedTypes => Enumerable.Empty<Type>();

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"writer.WriteString({valueExpr} != null ? {valueExpr}.Value : null);");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine($"{varName} = {_typeSpec.GlobalNamespaceQualifiedTypeString}.FromString(reader.ReadString(context.MaxStringSize));");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitStringId(Type, obj);
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // StringIds do not contain MetaRefs.
            }
        }

        class DynamicEnumInfo : MetaSerializableTypeInfo
        {
            public DynamicEnumInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
            }

            public override IEnumerable<Type> DirectlyContainedTypes => Enumerable.Empty<Type>();

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                sb.AppendLine($"writer.WriteVarInt{GetWriteFuncSuffix(spaceEnsured)}({valueExpr}.Id);");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                sb.AppendLine("int typeCode = reader.ReadVarInt();");
                sb.AppendLine($"{varName} = {_typeSpec.GlobalNamespaceQualifiedTypeString}.FromId(typeCode) as {_typeSpec.GlobalNamespaceQualifiedTypeString} ?? throw new MetaSerializationException($\"Unknown enum element of {_typeSpec.Name}: {{typeCode}}\");");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // DynamicEnums do not contain MetaRefs.
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitDynamicEnum(Type, obj);
            }

            protected override int? MaxSerializedBytes() => VarIntUtils.VarIntMaxBytes;
        }

        class GameConfigDataInfo : MetaSerializableTypeInfo
        {
            public GameConfigDataInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                ConfigKeyType = _typeSpec.Type.GetGenericInterfaceTypeArguments(typeof(IGameConfigKey<>))[0];
            }

            readonly Type ConfigKeyType;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ ConfigKeyType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                bool keyCanBeNull = ConfigKeyType.CanBeNull();
                string configKeyTypeName = ConfigKeyType.ToGlobalNamespaceQualifiedTypeString();

                sb.AppendLine($"IsConvertibleTo<IGameConfigKey<{configKeyTypeName}>>({valueExpr});");
                sb.AppendLine($"if ({valueExpr} != null)");
                sb.Indent("{");
                sb.AppendLine($"{SerializationExpression(ConfigKeyType, $"((IGameConfigKey<{configKeyTypeName}>){valueExpr}).ConfigKey", spaceEnsured)};");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");

                if (keyCanBeNull)
                    sb.AppendLine($"{SerializationExpression(ConfigKeyType, $"({configKeyTypeName})null", spaceEnsured)};");
                else if (_typeSpec.ConfigNullSentinelKey != null)
                {
                    sb.AppendLine($"{configKeyTypeName} sentinel = ({configKeyTypeName})MetaSerializerTypeRegistry.GetTypeSpec(typeof({_typeSpec.GlobalNamespaceQualifiedTypeString})).ConfigNullSentinelKey;");
                    sb.AppendLine($"{SerializationExpression(ConfigKeyType, "sentinel", spaceEnsured)};");
                }
                else
                    sb.AppendLine($"throw new MetaSerializationException(\"Null {_typeSpec.Name} cannot be serialized, because its key type {ConfigKeyType.ToGenericTypeString()} is not nullable and {_typeSpec.Name} has no ConfigNullSentinelKey defined. A config reference is serialized as its key.\");");

                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                string          typeStr         = _typeSpec.GlobalNamespaceQualifiedTypeString;
                WireDataType    wireType        = TaggedWireSerializer.GetWireType(_typeSpec.Type);

                sb.AppendLine($"{DeserializationExpression(ConfigKeyType, "configKey", $"WireDataType.{wireType}")};");
                // \todo [petri] implement generic getter to avoid boxing?

                bool keyCanBeNull = ConfigKeyType.CanBeNull();
                string configKeyTypeName = ConfigKeyType.ToGlobalNamespaceQualifiedTypeString();

                if (keyCanBeNull)
                {
                    sb.AppendLine("if (configKey != null)");
                    sb.Indent("{");
                }
                else if (_typeSpec.ConfigNullSentinelKey != null)
                {
                    sb.AppendLine($"if (!configKey.Equals(({configKeyTypeName})MetaSerializerTypeRegistry.GetTypeSpec(typeof({_typeSpec.GlobalNamespaceQualifiedTypeString})).ConfigNullSentinelKey))");
                    sb.Indent("{");
                }

                sb.AppendLine($"IGameConfigDataResolver resolver = context.Resolver ?? throw new InvalidOperationException($\"Trying to deserialize a config data reference to '{{configKey}}' (type {_typeSpec.Type.ToGenericTypeString()}), but reference resolver is null. Note: if this happened while loading the game config, it probably means the reference needs to be wrapped in MetaRef<>, i.e. MetaRef<{_typeSpec.Type.ToGenericTypeString()}> .\");");
                sb.AppendLine($"{varName} = ({typeStr})resolver.ResolveReference(typeof({typeStr}), configKey);");

                if (keyCanBeNull || _typeSpec.ConfigNullSentinelKey != null)
                {
                    sb.Unindent("}");
                    sb.AppendLine("else");
                    sb.Indent("{");
                    sb.AppendLine($"{varName} = null;");
                    sb.Unindent("}");
                }
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                // A game config data reference is just a reference, so we do not recurse into it.
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitGameConfigData(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                return s_typeInfoCache[ConfigKeyType].MaxSerializedBytesStatic();
            }
        }

        class MetaRefInfo : MetaSerializableTypeInfo
        {
            public MetaRefInfo(MetaSerializableType typeSpec)
                : base(typeSpec)
            {
                ConfigDataType = _typeSpec.Type.GetGenericArguments()[0];
                ConfigKeyType = ConfigDataType.GetGenericInterfaceTypeArguments(typeof(IGameConfigKey<>))[0];
                ConfigDataTypeName = ConfigDataType.ToGlobalNamespaceQualifiedTypeString();
                ConfigKeyTypeName = ConfigKeyType.ToGlobalNamespaceQualifiedTypeString();
            }

            readonly Type ConfigDataType;
            readonly Type ConfigKeyType;
            readonly string ConfigDataTypeName;
            readonly string ConfigKeyTypeName;

            public override IEnumerable<Type> DirectlyContainedTypes => new Type[]{ ConfigKeyType };

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                bool keyCanBeNull = ConfigKeyType.CanBeNull();
                object nullSentinelKey = MetaSerializerTypeRegistry.GetTypeSpec(ConfigDataType).ConfigNullSentinelKey;

                sb.AppendLine($"if ({valueExpr} != null)");
                sb.Indent("{");
                sb.AppendLine($"{SerializationExpression(ConfigKeyType, $"({ConfigKeyTypeName}){valueExpr}.KeyObject", spaceEnsured)};");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");

                if (keyCanBeNull)
                    sb.AppendLine($"{SerializationExpression(ConfigKeyType, $"({ConfigKeyTypeName})null", spaceEnsured)};");
                else if (nullSentinelKey != null)
                {
                    sb.AppendLine($"{ConfigKeyTypeName} sentinel = ({ConfigKeyTypeName})MetaSerializerTypeRegistry.GetTypeSpec(typeof({ConfigDataTypeName})).ConfigNullSentinelKey;");
                    sb.AppendLine($"{SerializationExpression(ConfigKeyType, "sentinel", spaceEnsured)};");
                }
                else
                    sb.AppendLine($"throw new MetaSerializationException(\"Null {_typeSpec.Name} cannot be serialized, because its key type {ConfigKeyType.ToGenericTypeString()} is not nullable and {ConfigDataTypeName} has no ConfigNullSentinelKey defined. A config reference is serialized as its key.\");");

                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                WireDataType wireType = TaggedWireSerializer.GetWireType(_typeSpec.Type);

                sb.AppendLine($"{DeserializationExpression(ConfigKeyType, "configKey", $"WireDataType.{wireType}")};");

                bool keyCanBeNull = ConfigKeyType.CanBeNull();
                object nullSentinelKey = MetaSerializerTypeRegistry.GetTypeSpec(ConfigDataType).ConfigNullSentinelKey;

                if (keyCanBeNull)
                {
                    sb.AppendLine("if (configKey != null)");
                    sb.Indent("{");
                }
                else if (nullSentinelKey != null)
                {
                    sb.AppendLine($"if (!configKey.Equals(({ConfigKeyType.ToGlobalNamespaceQualifiedTypeString()})MetaSerializerTypeRegistry.GetTypeSpec(typeof({ConfigDataType.ToGlobalNamespaceQualifiedTypeString()})).ConfigNullSentinelKey))");
                    sb.Indent("{");
                }

                sb.AppendLine($"MetaRef<{ConfigDataTypeName}> metaRef = MetaRef<{ConfigDataTypeName}>.FromKey(configKey);");
                sb.AppendLine("if (context.Resolver != null)");
                sb.AppendLine("    metaRef = metaRef.CreateResolved(context.Resolver);");
                sb.AppendLine($"{varName} = metaRef;");

                if (keyCanBeNull || nullSentinelKey != null)
                {
                    sb.Unindent("}");
                    sb.AppendLine("else");
                    sb.Indent("{");
                    sb.AppendLine($"{varName} = null;");
                    sb.Unindent("}");
                }
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"if ({varName} != null)");
                sb.Indent("{");
                sb.AppendLine($"var newRef = context.VisitMetaRef({varName});");
                sb.AppendLine($"if (context.MetaRefTraversal.IsMutatingOperation)");
                sb.Indent("{");
                sb.AppendLine($"{varName} = newRef;");
                sb.Unindent("}");
                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                visitor.VisitMetaRef(Type, obj);
            }

            protected override int? MaxSerializedBytes()
            {
                return s_typeInfoCache[ConfigKeyType].MaxSerializedBytesStatic();
            }
        }

        class GameConfigDataContentInfo : MetaSerializableTypeInfo
        {
            public GameConfigDataContentInfo(MetaSerializableType typeSpec, MetaSerializableType gameConfigDataTypeSpec)
                : base(typeSpec)
            {
                if (gameConfigDataTypeSpec.Type != typeSpec.Type.GetGenericArguments()[0])
                    throw new ArgumentException($"{gameConfigDataTypeSpec.Type.ToNamespaceQualifiedTypeString()} does not match config data content type of {typeSpec.Type.ToNamespaceQualifiedTypeString()}");

                _gameConfigDataTypeSpec = gameConfigDataTypeSpec;
            }

            MetaSerializableType _gameConfigDataTypeSpec;

            Type GameConfigDataType => _gameConfigDataTypeSpec.Type;

            public override IEnumerable<Type> DirectlyContainedTypes => new ConcreteClassOrStructMembersInfo(_gameConfigDataTypeSpec).DirectlyContainedTypes;

            public override void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured)
            {
                // \note GameConfigDataContent<TConfigData> has `class` constraint on TConfigData,
                //       so we know it is nullable. If you remove the constraint, please update
                //       this code to not assume nullability.
                //       #config-data-content-nullable
                // \note IsClass isn't exactly the same as the `class` constraint, but good enough.
                MetaDebug.Assert(GameConfigDataType.IsClass, $"Expected {_typeSpec.Type.ToGenericTypeString()}'s content type to be a class type but it isn't");

                sb.AppendLine($"if ({valueExpr}.ConfigData != null)");
                sb.Indent("{");
                sb.AppendLine("writer.WriteVarIntConst(VarIntConst.One);");
                sb.AppendLine();
                sb.AppendLine($"{MembersSerializationExpression(GameConfigDataType, $"{valueExpr}.ConfigData", false)};");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine("writer.WriteVarIntConst(VarIntConst.Zero);");
                sb.Unindent("}");
            }

            public override void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines)
            {
                // \note GameConfigDataContent<TConfigData> has `class` constraint on TConfigData,
                //       so we know it is nullable. If you remove the constraint, please update
                //       this code to not assume nullability.
                //       #config-data-content-nullable
                // \note IsClass isn't exactly the same as the `class` constraint, but good enough.
                MetaDebug.Assert(GameConfigDataType.IsClass, $"Expected {_typeSpec.Type.ToGenericTypeString()}'s content type to be a class type but it isn't");

                sb.AppendLine("int hasValue = reader.ReadVarInt();");
                sb.AppendLine("if (hasValue != 0)");
                sb.Indent("{");
                sb.AppendLine($"{MembersDeserializationExpression(GameConfigDataType, "configData")};");
                sb.AppendLine($"{varName} = new {_typeSpec.GlobalNamespaceQualifiedTypeString}(configData);");
                sb.Unindent("}");
                sb.AppendLine("else");
                sb.Indent("{");
                sb.AppendLine($"{varName} = new {_typeSpec.GlobalNamespaceQualifiedTypeString}(null);");
                sb.Unindent("}");
            }

            public override void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
            {
                sb.AppendLine($"if ({varName}.ConfigData != null)");
                sb.Indent("{");
                sb.AppendLine($"{_gameConfigDataTypeSpec.GlobalNamespaceQualifiedTypeString} newConfigData = {varName}.ConfigData;");
                sb.AppendLine($"{MembersMetaRefTraverseExpression(GameConfigDataType, "newConfigData")};");
                sb.AppendLine($"if (context.MetaRefTraversal.IsMutatingOperation)");
                sb.Indent("{");
                sb.AppendLine($"{varName} = new {_typeSpec.GlobalNamespaceQualifiedTypeString}(newConfigData);");
                sb.Unindent("}");
                sb.Unindent("}");
            }

            public override void Traverse(object obj, DynamicTraversal.IVisitor visitor)
            {
                IGameConfigDataContent configDataContent = (IGameConfigDataContent)obj;
                visitor.BeginTraverseGameConfigDataContent(Type, obj);
                if (obj != null)
                    visitor.TraverseMembers(GameConfigDataType, configDataContent.ConfigDataObject);
                visitor.EndTraverseGameConfigDataContent(Type, obj);
            }
        }
    }
}
