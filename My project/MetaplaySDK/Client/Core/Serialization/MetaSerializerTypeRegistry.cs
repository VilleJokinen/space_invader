// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_EDITOR || NETCOREAPP
#   define ENABLE_SERIALIZER_GENERATION
#endif

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Profiling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Information of a serialized Property or Field in [MetaSerializable] type. The members may
    /// be marked with [MetaMember] attribute or they may be implicitly serializable.
    /// </summary>
    public class MetaSerializableMember
    {
        public readonly int                          TagId;
        public readonly MetaMemberFlags              Flags;
        public readonly MemberInfo                   MemberInfo;
        public          MethodInfo                   OnDeserializationFailureMethod;
        public readonly SerializerGenerationOnlyInfo CodeGenInfo;

        public string Name => MemberInfo.Name;
        public Type Type => MemberInfo.GetDataMemberType();
        public Action<object, object> SetValue => MemberInfo.GetDataMemberSetValueOnDeclaringType();
        public Func<object, object> GetValue => MemberInfo.GetDataMemberGetValueOnDeclaringType();
        public Type DeclaringType => MemberInfo.DeclaringType;
        public string OnDeserializationFailureMethodName => OnDeserializationFailureMethod?.Name;

        public struct SerializerGenerationOnlyInfo
        {
#if ENABLE_SERIALIZER_GENERATION
            public string GlobalNamespaceQualifiedTypeString { get; }
            public int?   AddedInLogicVersion;
            public int?   RemovedInLogicVersion;
            public int    MaxCollectionSize;

            public SerializerGenerationOnlyInfo(string globalNamespaceQualifiedTypeString, int? addedInLogicVersion, int? removedInLogicVersion, int maxCollectionSize)
            {
                GlobalNamespaceQualifiedTypeString = globalNamespaceQualifiedTypeString;
                AddedInLogicVersion                = addedInLogicVersion;
                RemovedInLogicVersion              = removedInLogicVersion;
                MaxCollectionSize                  = maxCollectionSize;
            }
#endif
        }

        public MetaSerializableMember(
            int tagId,
            MetaMemberFlags flags,
            MemberInfo memberInfo,
            SerializerGenerationOnlyInfo serializerGenerationOnlyInfo)
        {
            TagId             = tagId;
            Flags             = flags;
            MemberInfo        = memberInfo;
            CodeGenInfo       = serializerGenerationOnlyInfo;
        }

        public override string ToString()
        {
            return Invariant($"MetaSerializableMember{{ tagId={TagId}, flags={Flags}, type={Type.Name}, name={Name} }}");
        }
    }

    /// <summary>
    /// Information of a [MetaSerializable] type.
    /// </summary>
    public class MetaSerializableType
    {
        public Type Type { get; }

        /// <summary>
        /// Name of type (using ToGenericTypeString())
        /// </summary>
        public string                                     Name                           { get; }
        public bool                                       UsesImplicitMembers            { get; set; }
        public bool                                       UsesConstructorDeserialization { get; set; }
        public bool                                       IsPublic                       { get; set; }
        public WireDataType                               WireType                       { get; }
        public bool                                       HasSerializableAttribute       { get; } // \todo: check if this is still actually needed
        public int                                        TypeCode                       { get; set; }
        public List<MetaSerializableMember>               Members                        { get; set; } // null if not a concrete object
        public Dictionary<int, MetaSerializableMember>    MemberByTagId                  { get; set; } // null if not a concrete object
        public Dictionary<string, MetaSerializableMember> MemberByName                   { get; set; } // null if not a concrete object

        /// <summary>
        /// Constructor used for deserialization, this is only set if <see cref="UsesConstructorDeserialization"/> is true.
        /// </summary>
        public ConstructorInfo                            DeserializationConstructor     { get; set; }

        /// <summary>
        /// The dictionary of all derived classes where key is the concrete type's typecode. This contains
        /// all derived types and not just the immediate descendants. (For example, in A -> B -> C, C would
        /// have both A and B in this set.)
        /// </summary>
        public Dictionary<int, Type>                        DerivedTypes                { get; set; }
        public List<MethodInfo>                             OnDeserializedMethods       { get; set; }

        /// <summary>
        /// Optionally present for IGameConfigData types.
        /// If present, this is the value of the static member called ConfigNullSentinelKey,
        /// used as a sentinel for serializing a null config reference when the key type
        /// is non-nullable and would thus otherwise be unable to represent null config references.
        /// </summary>
        public object                                       ConfigNullSentinelKey       { get; set; }

        public MetaDeserializationConverter[]               DeserializationConverters   { get; set; }

#if ENABLE_SERIALIZER_GENERATION
        /// <summary>
        /// Name of type (using ToGenericTypeString())
        /// </summary>
        public string                                       GlobalNamespaceQualifiedTypeString { get; }
#endif

        public bool IsEnum                  => Type.IsEnum;
        public bool IsCollection            => WireType == WireDataType.ValueCollection || WireType == WireDataType.KeyValueCollection;
        public bool IsConcreteObject        => WireType == WireDataType.Struct || WireType == WireDataType.NullableStruct;
        public bool IsObject                => WireType == WireDataType.Struct || WireType == WireDataType.NullableStruct || WireType == WireDataType.AbstractStruct;
        public bool IsTuple                 => Type.ImplementsInterface<ITuple>();
        public bool IsAbstract              => WireType == WireDataType.AbstractStruct;
        public bool IsGameConfigData        => Type.ImplementsGenericInterface(typeof(IGameConfigData<>));
        public bool IsGameConfigDataContent => Type.IsGenericTypeOf(typeof(GameConfigDataContent<>));
        public bool IsMetaRef               => Type.IsGenericTypeOf(typeof(MetaRef<>));
        public bool IsSystemNullable        => Type.IsGenericTypeOf(typeof(Nullable<>));

        public MetaSerializableType(Type type, WireDataType wireType, bool hasSerializableAttribute)
        {
            Type                        = type;
            Name                        = type.ToGenericTypeString();
            WireType                    = wireType;
            HasSerializableAttribute    = hasSerializableAttribute;
#if ENABLE_SERIALIZER_GENERATION
            GlobalNamespaceQualifiedTypeString = type.ToGlobalNamespaceQualifiedTypeString();
#endif
        }

        // Helper struct for capturing member data for re-creation from generated code.
        public readonly struct MemberBuiltinData
        {
            public readonly int TagId;
            public readonly string Name;
            public readonly MetaMemberFlags Flags;
            public readonly Type DeclaringType;
            public readonly string OnDeserializationFailureMethodName;

            public MemberBuiltinData(
                int tagId,
                string name,
                MetaMemberFlags flags,
                Type declaringType,
                string onDeserializationFailureMethodName)
            {
                TagId                              = tagId;
                Name                               = name;
                Flags                              = flags;
                DeclaringType                      = declaringType;
                OnDeserializationFailureMethodName = onDeserializationFailureMethodName;
            }
        }

        // Helper function for re-creating a MetaSerializableType instance from generated source code.
        public static MetaSerializableType CreateFromBuiltinData(
            Type type,
            bool isPublic,
            bool usesImplicitMembers,
            WireDataType wireType,
            bool hasSerializableAttribute,
            int typeCode,
            MemberBuiltinData[] members,
            ValueTuple<int, Type>[] derivedTypes,
            ValueTuple<Type, string>[] onDeserializedMethods,
            bool hasConfigNullSentinelKey,
            bool hasDeserializationConverters)
        {
            MetaSerializableType ret = new MetaSerializableType(type, wireType, hasSerializableAttribute);
            ret.TypeCode = typeCode;
            ret.IsPublic = isPublic;
            ret.UsesImplicitMembers = usesImplicitMembers;

            if (members != null)
            {
                ret.Members = new List<MetaSerializableMember>(members.Length);
                foreach (MemberBuiltinData memberData in members)
                {
                    MemberInfo memberInfo = null;

                    try
                    {
                        memberInfo = memberData.DeclaringType.GetMember(memberData.Name, MemberTypes.Field | MemberTypes.Property,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Error("Can't find member {name} in type {type}: {ex}", memberData.Name, memberData.DeclaringType.FullName, ex);
                        throw;
                    }
                    MetaSerializableMember member = new MetaSerializableMember(memberData.TagId, memberData.Flags, memberInfo, serializerGenerationOnlyInfo: default);
                    if (memberData.OnDeserializationFailureMethodName != null)
                    {
                        member.OnDeserializationFailureMethod = memberData.DeclaringType.GetMethod(memberData.OnDeserializationFailureMethodName,
                            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    ret.Members.Add(member);
                }

                ret.MemberByTagId = ret.Members.ToDictionary(x => x.TagId);
                ret.MemberByName = ret.Members.ToDictionary(x => x.Name);
            }

            ret.DerivedTypes = derivedTypes?.ToDictionary(x => x.Item1, x => x.Item2);
            ret.OnDeserializedMethods = onDeserializedMethods?.Select(x => {
                return x.Item1.GetMethod(x.Item2, BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }).ToList();
            if (hasConfigNullSentinelKey)
                ret.ConfigNullSentinelKey = type.GetMember("ConfigNullSentinelKey", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single().GetDataMemberGetValueOnDeclaringType()(null);
            if (hasDeserializationConverters)
            {
                List<MetaDeserializationConverter> converters = new List<MetaDeserializationConverter>();
                foreach (MetaDeserializationConverterAttributeBase converterAttribute in type.GetCustomAttributes<MetaDeserializationConverterAttributeBase>(inherit: false))
                    converters.AddRange(converterAttribute.CreateConverters(type));
                ret.DeserializationConverters = converters.ToArray();
            }
            else
            {
                ret.DeserializationConverters = Array.Empty<MetaDeserializationConverter>();
            }

            return ret;
        }

        public override string ToString() => Name;

        public ConstructorInfo FindBestMatchingConstructor()
        {
            ConstructorInfo bestMatching             = null;
            bool            bestMatchingHasAttribute = false;
            foreach (ConstructorInfo constructorInfo in Type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] parameterInfos = constructorInfo.GetParameters();

                // Early exit for types that use a parameterless constructor
                if (!UsesConstructorDeserialization && parameterInfos.Length == 0)
                    return constructorInfo;

                if (parameterInfos.Length != Members.Count)
                    continue;

                HashSet<string> parameterNames          = new HashSet<string>(parameterInfos.Length);
                bool            allMatch                = true;
                bool            hasDuplicateParameters  = false;
                foreach (MetaSerializableMember member in Members)
                {
                    ParameterInfo matching = null;
                    string        loweredName = null;

                    foreach (ParameterInfo info in parameterInfos)
                    {
                        loweredName = info.Name.ToLower(CultureInfo.InvariantCulture);

                        if (loweredName.Equals(member.Name, StringComparison.OrdinalIgnoreCase) && info.ParameterType == member.Type)
                        {
                            matching = info;
                            break;
                        }
                    }

                    if (matching != null)
                    {
                        if (!parameterNames.Contains(loweredName))
                            parameterNames.Add(loweredName);
                        else
                            hasDuplicateParameters = true;
                    }
                    else
                        allMatch = false;
                }

                bool hasAttribute = constructorInfo.GetCustomAttributes<MetaDeserializationConstructorAttribute>().Any();
                if (hasAttribute)
                {
                    if (hasDuplicateParameters)
                        throw new InvalidOperationException($"Constructor in '{Type.FullName}' has duplicate case-insensitive parameters, this is not supported for constructor based deserialization.");
                    if (!allMatch)
                        throw new InvalidOperationException($"Constructor in '{Type.FullName}' with attribute {nameof(MetaDeserializationConstructorAttribute)} does not have a valid signature. Constructor parameters must have the same name and type as the MetaMembers defined in this type.");
                    if (bestMatchingHasAttribute)
                        throw new InvalidOperationException($"Type '{Type.FullName} has multiple constructors with the {nameof(MetaDeserializationConstructorAttribute)} attribute, this is not valid. Please ensure there is only one {nameof(MetaDeserializationConstructorAttribute)} attribute.");

                    bestMatching             = constructorInfo;
                    bestMatchingHasAttribute = true;
                }

                if (allMatch && !hasDuplicateParameters)
                {
                    if (bestMatching == null)
                        bestMatching = constructorInfo;
                    else if (!bestMatchingHasAttribute)
                        throw new InvalidOperationException($"Multiple valid deserialization constructors found for '{Type.FullName}', please specify the correct constructor using [MetaDeserializationConstructor].");
                }
            }

            if (bestMatching == null)
            {
                if (UsesConstructorDeserialization)
                    throw new InvalidOperationException($"Could not find matching constructor for type '{Type.FullName}'. Constructor parameters must have the same name and type as the MetaMembers defined in this type.");
                else
                    throw new InvalidOperationException($"Could not find empty constructor for type '{Type.FullName}'.");
            }

            return bestMatching;
        }
    }

    public struct MetaSerializerTypeInfo
    {
        public Dictionary<Type, MetaSerializableType> Specs;
        public uint FullTypeHash;
    }

    public interface IMetaSerializerTypeInfoProvider
    {
        MetaSerializerTypeInfo GetTypeInfo();
    }

    // MetaSerializerTypeRegistry

    public class MetaSerializerTypeRegistry
    {
        static MetaSerializerTypeInfo _typeInfo;
        public static IEnumerable<MetaSerializableType> AllTypes => _typeInfo.Specs.Values;
        public static MetaSerializerTypeInfo TypeInfo => _typeInfo;

        // Full hash of all MetaMember-tagged members of all MetaSerializable classes (for checking client/server compatibility)
        // \todo [petri] the computation is really naive and misses things like swapping members
        public static uint FullProtocolHash => _typeInfo.FullTypeHash;

        public static MetaSerializableType GetTypeSpec(Type type)
        {
            if (_typeInfo.Specs.TryGetValue(type, out MetaSerializableType typeSpec))
                return typeSpec;
            else
                throw new KeyNotFoundException($"Type {type.ToGenericTypeString()} has not been registered in MetaSerializerTypeRegistry");
        }

        public static bool TryGetTypeSpec(Type type, out MetaSerializableType typeSpec)
        {
            return _typeInfo.Specs.TryGetValue(type, out typeSpec);
        }

        public static bool TryGetDerivedType(Type type, int derivedTypeCode, out MetaSerializableType derivedTypeSpec)
        {
            if (_typeInfo.Specs.TryGetValue(type, out MetaSerializableType typeSpec))
            {
                if (typeSpec.DerivedTypes != null)
                {
                    if (typeSpec.DerivedTypes.TryGetValue(derivedTypeCode, out Type derivedType))
                        return _typeInfo.Specs.TryGetValue(derivedType, out derivedTypeSpec);
                }
            }

            derivedTypeSpec = null;
            return false;
        }

        static RuntimeTypeInfoProvider s_runtimeTypeInfoProvider = null;

        public static void RegisterRuntimeTypeInfoProvider(RuntimeTypeInfoProvider provider)
        {
            if (s_runtimeTypeInfoProvider != null)
                throw new Exception("Duplicate RuntimeTypeInfoProvider registration");

            s_runtimeTypeInfoProvider = provider;
        }

        public static void Initialize()
        {
            using (ProfilerScope.Create("MetaSerializerTypeRegistry.Initialize()"))
            {
                IMetaSerializerTypeInfoProvider provider = s_runtimeTypeInfoProvider;
                if (provider == null)
                {
                    // Attempt using MetaSerializerTypeScanner for type info
                    Type scanner = TypeScanner.TryGetTypeByName("Metaplay.Core.Serialization.MetaSerializerTypeScanner");
                    if (scanner != null)
                        provider = (IMetaSerializerTypeInfoProvider)Activator.CreateInstance(scanner);

                    if (provider == null)
                        throw new InvalidOperationException($"MetaSerialization configuration error, runtime type info not provided and type scanner not found!");
                }

                _typeInfo = provider.GetTypeInfo();
            }
        }

        internal static void OverrideTypeScanner(IMetaSerializerTypeInfoProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            using (ProfilerScope.Create($"{provider.GetType().FullName}.Initialize()"))
            {
                _typeInfo = provider.GetTypeInfo();
            }
        }
    }
}
