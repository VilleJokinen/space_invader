// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    public static partial class TaggedSerializerRoslynGenerator
    {
        // Upper limit for the SpanWriter.EnsureSpace() request in generated code. Larger value allows grouping more writes into a single
        // block but potentially causes more wasted space in the write buffers.
        static readonly int MaxSpanSize = 256;
        static Dictionary<Type, ITypeInfo> s_typeInfoCache;
        static Dictionary<Type, ITypeInfo> s_additionalTypeInfos = new Dictionary<Type, ITypeInfo>()
        {
            {typeof(System.Guid), new GuidInfo(typeof(Guid))},
            {typeof(System.Guid?), new GuidInfo(typeof(Guid?))}
        };

        static string MemberFlagsToMask(MetaMemberFlags flags)
        {
            string[] flagsStr = EnumUtil.GetValues<MetaMemberFlags>().Where(flag => flag != 0 && flags.HasFlag(flag)).Select(v => $"MetaMemberFlags.{v}").ToArray();
            if (flagsStr.Length > 1)
                return $"({string.Join(" | ", flagsStr)})";
            else
                return flagsStr[0];
        }

        static Type NullableType(Type containedType)
        {
            return typeof(Nullable<>).MakeGenericType(containedType);
        }

        static string CreateInstanceExpression(MetaSerializableType typeSpec, bool useTrampolines)
        {
            // \note Trampolines are not needed for value types, as they are always publicly default-constructible
            if (useTrampolines && !typeSpec.Type.IsValueType)
                return $"Trampolines.CreateInstance_{MethodSuffixForType(typeSpec.Type)}()";
            else
                return $"new {typeSpec.Type.ToGlobalNamespaceQualifiedTypeString()}()";
        }

        static bool TryGetConverterWireTypesPredicateExpression(Type type, string varName, out string predicateStr)
        {
            if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType typeSpec)
                && typeSpec.DeserializationConverters.Length > 0)
            {
                IEnumerable<string> comparisons = typeSpec.DeserializationConverters.Select(converter => $"{varName} == WireDataType.{converter.AcceptedWireDataType}");
                string expression = string.Join(" || ", comparisons);
                predicateStr = $"({expression})";
                return true;
            }
            else
            {
                predicateStr = null;
                return false;
            }
        }

        /// <summary>
        /// Return a suffix that should be used for a serialization/deserialization
        /// method associated with the given type. This is done to give unique (or
        /// almost unique - doesn't have to be perfect) names for the methods, in
        /// order to avoid overloads. Overloads are avoided because they significantly
        /// increase the duration of compiling the generated serializer.
        /// </summary>
        static string MethodSuffixForType(Type type)
        {
            return s_typeInfoCache[type].GlobalNamespaceQualifiedTypeString
                .Replace("global::", "")
                .Replace("[]", "Array")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(".", "_")
                .Replace(",", "_");
        }

        static string SerializationMethodSignature(ITypeInfo typeInfo, string valueParameterName, bool spaceEnsured)
        {
            if (spaceEnsured)
                return $"static void SerializeUnchecked_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, ref SpanWriter writer, {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
            else
                return $"static void Serialize_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, ref SpanWriter writer, {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
        }

        static string SerializationExpression(Type type, string valueStr, bool spaceEnsured)
        {
            if (spaceEnsured)
                return Invariant($"SerializeUnchecked_{MethodSuffixForType(type)}(ref context, ref writer, {valueStr})");
            else
                return Invariant($"Serialize_{MethodSuffixForType(type)}(ref context, ref writer, {valueStr})");
        }

        static string DeserializationMethodSignature(ITypeInfo typeInfo, string valueParameterName)
        {
            return $"static void Deserialize_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, IOReader reader, WireDataType wireType, out {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
        }
        static string DeserializationExpression(Type type, string varName, string wireTypeStr)
        {
            return Invariant($"Deserialize_{MethodSuffixForType(type)}(ref context, reader, {wireTypeStr}, out {type.ToGlobalNamespaceQualifiedTypeString()} {varName})");
        }

        static string MembersSerializationMethodSignature(IMembersInfo typeInfo, string valueParameterName, bool spaceEnsured)
        {
            if (spaceEnsured)
                return $"static void SerializeMembersUnchecked_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, ref SpanWriter writer, {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
            else
                return $"static void SerializeMembers_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, ref SpanWriter writer, {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
        }

        static string MembersSerializationExpression(Type type, string valueStr, bool spaceEnsured)
        {
            if (spaceEnsured)
                return $"SerializeMembersUnchecked_{MethodSuffixForType(type)}(ref context, ref writer, {valueStr})";
            else
                return $"SerializeMembers_{MethodSuffixForType(type)}(ref context, ref writer, {valueStr})";
        }
        static string MembersDeserializationMethodSignature(IMembersInfo typeInfo, string valueParameterName)
        {
            return $"static void DeserializeMembers_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, IOReader reader, out {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
        }
        static string MembersDeserializationExpression(Type type, string varName)
        {
            return $"DeserializeMembers_{MethodSuffixForType(type)}(ref context, reader, out {type.ToGlobalNamespaceQualifiedTypeString()} {varName})";
        }

        static string TableSerializationMethodSignature(MetaSerializableType itemTypeSpec, string itemsParameterName)
        {
            return $"static void SerializeTable_{MethodSuffixForType(itemTypeSpec.Type)}(ref MetaSerializationContext context, ref SpanWriter writer, IReadOnlyList<{itemTypeSpec.GlobalNamespaceQualifiedTypeString}> {itemsParameterName})";
        }
        static string TableSerializationExpression(Type itemType, string itemsStr)
        {
            return $"SerializeTable_{MethodSuffixForType(itemType)}(ref context, ref writer, {itemsStr})";
        }
        static string TableDeserializationMethodSignature(MetaSerializableType itemTypeSpec, string itemsParameterName)
        {
            return $"static void DeserializeTable_{MethodSuffixForType(itemTypeSpec.Type)}(ref MetaSerializationContext context, IOReader reader, out List<{itemTypeSpec.GlobalNamespaceQualifiedTypeString}> {itemsParameterName})";
        }
        static string TableDeserializationExpression(Type itemType, string itemsVarName)
        {
            return $"DeserializeTable_{MethodSuffixForType(itemType)}(ref context, reader, out List<{itemType.ToGlobalNamespaceQualifiedTypeString()}> {itemsVarName})";
        }

        static string MetaRefTraverseMethodSignature(ITypeInfo typeInfo, string valueParameterName)
        {
            return $"static void TraverseMetaRefs_{MethodSuffixForType(typeInfo.Type)}(ref MetaSerializationContext context, ref {typeInfo.GlobalNamespaceQualifiedTypeString} {valueParameterName})";
        }
        static string MetaRefTraverseExpression(Type type, string varName)
        {
            return $"TraverseMetaRefs_{MethodSuffixForType(type)}(ref context, ref {varName})";
        }
        static string MembersMetaRefTraverseMethodSignature(Type type, string valueParameterName)
        {
            return $"static void TraverseMembersMetaRefs_{MethodSuffixForType(type)}(ref MetaSerializationContext context, ref {type.ToGlobalNamespaceQualifiedTypeString()} {valueParameterName})";
        }
        static string MembersMetaRefTraverseExpression(Type type, string varName)
        {
            return $"TraverseMembersMetaRefs_{MethodSuffixForType(type)}(ref context, ref {varName})";
        }
        static string TableMetaRefsTraverseMethodSignature(Type itemType, string itemsParameterName)
        {
            return $"static void TraverseTableMetaRefs_{MethodSuffixForType(itemType)}(ref MetaSerializationContext context, List<{itemType.ToGlobalNamespaceQualifiedTypeString()}> {itemsParameterName})";
        }
        static string TableMetaRefsTraverseExpression(Type itemType, string valueStr)
        {
            return $"TraverseTableMetaRefs_{MethodSuffixForType(itemType)}(ref context, {valueStr})";
        }

        static void AppendHelpers(IndentedStringBuilder sb)
        {
            sb.AppendLine("static void WriteWireType(ref SpanWriter writer, WireDataType wireType)");
            sb.AppendLine("{");
            sb.AppendLine("    writer.WriteByte((byte)wireType);");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static WireDataType ReadWireType(IOReader reader)");
            sb.AppendLine("{");
            sb.AppendLine("    WireDataType wireType = (WireDataType)reader.ReadByte();");
            sb.AppendLine("    return wireType;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static int ReadTagId(IOReader reader)");
            sb.AppendLine("{");
            sb.AppendLine("    int tagId = reader.ReadVarInt();");
            sb.AppendLine("    return tagId;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static void IsConvertibleTo<T>(T _)");
            sb.AppendLine("{");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static bool IsVersionInRange(ref MetaSerializationContext context, int minVersion, int maxVersion) =>");
            sb.AppendLine("    (context.LogicVersion != null) ? (context.LogicVersion >= minVersion && context.LogicVersion < maxVersion) : true;");
            sb.AppendLine();
            sb.AppendLine("static byte[] ReadBytesForWireObjectStartingAt(IOReader reader, WireDataType wireType, int startOffset)");
            sb.AppendLine("{");
            sb.AppendLine("    reader.Seek(startOffset);");
            sb.AppendLine("    TaggedWireSerializer.SkipWireType(reader, wireType);");
            sb.AppendLine("    int endOffset = reader.Offset;");
            sb.AppendLine("    reader.Seek(startOffset);");
            sb.AppendLine("    byte[] bytes = new byte[endOffset - startOffset];");
            sb.AppendLine("    reader.ReadBytes(bytes);");
            sb.AppendLine("    return bytes;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static void ExpectWireType(ref MetaSerializationContext context, IOReader reader, Type type, WireDataType encounteredWireType, WireDataType expectedWireType)");
            sb.AppendLine("{");
            sb.AppendLine("    // Move unlikely control flow to a separate func to skip static inits.");
            sb.AppendLine("    if (encounteredWireType != expectedWireType)");
            sb.AppendLine("        OnExpectWireTypeFail(ref context, reader, type, encounteredWireType, expectedWireType);");
            sb.AppendLine("}");
            sb.AppendLine("static void OnExpectWireTypeFail(ref MetaSerializationContext context, IOReader reader, Type type, WireDataType encounteredWireType, WireDataType expectedWireType)");
            sb.AppendLine("{");
            sb.AppendLine("    TaggedWireSerializer.ExpectWireType(ref context, reader, type, encounteredWireType, expectedWireType);");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static void SkipWireType(IOReader reader, WireDataType wireType)");
            sb.AppendLine("{");
            sb.AppendLine("    // This is unlikely control flow. Moved to a separate func to skip static inits.");
            sb.AppendLine("    TaggedWireSerializer.SkipWireType(reader, wireType);");
            sb.AppendLine("}");
        }

        internal interface ITypeInfo : DynamicTraversal.ITraversableTypeInfo
        {
            Type Type { get; }
            string GlobalNamespaceQualifiedTypeString { get; }
            IEnumerable<Type> DirectlyContainedTypes { get; }
            bool WriteToDebugStreamAtSerializationStart { get; }
            void AppendSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured);
            void AppendDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines);
            void AppendMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines);
            // Upper bound for number of serialized bytes for this type, if known at build time.
            int? MaxSerializedBytesStatic();
        }

        internal interface IMembersInfo : DynamicTraversal.ITraversableTypeInfo
        {
            Type Type { get; }
            string GlobalNamespaceQualifiedTypeString { get; }
            IEnumerable<Type> DirectlyContainedTypes { get; }
            void AppendMembersSerializationCode(IndentedStringBuilder sb, string valueExpr, bool useTrampolines, bool spaceEnsured);
            void AppendMembersDeserializationCode(IndentedStringBuilder sb, string varName, bool useTrampolines);
            void AppendMembersMetaRefTraverseCode(IndentedStringBuilder sb, string varName, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines);
            // Upper bound for number of serialized bytes for this members collection, if known at build time.
            int? MaxSerializedBytesStatic();
        }

        static List<ITypeInfo> GetBuiltinTypeInfos()
        {
            List<ITypeInfo> infos = new List<ITypeInfo>();

            foreach (ITypeInfo info in s_primitiveInfos)
            {
                infos.Add(info);

                if (info.Type.IsValueType)
                {
                    Type nullableType = NullableType(info.Type);
                    infos.Add(new NullablePrimitiveInfo(nullableType, info));
                }
            }

            return infos;
        }

        static ITypeInfo GetSerializableTypeInfo(MetaSerializableType typeSpec)
        {
            if (typeSpec.WireType == WireDataType.ValueCollection)
                return new ValueCollectionInfo(typeSpec);
            else if (typeSpec.WireType == WireDataType.KeyValueCollection)
                return new KeyValueCollectionInfo(typeSpec);
            else if (typeSpec.IsEnum)
                return new EnumInfo(typeSpec);
            else if (typeSpec.IsSystemNullable && typeSpec.Type.GetSystemNullableElementType().IsEnum)
                return new NullableEnumInfo(typeSpec);
            else if (typeSpec.Type.ImplementsInterface<IStringId>())
                return new StringIdInfo(typeSpec);
            else if (typeSpec.Type.ImplementsInterface<IDynamicEnum>())
                return new DynamicEnumInfo(typeSpec);
            else if (typeSpec.IsGameConfigData)
                return new GameConfigDataInfo(typeSpec);
            else if (typeSpec.Type.IsGenericTypeOf(typeof(GameConfigDataContent<>)))
            {
                MetaSerializableType configDataContentTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeSpec.Type.GetGenericArguments()[0]);
                return new GameConfigDataContentInfo(typeSpec, configDataContentTypeSpec);
            }
            else if (typeSpec.IsMetaRef)
                return new MetaRefInfo(typeSpec);
            else if (typeSpec.IsObject)
            {
                if (typeSpec.Type.IsAbstract)
                    return new AbstractClassOrInterfaceInfo(typeSpec);
                else if (typeSpec.Type.IsClass)
                    return new ConcreteClassInfo(typeSpec);
                else if (typeSpec.IsSystemNullable)
                    return new NullableStructInfo(typeSpec);
                else
                    return new StructInfo(typeSpec);
            }
            else
                throw new InvalidOperationException($"Unknown type: {typeSpec.Name}");
        }

        static void AppendSerializeTypeFunc(IndentedStringBuilder sb, ITypeInfo typeInfo, bool useTrampolines, bool spaceEnsured)
        {
            const string ValueParamName = "inValue";

            sb.AppendLine();
            sb.AppendLine(SerializationMethodSignature(typeInfo, ValueParamName, spaceEnsured));
            sb.Indent("{");

            if (typeInfo.WriteToDebugStreamAtSerializationStart && sb.DebugEnabled)
            {
                sb.AppendDebugLine($"context.DebugStream?.AppendLine($\"@{{writer.Offset}} write type {typeInfo.Type.ToNamespaceQualifiedTypeString()}\");");
                sb.AppendDebugLine();
            }
            typeInfo.AppendSerializationCode(sb, ValueParamName, useTrampolines, spaceEnsured);

            sb.Unindent("}");
        }

        static void AppendDeserializeTypeFunc(IndentedStringBuilder sb, ITypeInfo typeInfo, bool useTrampolines)
        {
            const string ValueParamName = "outValue";

            sb.AppendLine();
            sb.AppendLine(DeserializationMethodSignature(typeInfo, ValueParamName));
            sb.Indent("{");

            bool hasAnyConverters = false;
            if (MetaSerializerTypeRegistry.TryGetTypeSpec(typeInfo.Type, out MetaSerializableType typeSpec)
                && typeSpec.DeserializationConverters.Length > 0)
            {
                hasAnyConverters = true;

                sb.AppendLine("switch (wireType)");
                sb.Indent("{");

                foreach ((MetaDeserializationConverter converter, int converterIndex) in typeSpec.DeserializationConverters.ZipWithIndex())
                {
                    sb.AppendLine($"case WireDataType.{converter.AcceptedWireDataType}:");
                    sb.Indent("{");

                    if (converter.SourceDeserialization == MetaDeserializationConverter.SourceDeserializationKind.Normal)
                        sb.AppendLine($"{DeserializationExpression(converter.SourceType, "source", "wireType")};");
                    else if (converter.SourceDeserialization == MetaDeserializationConverter.SourceDeserializationKind.Members)
                    {
                        sb.AppendLine($"{MembersDeserializationExpression(converter.SourceType, "source")};");
                    }
                    else
                        throw new InvalidOperationException($"Unhandled {nameof(MetaDeserializationConverter.SourceDeserializationKind)}: {converter.SourceDeserialization}");

                    sb.AppendLine($"MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof({typeInfo.GlobalNamespaceQualifiedTypeString}));");
                    sb.AppendLine($"MetaDeserializationConverter converter = typeSpec.DeserializationConverters[{converterIndex.ToString(CultureInfo.InvariantCulture)}];");
                    sb.AppendLine($"{ValueParamName} = ({typeInfo.GlobalNamespaceQualifiedTypeString})converter.Convert(source);");
                    sb.AppendLine($"break;");
                    sb.Unindent("}");
                    sb.AppendLine();
                }

                sb.AppendLine("default:");
                sb.Indent("{");
            }

            sb.AppendLine($"ExpectWireType(ref context, reader, typeof({typeInfo.GlobalNamespaceQualifiedTypeString}), wireType, WireDataType.{TaggedWireSerializer.GetWireType(typeInfo.Type)});");
            sb.AppendLine();
            typeInfo.AppendDeserializationCode(sb, ValueParamName, useTrampolines);

            if (hasAnyConverters)
            {
                sb.AppendLine("break;");
                sb.Unindent("}"); // Closes the default case
                sb.Unindent("}"); // Closes the switch
            }

            sb.Unindent("}");
        }

        static void AppendTraverseMetaRefsInTypeFunc(IndentedStringBuilder sb, ITypeInfo typeInfo, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
        {
            const string ValueParamName = "valueRef";

            sb.AppendLine();
            sb.AppendLine(MetaRefTraverseMethodSignature(typeInfo, ValueParamName));
            sb.Indent("{");
            typeInfo.AppendMetaRefTraverseCode(sb, ValueParamName, metaRefContainingTypes, useTrampolines);
            sb.Unindent("}");
        }

        static void AppendSerializeMembersFunc(IndentedStringBuilder sb, IMembersInfo membersInfo, bool useTrampolines, bool spaceEnsured)
        {
            const string ValueParamName = "inValue";

            sb.AppendLine();
            sb.AppendLine(MembersSerializationMethodSignature(membersInfo, ValueParamName, spaceEnsured));
            sb.Indent("{");

            int? maxSerializedBytes = membersInfo.MaxSerializedBytesStatic();
            if (!spaceEnsured && maxSerializedBytes.HasValue && maxSerializedBytes.Value <= MaxSpanSize)
            {
                sb.AppendLine(Invariant($"writer.EnsureSpace({maxSerializedBytes.Value});"));
                sb.AppendLine(MembersSerializationExpression(membersInfo.Type, ValueParamName, true) + ";");
            }
            else
            {
                membersInfo.AppendMembersSerializationCode(sb, ValueParamName, useTrampolines, spaceEnsured);
            }
            sb.Unindent("}");
        }

        static void AppendDeserializeMembersFunc(IndentedStringBuilder sb, IMembersInfo membersInfo, bool useTrampolines)
        {
            const string ValueParamName = "outValue";

            sb.AppendLine();
            sb.AppendLine(MembersDeserializationMethodSignature(membersInfo, ValueParamName));
            sb.Indent("{");
            membersInfo.AppendMembersDeserializationCode(sb, ValueParamName, useTrampolines);
            sb.Unindent("}");
        }

        static void AppendTraverseMetaRefsInTypeMembersFunc(IndentedStringBuilder sb, IMembersInfo membersInfo, OrderedSet<Type> metaRefContainingTypes, bool useTrampolines)
        {
            const string ValueParamName = "valueRef";

            sb.AppendLine();
            sb.AppendLine(MembersMetaRefTraverseMethodSignature(membersInfo.Type, ValueParamName));
            sb.Indent("{");
            membersInfo.AppendMembersMetaRefTraverseCode(sb, ValueParamName, metaRefContainingTypes, useTrampolines);
            sb.Unindent("}");
        }

        static bool IsSerializableByMembers(MetaSerializableType typeSpec)
        {
            if (typeSpec.IsTuple)
                return true;

            if (MetaSerializerTypeScanner.TypeMemberOverrides.ContainsKey(typeSpec.Type))
                return true;

            return typeSpec.IsObject
                && !typeSpec.IsAbstract
                && !typeSpec.IsSystemNullable
                && !typeSpec.IsGameConfigDataContent
                && typeSpec.HasSerializableAttribute;
        }

        static void AppendMembersSerializationDelegates(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate getter trampolines for all members of all known types
            sb.AppendLine();
            sb.AppendLine("// Member getters");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsSerializableByMembers(typeSpec))
                {
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        if ((typeSpec.Type.FindNonPublicComponent() ?? member.Type.FindNonPublicComponent()) is Type nonPrivateComponentType)
                            throw new InvalidOperationException($"Cannot create getter delegate for {typeSpec.Name} field {member.Name}. Reason: {nonPrivateComponentType.Name} is private");

                        string typeName = typeSpec.GlobalNamespaceQualifiedTypeString;
                        string overloadDiscriminator = MethodSuffixForType(typeSpec.Type);
                        string memberTypeName = member.CodeGenInfo.GlobalNamespaceQualifiedTypeString;
                        sb.AppendLine($"internal static GetMemberDelegate<{typeName}, {memberTypeName}> Get_{overloadDiscriminator}_{member.Name} = MemberAccessGenerator.GenerateGetMember<{typeName}, {memberTypeName}>(\"{member.Name}\");");
                    }
                }
            }
        }

        static void AppendMembersDeserializationDelegates(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate setter trampolines for all members of all known types, as well as [MetaOnDeserialized] and [MetaOnMemberDeserializationFailure] method invocation trampolines
            sb.AppendLine();
            sb.AppendLine("// Member setters");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsSerializableByMembers(typeSpec))
                {
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        if ((typeSpec.Type.FindNonPublicComponent() ?? member.Type.FindNonPublicComponent()) is Type nonPrivateComponentType)
                            throw new InvalidOperationException($"Cannot create setter delegate for {typeSpec.Name} field {member.Name}. Reason: {nonPrivateComponentType.Name} is private");

                        string typeName = typeSpec.GlobalNamespaceQualifiedTypeString;
                        string overloadDiscriminator = MethodSuffixForType(typeSpec.Type);
                        string memberTypeName = member.CodeGenInfo.GlobalNamespaceQualifiedTypeString;
                        sb.AppendLine($"internal static SetMemberDelegate<{typeName}, {memberTypeName}> Set_{overloadDiscriminator}_{member.Name} = MemberAccessGenerator.GenerateSetMember<{typeName}, {memberTypeName}>(\"{member.Name}\");");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("// [MetaOnDeserialized] method invokers");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsSerializableByMembers(typeSpec))
                {
                    foreach (MethodInfo onDeserializedMethod in typeSpec.OnDeserializedMethods)
                    {
                        string typeName = typeSpec.GlobalNamespaceQualifiedTypeString;
                        string overloadDiscriminator = MethodSuffixForType(typeSpec.Type);

                        if (onDeserializedMethod.GetParameters().Any())
                            sb.AppendLine($"internal static InvokeOnDeserializedMethodWithParamsDelegate<{typeName}> InvokeOnDeserializedWithParams_{overloadDiscriminator}_{onDeserializedMethod.Name} = MemberAccessGenerator.GenerateInvokeOnDeserializedMethodWithParams<{typeName}>(\"{onDeserializedMethod.Name}\");");
                        else
                            sb.AppendLine($"internal static InvokeOnDeserializedMethodDelegate<{typeName}> InvokeOnDeserialized_{overloadDiscriminator}_{onDeserializedMethod.Name} = MemberAccessGenerator.GenerateInvokeOnDeserializedMethod<{typeName}>(\"{onDeserializedMethod.Name}\");");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("// [MetaOnMemberDeserializationFailure] method invokers");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsSerializableByMembers(typeSpec))
                {
                    foreach (MetaSerializableMember memberSpec in typeSpec.Members)
                    {
                        if (memberSpec.OnDeserializationFailureMethod == null)
                            continue;

                        MethodInfo method = memberSpec.OnDeserializationFailureMethod;
                        string typeName = typeSpec.GlobalNamespaceQualifiedTypeString;
                        string overloadDiscriminator = MethodSuffixForType(typeSpec.Type);
                        string resultTypeStr = method.ReturnType.ToGlobalNamespaceQualifiedTypeString();
                        sb.AppendLine($"internal static InvokeOnMemberDeserializationFailureMethodDelegate<{resultTypeStr}> InvokeOnMemberDeserializationFailure_{overloadDiscriminator}_{memberSpec.Name} = MemberAccessGenerator.GenerateInvokeOnMemberDeserializationFailureMethod<{resultTypeStr}>(typeof({typeName}), \"{memberSpec.Name}\");");
                    }
                }
            }
        }

        static void AppendCreateInstanceDelegates(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate instance creation trampolines for concrete reference types.
            // Not needed for value types; those are always publicly default-constructible.
            // MetaRefs do not have a constructor and need to be resolved using its key type.
            sb.AppendLine();
            sb.AppendLine("// Object instance creators");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (!typeSpec.IsObject)
                    continue;
                if (typeSpec.Type.IsValueType && !typeSpec.UsesConstructorDeserialization)
                    continue;
                if (typeSpec.IsAbstract)
                    continue;
                if (typeSpec.IsMetaRef)
                    continue;

                string typeName = typeSpec.GlobalNamespaceQualifiedTypeString;
                string typeSuffix = MethodSuffixForType(typeSpec.Type);
                if (typeSpec.UsesConstructorDeserialization)
                {
                    ConstructorInfo     bestMatchingConstructor = typeSpec.DeserializationConstructor;
                    IEnumerable<string> parameterTypes          = bestMatchingConstructor.GetParameters().Select(x => x.ParameterType).Select(x => $"typeof({x.ToGlobalNamespaceQualifiedTypeString()})");
                    sb.AppendLine($"internal static CreateInstanceWithParametersDelegate<{typeName}> CreateInstance_{typeSuffix} = MemberAccessGenerator.GenerateCreateInstanceWithParameters<{typeName}>({string.Join(", ", parameterTypes)});");
                }
                else
                    sb.AppendLine($"internal static CreateInstanceDelegate<{typeName}> CreateInstance_{typeSuffix} = MemberAccessGenerator.GenerateCreateInstance<{typeName}>();");
            }
        }

        static void AppendSerializeObject(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate dynamic-typed SerializeObject(context, writer, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static void SerializeObject(ref MetaSerializationContext context, ref SpanWriter writer, Type type, object obj)");
            sb.Indent("{");
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                sb.AppendLine($"if (type == typeof({typeSpec.GlobalNamespaceQualifiedTypeString}))");
                sb.Indent("{");
                sb.AppendLine($"WriteWireType(ref writer, WireDataType.{typeSpec.WireType});");
                sb.AppendLine($"{SerializationExpression(typeSpec.Type, $"({typeSpec.GlobalNamespaceQualifiedTypeString})obj", false)};");
                sb.AppendLine("return;");
                sb.Unindent("}");
            }

            sb.AppendLine("throw new InvalidOperationException($\"SerializeObject(): Unsupported type {type.ToGenericTypeString()}\");");
            sb.Unindent("}");
        }

        static void AppendDeserializeObject(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate dynamic-typed DeserializeObject(context, writer, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static object DeserializeObject(ref MetaSerializationContext context, IOReader reader, Type type)");
            sb.Indent("{");
            sb.AppendLine("WireDataType wireType = ReadWireType(reader);");
            sb.AppendLine();
            foreach (MetaSerializableType typeSpec in allTypes)
                sb.AppendLine($"if (type == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ {DeserializationExpression(typeSpec.Type, "obj", "wireType")}; return obj; }}");
            sb.AppendLine("throw new InvalidOperationException($\"DeserializeObject(): Unsupported type {type.ToGenericTypeString()}\");");
            sb.Unindent("}");
        }

        static void AppendTraverseMetaRefsInObject(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes, OrderedSet<Type> metaRefContainingTypes)
        {
            // Generate dynamic-typed TraverseMetaRefsInObject(context, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static void TraverseMetaRefsInObject(ref MetaSerializationContext context, Type type, ref object obj)");
            sb.Indent("{");
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (metaRefContainingTypes.Contains(typeSpec.Type))
                    sb.AppendLine($"if (type == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ var typedObj = ({typeSpec.GlobalNamespaceQualifiedTypeString})obj; {MetaRefTraverseExpression(typeSpec.Type, "typedObj")}; if (context.MetaRefTraversal.IsMutatingOperation) {{ obj = typedObj; }} return; }}");
                else
                    sb.AppendLine($"if (type == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ /* This type does not contain MetaRefs */ return; }}");
            }
            sb.AppendLine("throw new InvalidOperationException($\"TraverseMetaRefsInObject(): Unsupported type {type.ToGenericTypeString()}\");");
            sb.Unindent("}");
        }

        static bool IsTableSerializable(MetaSerializableType typeSpec)
        {
            // Only generate table-serialization for concrete objects implementing IGameConfigData (and must also be members-serializable)
            return typeSpec.IsConcreteObject
                && typeSpec.Type.ImplementsInterface<IGameConfigData>()
                && IsSerializableByMembers(typeSpec);
        }

        static void AppendSerializeTable(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Generate dynamic-typed SerializeTable(context, writer, List<T> items)
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec))
                {
                    sb.AppendLine();
                    sb.AppendLine(TableSerializationMethodSignature(typeSpec, "items"));
                    sb.Indent("{");
                    if (sb.DebugEnabled)
                        sb.AppendDebugLine($"context.DebugStream?.AppendLine($\"@{{writer.Offset}} write type Table<{typeSpec.Name}>\");");
                    sb.AppendLine("if (items != null)");
                    sb.AppendLine("{");

                    sb.AppendLine("    if (items.Count > context.MemberContext.MaxCollectionSize)");
                    sb.AppendLine("        throw new InvalidOperationException($\"Invalid value collection size {items.Count} (maximum allowed is {context.MemberContext.MaxCollectionSize})\");");

                    sb.AppendLine("    writer.WriteVarInt(items.Count);");
                    sb.AppendLine($"    foreach ({typeSpec.GlobalNamespaceQualifiedTypeString} item in items)");
                    sb.AppendLine($"        {MembersSerializationExpression(typeSpec.Type, "item", false)};");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("    writer.WriteVarIntConst(VarIntConst.MinusOne);");
                    sb.Unindent("}");
                }
            }

            // Generate dynamic-typed SerializeTable(context, writer, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static void SerializeTable(ref MetaSerializationContext context, ref SpanWriter writer, Type itemType, object obj, int maxCollectionSizeOverride)");
            sb.Indent("{");
            sb.AppendLine("context.MemberContext.UpdateCollectionSize(maxCollectionSizeOverride);");
            sb.AppendLine($"WriteWireType(ref writer, WireDataType.ObjectTable);");
            sb.AppendLine();
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec))
                    sb.AppendLine($"if (itemType == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ {TableSerializationExpression(typeSpec.Type, $"(IReadOnlyList<{typeSpec.GlobalNamespaceQualifiedTypeString}>)obj")}; return; }}");
            }
            sb.AppendLine("throw new InvalidOperationException($\"SerializeTable(): Unsupported table item type {itemType.ToGenericTypeString()} -- the type must be a concrete class, must implement IGameConfigData<>, and have [MetaSerializable] attribute\");");
            sb.Unindent("}");
        }

        static void AppendDeserializeTable(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes, bool useTrampolines)
        {
            // Generate dynamic-typed DeserializeTable(context, writer, List<T> items)
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec))
                {
                    sb.AppendLine();
                    sb.AppendLine(TableDeserializationMethodSignature(typeSpec, "items"));
                    sb.Indent("{");
                    sb.AppendLine("int count = reader.ReadVarInt();");
                    sb.AppendLine("if (count < -1 || count > context.MemberContext.MaxCollectionSize)");
                    sb.AppendLine("    throw new InvalidOperationException($\"Invalid value collection size {count} (maximum allowed is {context.MemberContext.MaxCollectionSize})\");");
                    sb.AppendLine("else if (count == -1)");
                    sb.AppendLine("    items = null;");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine($"    items = new List<{typeSpec.GlobalNamespaceQualifiedTypeString}>(count);");
                    sb.AppendLine("    for (int ndx = 0; ndx < count; ndx++)");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        {MembersDeserializationExpression(typeSpec.Type, "item")};");
                    sb.AppendLine("        items.Add(item);");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                    sb.Unindent("}");
                }
            }

            // Generate dynamic-typed DeserializeTable(context, writer, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static object DeserializeTable(ref MetaSerializationContext context, IOReader reader, Type itemType, int maxCollectionSizeOverride)");
            sb.Indent("{");
            sb.AppendLine("WireDataType wireType = ReadWireType(reader);");
            sb.AppendLine("ExpectWireType(ref context, reader, itemType, wireType, WireDataType.ObjectTable);");
            sb.AppendLine("context.MemberContext.UpdateCollectionSize(maxCollectionSizeOverride);");
            sb.AppendLine();
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec))
                {
                    sb.AppendLine($"if (itemType == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ {TableDeserializationExpression(typeSpec.Type, "obj")}; return obj; }}");
                }
            }
            sb.AppendLine("throw new InvalidOperationException($\"DeserializeTable(): Unsupported table item type {itemType.ToGenericTypeString()} -- the type must be a concrete class, must implement IGameConfigData<>, and have [MetaSerializable] attribute\");");
            sb.Unindent("}");
        }

        static void AppendTraverseMetaRefsInTable(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes, OrderedSet<Type> metaRefByMembersContainingTypes)
        {
            // Generate internal statically-typed table MetaRef traversing methods
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec) && metaRefByMembersContainingTypes.Contains(typeSpec.Type))
                {
                    sb.AppendLine();
                    sb.AppendLine(TableMetaRefsTraverseMethodSignature(typeSpec.Type, "items"));
                    sb.Indent("{");
                    sb.AppendLine("if (items != null)");
                    sb.Indent("{");
                    sb.AppendLine("int count = items.Count;");
                    sb.AppendLine("for (int ndx = 0; ndx < count; ndx++)");
                    sb.Indent("{");
                    sb.AppendLine($"var elem = items[ndx];");
                    sb.AppendLine($"context.MetaRefTraversal.VisitTableTopLevelConfigItem?.Invoke(ref context, elem);");
                    sb.AppendLine($"{MembersMetaRefTraverseExpression(typeSpec.Type, "elem")};");
                    sb.AppendLine($"if (context.MetaRefTraversal.IsMutatingOperation)");
                    sb.Indent("{");
                    sb.AppendLine($"items[ndx] = elem;");
                    sb.Unindent("}");
                    sb.Unindent("}");
                    sb.Unindent("}");
                    sb.Unindent("}");
                }
            }

            // Generate dynamic-typed TraverseMetaRefsInTable(context, writer, Type type, object obj)
            sb.AppendLine();
            sb.AppendLine("public static void TraverseMetaRefsInTable(ref MetaSerializationContext context, Type itemType, object obj)");
            sb.Indent("{");
            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsTableSerializable(typeSpec))
                {
                    if (metaRefByMembersContainingTypes.Contains(typeSpec.Type))
                        sb.AppendLine($"if (itemType == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ {TableMetaRefsTraverseExpression(typeSpec.Type, $"(List<{typeSpec.GlobalNamespaceQualifiedTypeString}>)obj")}; return; }}");
                    else
                        sb.AppendLine($"if (itemType == typeof({typeSpec.GlobalNamespaceQualifiedTypeString})) {{ /* This type's members do not contain MetaRefs */ return; }}");
                }
            }
            sb.AppendLine("throw new InvalidOperationException($\"TraverseMetaRefsInTable(): Unsupported table item type {itemType.ToGenericTypeString()} -- the type must be a concrete class, must implement IGameConfigData<>, and have [MetaSerializable] attribute\");");
            sb.Unindent("}");
        }

        static void AppendILCompatibilityInfoComments(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes)
        {
            // Add some comments with info that is relevant for generated IL but is not visible
            // in the actual generated code (other than in these generated comments).
            // Without these comments, the serializer cache would erroneously keep the
            // old serializer dll when the only changes to the serializable types have been
            // changes that do not change the code proper.
            //
            // Such relevant information is:
            // - whether a member is a field or a property (accesses in c# code are identical,
            //   but generated IL differs)

            sb.AppendLine();
            sb.AppendLine("// Additional IL compatibility info for serializer caching");
            sb.AppendLine();

            foreach (MetaSerializableType typeSpec in allTypes)
            {
                if (IsSerializableByMembers(typeSpec))
                {
                    sb.AppendLine($"// type {typeSpec.GlobalNamespaceQualifiedTypeString}");
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        string memberKind = member.MemberInfo.MemberType.ToString();
                        sb.AppendLine($"// {memberKind} {member.Name}");
                    }
                }
            }
        }

        static string CommaSuffixForListElementMaybe(int index, int listLength)
        {
            return index < listLength - 1 ? "," : "";
        }

        static string QuoteString(string s)
        {
            return s == null ? null : $"\"{s}\"";
        }

        static void AppendRuntimeTypeInfo(IndentedStringBuilder sb, IEnumerable<MetaSerializableType> allTypes, uint typeHash)
        {
            sb.AppendLine();
            sb.AppendLine("// Built-in MetaSerializable type info. Used by RuntimeTypeInfoProvider for populating the MetaSerializableTypeRegistry.");
            sb.AppendLine("// The type info construction is split into multiple methods because WASM build duration seems scale badly with respect to method size.");
            sb.AppendLine();

            // The creation of the type infos is split into multiple methods, because WASM build
            // gets very slow if there is just one very long method. Anecdotally, a build time of
            // 30 minutes was decreased to 10 minutes by splitting into multiple methods
            // (that was an incremental build after only changing a MetaMember tag id).

            const int numTypesPerMethod = 20;
            int numMethods = (allTypes.Count() + numTypesPerMethod - 1) / numTypesPerMethod; // divide, round up

            // Write sub-methods

            IEnumerable<MetaSerializableType> typesCursor = allTypes;
            for (int methodNdx = 0; methodNdx < numMethods; methodNdx++)
            {
                sb.AppendLine(Invariant($"static void GetTypeInfoSubMethod{methodNdx}(Dictionary<Type, MetaSerializableType> types)"));
                sb.Indent("{");

                foreach (MetaSerializableType typeSpec in typesCursor.Take(numTypesPerMethod))
                {
                    sb.Indent($"types.Add(typeof({typeSpec.GlobalNamespaceQualifiedTypeString}), MetaSerializableType.CreateFromBuiltinData(");
                    sb.AppendLine($"typeof({typeSpec.GlobalNamespaceQualifiedTypeString}),");
                    sb.AppendLine($"{(typeSpec.IsPublic ? "true" : "false")},");
                    sb.AppendLine($"{(typeSpec.UsesImplicitMembers ? "true" : "false")},");
                    sb.AppendLine($"(WireDataType){(uint)typeSpec.WireType},");
                    sb.AppendLine($"{(typeSpec.HasSerializableAttribute ? "true" : "false")},");
                    sb.AppendLine(Invariant($"{typeSpec.TypeCode},"));
                    if (typeSpec.Members == null)
                    {
                        sb.AppendLine("null,");
                    }
                    else
                    {
                        sb.AppendLine($"new MetaSerializableType.MemberBuiltinData[]");
                        sb.Indent("{");
                        foreach ((MetaSerializableMember member, int index) in typeSpec.Members.ZipWithIndex())
                        {
                            sb.AppendLine(Invariant($"new MetaSerializableType.MemberBuiltinData({member.TagId}, \"{member.Name}\", ") +
                                          $"(MetaMemberFlags){(uint)member.Flags}, typeof({member.MemberInfo.DeclaringType.ToGlobalNamespaceQualifiedTypeString()}), " +
                                          $"{QuoteString(member.OnDeserializationFailureMethodName) ?? "null"}){CommaSuffixForListElementMaybe(index, typeSpec.Members.Count)}");
                        }
                        sb.Unindent("},");
                    }
                    if (typeSpec.DerivedTypes == null)
                    {
                        sb.AppendLine("null,");
                    }
                    else
                    {
                        sb.AppendLine($"new ValueTuple<int, Type>[]");
                        sb.Indent("{");
                        int idx = 0;
                        foreach ((int typeCode, Type type) in typeSpec.DerivedTypes)
                        {
                            sb.AppendLine(Invariant($"({typeCode}, typeof({type.ToGlobalNamespaceQualifiedTypeString()})){CommaSuffixForListElementMaybe(idx++, typeSpec.DerivedTypes.Count)}"));
                        }
                        sb.Unindent("},");
                    }
                    if (typeSpec.OnDeserializedMethods == null)
                    {
                        sb.AppendLine("null,");
                    }
                    else
                    {
                        sb.AppendLine($"new ValueTuple<Type, string>[]");
                        sb.Indent("{");
                        int idx = 0;
                        foreach (MethodInfo m in typeSpec.OnDeserializedMethods)
                        {
                            sb.AppendLine($"(typeof({m.DeclaringType.ToGlobalNamespaceQualifiedTypeString()}), \"{m.Name}\"){CommaSuffixForListElementMaybe(idx++, typeSpec.OnDeserializedMethods.Count)}");
                        }
                        sb.Unindent("},");
                    }
                    sb.AppendLine(typeSpec.ConfigNullSentinelKey == null ? "false," : "true,");
                    sb.AppendLine(typeSpec.DeserializationConverters == null || typeSpec.DeserializationConverters.Length == 0 ? "false" : "true");
                    sb.Unindent("));");
                }
                typesCursor = typesCursor.Skip(numTypesPerMethod);

                sb.Unindent("}");
            }
            MetaDebug.Assert(typesCursor.Count() == 0, "Did not generate GetTypeInfo code for all types");

            // Write top-level method which calls the sub-methods (and returns type hash)

            sb.AppendLine("public static uint GetTypeInfo(Dictionary<Type, MetaSerializableType> types)");
            sb.Indent("{");

            for (int methodNdx = 0; methodNdx < numMethods; methodNdx++)
                sb.AppendLine(Invariant($"GetTypeInfoSubMethod{methodNdx}(types);"));

            sb.AppendLine($"return {typeHash};");
            sb.Unindent("}");
        }

        public readonly struct GeneratedSource
        {
            public readonly string Source;
            public readonly OrderedSet<Assembly> ReferencedAssemblies;

            public GeneratedSource(string source, OrderedSet<Assembly> referencedAssemblies)
            {
                Source = source;
                ReferencedAssemblies = referencedAssemblies;
            }
        }

        public static GeneratedSource GenerateSerializerCode(IEnumerable<MetaSerializableType> allTypes, uint typeHash, bool useMemberAccessTrampolines, bool generateRuntimeTypeInfo)
        {
            OrderedSet<Assembly> referencedAssemblies = new OrderedSet<Assembly>();

            // Cloud builds have ActorRefs as a builtin
            #if NETCOREAPP
            referencedAssemblies.Add(typeof(Akka.Actor.IActorRef).Assembly);
            #endif

            try
            {
                s_typeInfoCache = CreateTypeInfos(allTypes);

                foreach (MetaSerializableType t in allTypes)
                    referencedAssemblies.Add(t.Type.Assembly);

                // Always add a dependency to Metaplay.Cloud
                referencedAssemblies.Add(typeof(MetaplayCore).Assembly);

                string source = GenerateSerializerCodeImpl(allTypes, typeHash, useMemberAccessTrampolines, generateRuntimeTypeInfo, referencedAssemblies);
                return new GeneratedSource(source, referencedAssemblies);
            }
            finally
            {
                // Cache lifetime is the duration of the GenerateSerializerCodeImpl call, always clear the reference so that allocations can be freed.
                s_typeInfoCache = null;
            }
        }

        internal static Dictionary<Type, ITypeInfo> CreateTypeInfos(IEnumerable<MetaSerializableType> allTypes)
        {
            Dictionary<Type, ITypeInfo> typeInfos = GetBuiltinTypeInfos().ToDictionary(x => x.Type);
            foreach (MetaSerializableType t in allTypes)
                typeInfos.Add(t.Type, GetSerializableTypeInfo(t));

            foreach ((Type key, ITypeInfo value) in s_additionalTypeInfos)
                typeInfos.Add(key, value);

            return typeInfos;
        }

        internal static IEnumerable<IMembersInfo> CreateMembersInfos(IEnumerable<MetaSerializableType> allTypes)
        {
            return
                allTypes
                .Where(IsSerializableByMembers)
                .Select(typeSpec => (IMembersInfo)new ConcreteClassOrStructMembersInfo(typeSpec));
        }

        static string GenerateSerializerCodeImpl(IEnumerable<MetaSerializableType> allTypes, uint typeHash, bool useMemberAccessTrampolines, bool generateRuntimeTypeInfo, IEnumerable<Assembly> assemblies)
        {
            IndentedStringBuilder sb = new IndentedStringBuilder(outputDebugCode: false);

            // Collect all namespaces used in the types
            HashSet<string> namespaces = new HashSet<string>();
            namespaces.Add("System");
            namespaces.Add("System.Collections.Generic");
            namespaces.Add("Metaplay.Core.IO");
            namespaces.Add("Metaplay.Core.Serialization"); // for MemberAccessGenerator
            namespaces.Add("Metaplay.Core.Model"); // for MetaMemberFlags
            namespaces.Add("Metaplay.Core.Config"); // for IGameConfigData
            namespaces.Add("Metaplay.Core"); // for ToGenericTypeString

            //foreach (MetaSerializableType typeSpec in allTypes)
            //{
            //    if (!string.IsNullOrEmpty(typeSpec.Type.Namespace))
            //        namespaces.Add(typeSpec.Type.Namespace);
            //}

            sb.AppendLine("// MACHINE-GENERATED CODE, DO NOT MODIFY");
            sb.AppendLine();

            foreach (string ns in namespaces.OrderBy(v => v, StringComparer.InvariantCulture))
                sb.AppendLine($"using {ns};");
            sb.AppendLine();

            List<string> assemblyNames = new List<string>();
            foreach (Assembly assembly in assemblies)
                assemblyNames.Add(assembly.GetName().Name);
            assemblyNames.Sort(StringComparer.InvariantCulture);
            foreach (string assemblyName in assemblyNames)
                sb.AppendLine($"[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo(\"{assemblyName}\")]");
            sb.AppendLine();

            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]");
            sb.AppendLine("    internal sealed class IgnoresAccessChecksToAttribute : Attribute");
            sb.AppendLine("    {");
            sb.AppendLine("        public string AssemblyName { get; }");
            sb.AppendLine("        public IgnoresAccessChecksToAttribute(string assemblyName) => AssemblyName = assemblyName;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("namespace Metaplay.Generated");
            sb.Indent("{");

            sb.AppendLine("public static class TypeSerializer");
            sb.Indent("{");

            AppendHelpers(sb);

            IEnumerable<ITypeInfo> typeInfos = s_typeInfoCache.Values;

            IEnumerable<IMembersInfo> membersInfos = CreateMembersInfos(allTypes);

            GetMetaRefContainingTypes(
                typeInfos,
                out OrderedSet<Type> metaRefContainingTypes,
                out OrderedSet<Type> metaRefByMembersContainingTypes);

            sb.AppendLine();
            sb.AppendLine("// Type serializers, deserializers, and MetaRef traversers");
            foreach (ITypeInfo info in typeInfos)
            {
                AppendSerializeTypeFunc(sb, info, useMemberAccessTrampolines, false);
                int? maxSerializedBytes = info.MaxSerializedBytesStatic();
                if (maxSerializedBytes.HasValue && maxSerializedBytes <= MaxSpanSize)
                    AppendSerializeTypeFunc(sb, info, useMemberAccessTrampolines, true);
                AppendDeserializeTypeFunc(sb, info, useMemberAccessTrampolines);

                if (metaRefContainingTypes.Contains(info.Type))
                    AppendTraverseMetaRefsInTypeFunc(sb, info, metaRefContainingTypes, useMemberAccessTrampolines);
            }

            sb.AppendLine();
            sb.AppendLine("// Members serializers, deserializers, and MetaRef traversers");
            foreach (IMembersInfo info in membersInfos)
            {
                AppendSerializeMembersFunc(sb, info, useMemberAccessTrampolines, false);
                int? maxSerializedBytes = info.MaxSerializedBytesStatic();
                if (maxSerializedBytes.HasValue && maxSerializedBytes <= MaxSpanSize)
                    AppendSerializeMembersFunc(sb, info, useMemberAccessTrampolines, true);
                AppendDeserializeMembersFunc(sb, info, useMemberAccessTrampolines);

                if (metaRefByMembersContainingTypes.Contains(info.Type))
                    AppendTraverseMetaRefsInTypeMembersFunc(sb, info, metaRefContainingTypes, useMemberAccessTrampolines);
            }

            sb.AppendLine();
            sb.AppendLine("// Table serializers");
            AppendSerializeTable(sb, allTypes);

            sb.AppendLine();
            sb.AppendLine("// Table deserializers");
            AppendDeserializeTable(sb, allTypes, useMemberAccessTrampolines);

            sb.AppendLine();
            sb.AppendLine("// Table MetaRef traversers");
            AppendTraverseMetaRefsInTable(sb, allTypes, metaRefByMembersContainingTypes);

            sb.AppendLine();
            sb.AppendLine("// Top-level object serializer");
            AppendSerializeObject(sb, allTypes);

            sb.AppendLine();
            sb.AppendLine("// Top-level object deserializer");
            AppendDeserializeObject(sb, allTypes);

            sb.AppendLine();
            sb.AppendLine("// Top-level object MetaRef traverser");
            AppendTraverseMetaRefsInObject(sb, allTypes, metaRefContainingTypes);

            AppendILCompatibilityInfoComments(sb, allTypes);

            if (generateRuntimeTypeInfo)
                AppendRuntimeTypeInfo(sb, allTypes, typeHash);

            sb.Unindent("}"); // class TypeSerializer

            if (useMemberAccessTrampolines)
            {
                sb.AppendLine();
                sb.AppendLine("static class Trampolines");
                sb.Indent("{");

                AppendMembersSerializationDelegates(sb, allTypes);
                AppendMembersDeserializationDelegates(sb, allTypes);
                AppendCreateInstanceDelegates(sb, allTypes);

                sb.Unindent("}");
            }

            sb.Unindent("}"); // namespace Metaplay.Generated

            return sb.ToString();
        }

        /// <summary>
        /// Get types that (transitively) contain MetaRefs, i.e. types for which we need to
        /// generate MetaRef traversing code.
        /// </summary>
        /// <remarks>
        /// IGameConfigData types have two different serialization behaviors, used in
        /// different scenarios.
        /// "Normal" behavior: IGameConfigData is serialized by-key.
        /// "Config table" behavior: IGameConfigData is serialized by-members.
        /// In the "normal" case, an IGameConfigData never contains MetaRefs.
        /// However, in the "config table" behavior it might. This behavior is relevant
        /// for us since we want to traverse MetaRefs within config data.
        ///
        /// For this reason, this method produces two sets of types:
        /// Those that (transitively) contain MetaRefs when serialized with the "normal"
        /// behavior, and those that (transitively) contain MetaRefs when serialized by-members.
        /// The "by-members" set is a superset of the "normal" set. The "by-members" set,
        /// in addition to containing the same types as in the "normal" set, contains also
        /// the IGameConfigData types that have at least one member in the "normal" set.
        /// </remarks>
        /// <param name="metaRefContainingTypes">
        /// The types that transitively contain MetaRefs, when serialized
        /// with the normal serialization behavior.
        /// </param>
        /// <param name="metaRefByMembersContainingTypes">
        /// The types that transitively contain MetaRefs, when serialized
        /// with the "by-members" serialization behavior.
        /// This differs from <paramref name="metaRefContainingTypes"/> only
        /// with respect to IGameConfigData types.
        /// </param>
        internal static void GetMetaRefContainingTypes(IEnumerable<ITypeInfo> typeInfos, out OrderedSet<Type> metaRefContainingTypesOut, out OrderedSet<Type> metaRefByMembersContainingTypesOut)
        {
            IEnumerable<Type> types = typeInfos.Select(t => t.Type);

            // Construct mapping  T -> types_directly_contained_in_T
            OrderedDictionary<Type, List<Type>> typeDirectlyContains = new OrderedDictionary<Type, List<Type>>();
            foreach (ITypeInfo typeInfo in typeInfos)
                typeDirectlyContains.Add(typeInfo.Type, typeInfo.DirectlyContainedTypes.ToList());

            // Construct the inverse mapping of typeDirectlyContains:  T -> types_that_T_is_directly_contained_in
            OrderedDictionary<Type, List<Type>> typeIsDirectlyContainedIn = new OrderedDictionary<Type, List<Type>>();
            foreach (Type type in types)
                typeIsDirectlyContainedIn.Add(type, new List<Type>());
            foreach ((Type containing, List<Type> containeds) in typeDirectlyContains)
            {
                foreach (Type contained in containeds)
                    typeIsDirectlyContainedIn[contained].Add(containing);
            }

            // Get all MetaRef types
            IEnumerable<Type> metaRefTypes = types.Where(type => type.IsGenericTypeOf(typeof(MetaRef<>)));

            // Compute which types transitively contain MetaRefs
            OrderedSet<Type> metaRefContainingTypes = ComputeReachableNodes(roots: metaRefTypes, edges: typeIsDirectlyContainedIn);

            // Compute the "by-members" set: in addition to metaRefContainingTypes, this
            // contains the IGameConfigData types whose members contain MetaRefs.
            IEnumerable<Type> configDataTypes = types.Where(type => !type.IsAbstract && type.ImplementsGenericInterface(typeof(IGameConfigData<>)));
            IEnumerable<Type> metaRefByMembersContainingConfigDataTypes =
                configDataTypes
                .Where(type =>
                {
                    MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(type);
                    IEnumerable<Type> configDataTypeMembers = new ConcreteClassOrStructMembersInfo(typeSpec).DirectlyContainedTypes;
                    return configDataTypeMembers.Any(metaRefContainingTypes.Contains);
                });
            IEnumerable<Type> metaRefByMembersContainingTypes =
                metaRefContainingTypes
                .Concat(metaRefByMembersContainingConfigDataTypes)
                .ToOrderedSet();

            // Outputs

            metaRefContainingTypesOut           = metaRefContainingTypes;
            metaRefByMembersContainingTypesOut  = metaRefByMembersContainingTypes.ToOrderedSet();
        }

        static OrderedSet<TNode> ComputeReachableNodes<TNode>(IEnumerable<TNode> roots, OrderedDictionary<TNode, List<TNode>> edges)
        {
            OrderedSet<TNode> reachable = new OrderedSet<TNode>();
            foreach (TNode root in roots)
                FillReachableNodes(reachable, root, edges);
            return reachable;
        }

        static void FillReachableNodes<TNode>(OrderedSet<TNode> reachable, TNode current, OrderedDictionary<TNode, List<TNode>> edges)
        {
            if (reachable.Contains(current))
                return;
            reachable.Add(current);
            foreach (TNode neighbor in edges[current])
                FillReachableNodes(reachable, neighbor, edges);
        }
    }
}
