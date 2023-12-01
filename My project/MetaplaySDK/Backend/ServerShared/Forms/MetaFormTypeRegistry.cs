// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Metaplay.Server.Forms
{
    public enum MetaFormContentTypeKind
    {
        Class,
        Enum,
        StringId,
        DynamicEnum,
        Primitive,
        Localized,
        Abstract,
        ValueCollection,
        KeyValueCollection,
        ConfigLibraryItem
    }

    public class MetaFormContentType
    {
        public class MetaFormContentMember
        {
            public string                  FieldName            { get; private set; }
            public System.Type             Type                 { get; private set; }
            public MetaFormContentTypeKind TypeKind             { get; private set; }
            public string                  TypeName             => Type.ToNamespaceQualifiedTypeString();
            public bool                    CaptureDefault       { get; private set; }
            public bool                    IsCollection         => IsValueCollection || IsKeyValueCollection;
            public bool                    IsPrimitive          => TaggedWireSerializer.IsBuiltinType(Type);
            public bool                    IsValueCollection    => TaggedWireSerializer.GetWireType(Type) == WireDataType.ValueCollection;
            public bool                    IsKeyValueCollection => TaggedWireSerializer.GetWireType(Type) == WireDataType.KeyValueCollection;
            public System.Type[]           TypeParams           { get; private set; }
            public bool IsLocalized
            {
                get
                {
                    if (TypeKind == MetaFormContentTypeKind.Localized)
                        return true;

                    if (MetaFormTypeRegistry.Instance.TryGetTypeSpec(Type, out MetaFormContentType contentType))
                    {
                        if (contentType is MetaFormAbstractClassContentType abstractType)
                            return abstractType.IsLocalized;
                        if (contentType is MetaFormClassContentType classType)
                            return classType.IsLocalized;
                    }
                    else if (TypeKind == MetaFormContentTypeKind.ValueCollection && MetaFormTypeRegistry.Instance.TryGetTypeSpec(TypeParams[0], out MetaFormContentType arrayType))
                    {
                        if (arrayType.TypeKind == MetaFormContentTypeKind.Localized)
                            return true;
                        if (arrayType is MetaFormAbstractClassContentType abstractType)
                            return abstractType.IsLocalized;
                        if (arrayType is MetaFormClassContentType classType)
                            return classType.IsLocalized;
                    }
                    else if (TypeKind == MetaFormContentTypeKind.KeyValueCollection && MetaFormTypeRegistry.Instance.TryGetTypeSpec(TypeParams[1], out MetaFormContentType dictType))
                    {
                        if (dictType.TypeKind == MetaFormContentTypeKind.Localized)
                            return true;
                        if (dictType is MetaFormAbstractClassContentType abstractType)
                            return abstractType.IsLocalized;
                        if (dictType is MetaFormClassContentType classType)
                            return classType.IsLocalized;
                    }
                    return false;
                }
            }

            public IReadOnlyCollection<IMetaFormFieldDecorator> FieldDecorators { get; private set; }
            public IReadOnlyCollection<IMetaFormValidator>      FieldValidators { get; private set; }

            public MetaFormContentMember(
                string fieldName,
                System.Type type,
                MetaFormContentTypeKind typeKind,
                IReadOnlyCollection<IMetaFormFieldDecorator> fieldDecorators,
                bool captureDefault)
            {
                Type            = type;
                TypeKind        = typeKind;
                FieldDecorators = fieldDecorators;
                FieldName       = fieldName;
                TypeParams      = null;
                CaptureDefault  = captureDefault;
            }

            public MetaFormContentMember(
                string fieldName,
                System.Type type,
                System.Type[] typeParams,
                MetaFormContentTypeKind typeKind,
                IReadOnlyCollection<IMetaFormFieldDecorator> fieldDecorators,
                bool captureDefault)
            {
                Type            = type;
                TypeKind        = typeKind;
                FieldDecorators = fieldDecorators;
                FieldName       = fieldName;
                TypeParams      = typeParams;
                CaptureDefault  = captureDefault;
            }

            internal void SetValidators(IEnumerable<IMetaFormValidator> validators)
            {
                FieldValidators = validators.ToImmutableList();
            }
        }

        public MetaFormContentTypeKind                 TypeKind         { get; private set; }
        public MetaSerializableType                    SerializableType { get; private set; }
        public System.Type                             Type             => SerializableType.Type;
        public string                                  Name             { get; private set; }
        public IReadOnlyCollection<IMetaFormValidator> Validators       { get; private set; }

        public MetaFormContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType)
        {
            TypeKind         = typeKind;
            SerializableType = serializableType;
            Name             = SerializableType.Type.ToNamespaceQualifiedTypeString();
        }

        internal void SetValidators(IEnumerable<IMetaFormValidator> validators)
        {
            Validators = validators.ToImmutableList();
        }
    }

    public class MetaFormClassContentType : MetaFormContentType
    {
        public IReadOnlyCollection<MetaFormContentMember> Members      { get; private set; }
        public bool                                       IsLocalized  { get; private set; }
        public bool                                       IsGeneric    { get; private set; }
        public bool                                       IsDeprecated { get; private set; }
        public System.Type[]                              TypeParams   { get; private set; }

        public MetaFormClassContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, IReadOnlyCollection<MetaFormContentMember> members) : base(typeKind, serializableType)
        {
            Members     = members;
            IsLocalized = false;
            IsGeneric   = false;
            TypeParams  = null;
        }

        public MetaFormClassContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, IReadOnlyCollection<MetaFormContentMember> members, System.Type[] typeParams) : base(typeKind, serializableType)
        {
            Members     = members;
            IsLocalized = false;
            IsGeneric   = true;
            TypeParams  = typeParams;
        }

        internal void SetIsLocalized()
        {
            IsLocalized = true;
        }

        internal void SetIsDeprecated()
        {
            IsDeprecated = true;
        }
    }

    public class MetaFormAbstractClassContentType : MetaFormContentType
    {
        public bool                       IsLocalized  { get; private set; }
        public bool                       IsGeneric    { get; private set; }
        public MetaFormClassContentType[] DerivedTypes { get; private set; }
        public System.Type[]              TypeParams   { get; private set; }

        public MetaFormAbstractClassContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, MetaFormClassContentType[] derivedTypes) : base(typeKind, serializableType)
        {
            IsLocalized  = false;
            IsGeneric    = false;
            DerivedTypes = derivedTypes;
        }

        public MetaFormAbstractClassContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, MetaFormClassContentType[] derivedTypes, System.Type[] typeParams) : base(typeKind, serializableType)
        {
            IsLocalized  = false;
            IsGeneric    = true;
            TypeParams   = typeParams;
            DerivedTypes = derivedTypes;
        }

        internal void SetIsLocalized()
        {
            IsLocalized = true;
        }
    }

    public class MetaFormEnumContentType : MetaFormContentType
    {
        public IReadOnlyCollection<string> PossibleValues { get; private set; }

        public MetaFormEnumContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, IReadOnlyCollection<string> possibleValues) : base(typeKind, serializableType)
        {
            PossibleValues = possibleValues;
        }
    }

    public class MetaFormConfigReferenceContentType : MetaFormContentType
    {
        public string ConfigLibrary { get; private set; }

        public MetaFormConfigReferenceContentType(MetaFormContentTypeKind typeKind, MetaSerializableType serializableType, string configLibrary) : base(typeKind, serializableType)
        {
            ConfigLibrary = configLibrary;
        }
    }

    public class MetaFormTypeRegistry
    {
        // \todo [nomi] Nullable<T> handling
        // \todo [nomi] Abstract classes handling
        static        MetaFormTypeRegistry _instance;
        public static MetaFormTypeRegistry Instance => _instance ?? throw new InvalidOperationException($"{nameof(MetaFormTypeRegistry)} not yet initialized");

        readonly Dictionary<System.Type, MetaFormContentType> _byType          = new Dictionary<Type, MetaFormContentType>();
        readonly Dictionary<string, MetaFormContentType>      _byNsqName       = new Dictionary<string, MetaFormContentType>();
        readonly Dictionary<string, MetaFormContentType>      _byJsonName      = new Dictionary<string, MetaFormContentType>();
        readonly Dictionary<string, System.Type>              _primitiveByName = new Dictionary<string, Type>();

        static readonly System.Type[] _primitiveTypes = new System.Type[]
        {
            typeof(bool),
            typeof(sbyte),
            typeof(byte),
            typeof(short),
            typeof(ushort),
            typeof(char),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(MetaUInt128),
            typeof(F32),
            typeof(F32Vec2),
            typeof(F32Vec3),
            typeof(F64),
            typeof(F64Vec2),
            typeof(F64Vec3),
            typeof(float),
            typeof(double),
            typeof(MetaGuid),
            typeof(string),
            typeof(byte[]),
            typeof(EntityKind),
        };

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException($"Duplicate initialization of {nameof(MetaFormTypeRegistry)}");

            _instance = new MetaFormTypeRegistry();
        }

        MetaFormTypeRegistry()
        {
            IEnumerable<MetaFormContentType> types = SerializableTypesToFormTypes(MetaSerializerTypeRegistry.AllTypes);

            foreach (MetaFormContentType type in types)
            {
                BuildValidators(type);

                _byType.Add(type.Type, type);
                _byNsqName.Add(type.SerializableType.Type.ToNamespaceQualifiedTypeString(), type);
                _byJsonName.Add(TypeToJsonName(type.Type), type);
            }

            SetLocalizedTypes();

            foreach (Type primitiveType in _primitiveTypes)
                _primitiveByName.Add(primitiveType.ToNamespaceQualifiedTypeString(), primitiveType);
        }

        static IEnumerable<MetaFormContentType> SerializableTypesToFormTypes(IEnumerable<MetaSerializableType> types)
        {
            List<MetaSerializableType>     abstractTypes = new List<MetaSerializableType>();
            List<MetaFormClassContentType> classTypes    = new List<MetaFormClassContentType>();

            foreach (MetaSerializableType serializableType in types)
            {
                if (serializableType.IsAbstract && !serializableType.IsCollection)
                {
                    if (GetTypeKindFor(serializableType) == MetaFormContentTypeKind.Abstract)
                        abstractTypes.Add(serializableType);
                }

                if (!serializableType.IsAbstract && !serializableType.IsCollection)
                {
                    MetaFormContentTypeKind kind = GetTypeKindFor(serializableType);

                    if (kind == MetaFormContentTypeKind.Class || kind == MetaFormContentTypeKind.Localized)
                    {
                        List<Attribute> customAttrs = serializableType.Type.GetCustomAttributes().ToList();
                        bool derivedOnly = customAttrs.OfType<MetaFormDerivedMembersOnlyAttribute>().Any();
                        bool hidden = customAttrs.OfType<MetaFormDeprecatedAttribute>().Any() ||
                                      customAttrs.OfType<MetaFormHiddenAttribute>().Any() ||
                                      customAttrs.OfType<ObsoleteAttribute>().Any();

                        List<MetaFormContentType.MetaFormContentMember> members = new List<MetaFormContentType.MetaFormContentMember>();

                        if (serializableType.Members != null)
                        {
                            foreach (MetaSerializableMember serializableMember in serializableType.Members)
                            {
                                if (derivedOnly && serializableMember.DeclaringType != serializableType.Type)
                                    continue;

                                MetaFormContentType.MetaFormContentMember member;

                                bool captureDefault = !serializableMember.MemberInfo.GetCustomAttributes()
                                    .OfType<MetaFormDontCaptureDefaultAttribute>().Any();

                                if (serializableMember.Type.IsGenericType)
                                {
                                    member = new MetaFormContentType.MetaFormContentMember(
                                        serializableMember.Name,
                                        serializableMember.Type,
                                        serializableMember.Type.GenericTypeArguments,
                                        GetTypeKindFor(serializableMember.Type),
                                        serializableMember.MemberInfo.GetCustomAttributes()
                                            .OfType<IMetaFormFieldDecorator>().ToImmutableList(),
                                        captureDefault);
                                }
                                else if (serializableMember.Type.IsArray)
                                {
                                    member = new MetaFormContentType.MetaFormContentMember(
                                        serializableMember.Name,
                                        serializableMember.Type,
                                        new[] {serializableMember.Type.GetElementType()},
                                        GetTypeKindFor(serializableMember.Type),
                                        serializableMember.MemberInfo.GetCustomAttributes()
                                            .OfType<IMetaFormFieldDecorator>().ToImmutableList(),
                                        captureDefault);
                                }
                                else
                                {
                                    member = new MetaFormContentType.MetaFormContentMember(
                                        serializableMember.Name,
                                        serializableMember.Type,
                                        GetTypeKindFor(serializableMember.Type),
                                        serializableMember.MemberInfo.GetCustomAttributes()
                                            .OfType<IMetaFormFieldDecorator>().ToImmutableList(),
                                        captureDefault);
                                }

                                members.Add(member);
                            }
                        }

                        if (members.Count > 0)
                            SortMembersList(members);

                        MetaFormClassContentType contentType;

                        if (serializableType.Type.IsGenericType)
                        {
                            contentType = new MetaFormClassContentType(
                                kind,
                                serializableType,
                                members,
                                serializableType.Type.GenericTypeArguments);
                        }
                        else
                        {
                            contentType = new MetaFormClassContentType(
                                kind,
                                serializableType,
                                members);
                        }

                        if (kind == MetaFormContentTypeKind.Localized)
                            contentType.SetIsLocalized();

                        if (hidden)
                            contentType.SetIsDeprecated();

                        classTypes.Add(contentType);

                        yield return contentType;
                    }
                    else if (kind == MetaFormContentTypeKind.Enum)
                    {
                        yield return new MetaFormEnumContentType(
                            kind,
                            serializableType,
                            serializableType.Type.GetEnumNames());
                    }
                    else if (kind == MetaFormContentTypeKind.DynamicEnum)
                    {
                        PropertyInfo allValues = serializableType.Type.GetMember(
                            "AllValues",
                            BindingFlags.FlattenHierarchy |
                            BindingFlags.Static |
                            BindingFlags.NonPublic |
                            BindingFlags.Public).FirstOrDefault(
                            x =>
                            {
                                if (x is PropertyInfo propInfo)
                                    return typeof(ICollection).IsAssignableFrom(propInfo.PropertyType);

                                return false;
                            }) as PropertyInfo;

                        if (allValues == null)
                            throw new InvalidOperationException("Unable to find DynamicEnum<TEnum>.AllValues property.");

                        ICollection value = allValues.GetValue(null) as ICollection;

                        yield return new MetaFormEnumContentType(
                            kind,
                            serializableType,
                            value.Cast<IDynamicEnum>().Select(x => x.Name).ToImmutableList());
                    }
                    else if (kind == MetaFormContentTypeKind.StringId)
                    {
                        MetaFormConfigLibraryItemReference libraryRefAttr = serializableType.Type.GetCustomAttribute<MetaFormConfigLibraryItemReference>();

                        string libraryName;
                        if (libraryRefAttr != null)
                            libraryName = TryFindConfigLibrary(x => x.GenericTypeArguments[1] == libraryRefAttr.ConfigItemType);
                        else
                            libraryName = TryFindConfigLibrary(x => x.GenericTypeArguments[0] == serializableType.Type);

                        // \TODO: don't return config reference type when string id is not config reference!
                        yield return new MetaFormConfigReferenceContentType(kind, serializableType, libraryName);
                    }
                    else if (kind == MetaFormContentTypeKind.ConfigLibraryItem)
                    {
                        Type configItemType = serializableType.Type;
                        if (configItemType.IsGenericTypeOf(typeof(MetaRef<>)))
                            configItemType = configItemType.GenericTypeArguments[0];

                        string libraryName = TryFindConfigLibrary(x => x.GenericTypeArguments[1] == configItemType);

                        yield return new MetaFormConfigReferenceContentType(kind, serializableType, libraryName);
                    }
                    else
                        yield return new MetaFormContentType(kind, serializableType);
                }
            }

            foreach (MetaSerializableType abstractType in abstractTypes)
            {
                // Compute the possible derived types. For MetaIntegrations, we use IntegrationRegistry's
                // set of valid implementations.
                MetaFormClassContentType[] derived;
                if (IntegrationRegistry.IsMetaIntegrationType(abstractType.Type))
                {
                    HashSet<Type> integrationClasses = new HashSet<Type>(IntegrationRegistry.GetIntegrationClasses(abstractType.Type));
                    derived = classTypes.Where(x => integrationClasses.Contains(x.Type)).ToArray();
                }
                else
                {
                    derived = classTypes.Where(x => abstractType.DerivedTypes.ContainsValue(x.Type)).ToArray();
                }

                MetaFormAbstractClassContentType contentType;

                if (abstractType.Type.IsGenericType)
                {
                    contentType = new MetaFormAbstractClassContentType(
                        MetaFormContentTypeKind.Abstract,
                        abstractType,
                        derived,
                        abstractType.Type.GenericTypeArguments);
                }
                else
                {
                    contentType = new MetaFormAbstractClassContentType(
                        MetaFormContentTypeKind.Abstract,
                        abstractType,
                        derived);
                }

                yield return contentType;
            }
        }

        static void BuildValidators(MetaFormContentType contentType)
        {
            contentType.SetValidators(
                contentType.Type.GetCustomAttributes<MetaFormClassValidatorAttribute>()
                    .Concat(contentType.Type.GetInterfaces().SelectMany(i => i.GetCustomAttributes<MetaFormClassValidatorAttribute>()))
                    .DistinctBy(x => x.CustomValidatorType).Select(
                    classValidator
                        =>
                    {
                        if (classValidator.CustomValidatorType == null)
                            throw new NullReferenceException($"CustomValidatorType of {contentType.Name} is null!");

                        if (!typeof(IMetaFormValidator).IsAssignableFrom(classValidator.CustomValidatorType))
                            throw new InvalidCastException($"Cannot cast type {classValidator.CustomValidatorType} to {nameof(IMetaFormValidator)}.");

                        IMetaFormValidator validator;
                        if (classValidator.CustomValidatorType.GetConstructor(new[] {classValidator.GetType()}) != null)
                            validator = Activator.CreateInstance(classValidator.CustomValidatorType, classValidator) as IMetaFormValidator;
                        else
                            validator = Activator.CreateInstance(classValidator.CustomValidatorType) as IMetaFormValidator;

                        return validator;
                    }));

            if (contentType is MetaFormClassContentType classType)
            {
                MetaSerializableType serializableType = classType.SerializableType;

                IEnumerable<IMetaFormFieldValidatorAttribute> interfaceValidators = Array.Empty<IMetaFormFieldValidatorAttribute>();

                foreach (MetaFormContentType.MetaFormContentMember contentMember in classType.Members)
                {
                    MetaSerializableMember member = serializableType.MemberByName[contentMember.FieldName];

                    // Interface member validators
                    if (member.MemberInfo is PropertyInfo memberProp)
                    {
                        if (memberProp.CanRead)
                        {
                            Type memberDeclaringInterface = member.DeclaringType.GetInterfaces()
                                .FirstOrDefault(x => member.DeclaringType.GetInterfaceMap(x).TargetMethods.Contains(memberProp.GetMethod));
                            if (memberDeclaringInterface != null)
                            {
                                InterfaceMapping interfaceMap         = member.DeclaringType.GetInterfaceMap(memberDeclaringInterface);
                                int              interfaceMethodIndex = Array.IndexOf(interfaceMap.TargetMethods, memberProp.GetMethod);
                                MethodInfo       interfaceMethod      = interfaceMap.InterfaceMethods[interfaceMethodIndex];
                                PropertyInfo     interfaceProperty    = memberDeclaringInterface.GetProperties().FirstOrDefault(x => x.CanRead && x.GetMethod == interfaceMethod);
                                interfaceValidators = interfaceProperty.GetCustomAttributes().OfType<IMetaFormFieldValidatorAttribute>().ToArray();
                            }
                        }
                    }
                    contentMember.SetValidators(
                        member.MemberInfo.GetCustomAttributes().OfType<IMetaFormFieldValidatorAttribute>()
                            .Concat(interfaceValidators).Select(
                                fieldValidator =>
                                {
                                    if (fieldValidator.CustomValidatorType == null)
                                        throw new NullReferenceException($"CustomValidatorType of {contentType.Name}.{contentMember.FieldName} is null!");

                                    if (!typeof(IMetaFormValidator).IsAssignableFrom(fieldValidator.CustomValidatorType))
                                        throw new InvalidCastException($"Cannot cast type {fieldValidator.CustomValidatorType} to {nameof(IMetaFormValidator)}.");

                                    IMetaFormValidator validator;
                                    if (fieldValidator.CustomValidatorType.GetConstructor(new[] {fieldValidator.GetType()}) != null)
                                        validator = Activator.CreateInstance(fieldValidator.CustomValidatorType, fieldValidator) as IMetaFormValidator;
                                    else
                                        validator = Activator.CreateInstance(fieldValidator.CustomValidatorType) as IMetaFormValidator;

                                    return validator;
                                }));
                }
            }
        }

        void SetLocalizedTypes()
        {
            OrderedSet<string> checkedTypes = new OrderedSet<string>();

            bool IsTypeLocalized(MetaFormContentType type)
            {
                if (type.TypeKind == MetaFormContentTypeKind.Localized)
                    return true;

                if (type.TypeKind == MetaFormContentTypeKind.Class)
                {
                    MetaFormClassContentType classMemberType = type as MetaFormClassContentType;
                    if (classMemberType.IsLocalized)
                        return true;

                    if (IsClassTypeLocalized(classMemberType))
                    {
                        classMemberType.SetIsLocalized();
                        return true;
                    }
                }

                if (type.TypeKind == MetaFormContentTypeKind.Abstract)
                {
                    MetaFormAbstractClassContentType abstractType = type as MetaFormAbstractClassContentType;

                    foreach (MetaFormClassContentType derivedType in abstractType.DerivedTypes)
                    {
                        if (derivedType.IsLocalized || IsClassTypeLocalized(derivedType))
                        {
                            abstractType.SetIsLocalized();
                            return true;
                        }
                    }
                }

                return false;
            }

            bool IsClassTypeLocalized(MetaFormClassContentType classType)
            {
                if (checkedTypes.Contains(classType.Name)) // Recursive reference infinite loop break.
                    return classType.IsLocalized;

                checkedTypes.Add(classType.Name);

                foreach (MetaFormContentType.MetaFormContentMember member in classType.Members)
                {
                    if (member.IsValueCollection && TryGetTypeSpec(member.TypeParams[0], out MetaFormContentType valueCollectionType))
                    {
                        if (IsTypeLocalized(valueCollectionType))
                        {
                            classType.SetIsLocalized();
                            return true;
                        }
                    }
                    else if (member.IsKeyValueCollection && TryGetTypeSpec(member.TypeParams[1], out MetaFormContentType dictionaryCollectionType))
                    {
                        if (IsTypeLocalized(dictionaryCollectionType))
                        {
                            classType.SetIsLocalized();
                            return true;
                        }
                    }
                    else if (TryGetTypeSpec(member.Type, out MetaFormContentType memberType))
                    {
                        if (IsTypeLocalized(memberType))
                        {
                            classType.SetIsLocalized();
                            return true;
                        }
                    }
                }

                return false;
            }

            foreach (MetaFormContentType value in _byType.Values)
            {
                if (value is MetaFormClassContentType classType)
                {
                    if (!classType.IsLocalized && IsClassTypeLocalized(classType))
                        classType.SetIsLocalized();
                }
                else if (value is MetaFormAbstractClassContentType abstractType)
                {
                    if (!abstractType.IsLocalized && IsTypeLocalized(abstractType))
                        abstractType.SetIsLocalized();
                }
            }
        }

        static void SortMembersList(List<MetaFormContentType.MetaFormContentMember> members)
        {
            MetaFormContentType.MetaFormContentMember[] newMembers = members.Select(
                    (x, i) => new
                    {
                        order = x.FieldDecorators?.OfType<MetaFormLayoutOrderHintAttribute>().FirstOrDefault()?.Order ?? i + 1,
                        m     = x
                    }).OrderBy(o => o.order)
                .Select(o => o.m)
                .ToArray();

            members.Clear();
            members.AddRange(newMembers);
        }

        static string TryFindConfigLibrary(Predicate<Type> libraryFilter)
        {
            // Note: returning first found config library only!
            return GameConfigRepository.Instance
                .GetGameConfigTypeInfo(GameConfigRepository.Instance.SharedGameConfigType)
                .Entries.Values.Select(x => x.MemberInfo)
                .Where(x => x.GetDataMemberType().IsGameConfigLibrary() && libraryFilter(x.GetDataMemberType()))
                .Select(x => x.Name)
                .FirstOrDefault();
        }

        public static MetaFormContentTypeKind GetTypeKindFor(MetaSerializableType type) => GetTypeKindFor(type.Type);

        public static MetaFormContentTypeKind GetTypeKindFor(System.Type type)
        {
            if (_primitiveTypes.Contains(type))
                return MetaFormContentTypeKind.Primitive;
            if (type.IsDictionary())
                return MetaFormContentTypeKind.KeyValueCollection;
            if (type.IsCollection())
                return MetaFormContentTypeKind.ValueCollection;
            if (type.IsEnum)
                return MetaFormContentTypeKind.Enum;
            if (type.HasGenericAncestor(typeof(DynamicEnum<>)))
                return MetaFormContentTypeKind.DynamicEnum;
            if (type.HasGenericAncestor(typeof(StringId<>)))
                return MetaFormContentTypeKind.StringId;
            if (type.ImplementsInterface<ILocalized>())
                return MetaFormContentTypeKind.Localized;
            if (type.ImplementsInterface<IGameConfigData>())
                return MetaFormContentTypeKind.ConfigLibraryItem;
            if (type.IsGenericTypeOf(typeof(MetaRef<>)) &&
                type.GenericTypeArguments[0].ImplementsInterface<IGameConfigData>())
                return MetaFormContentTypeKind.ConfigLibraryItem;
            if (type.IsAbstract)
                return MetaFormContentTypeKind.Abstract;

            return MetaFormContentTypeKind.Class;
        }

        public bool TryGetByName(string name, out MetaFormContentType typeSpec)
        {
            return _byNsqName.TryGetValue(name, out typeSpec) ||
                _byJsonName.TryGetValue(name, out typeSpec);
        }

        public bool TryGetPrimitiveByName(string name, out System.Type type)
        {
            return _primitiveByName.TryGetValue(name, out type);
        }

        public MetaFormContentType GetTypeSpec(Type type)
        {
            if (_byType.TryGetValue(type, out MetaFormContentType typeSpec))
                return typeSpec;
            else
                throw new KeyNotFoundException($"Type {type.ToGenericTypeString()} has not been registered in MetaFormTypeRegistry");
        }

        public bool TryGetTypeSpec(Type type, out MetaFormContentType typeSpec)
        {
            return _byType.TryGetValue(type, out typeSpec);
        }

        // \todo Move to extension method?
        // \todo [jarkko] can be removed now that typenames are trivial?
        public static string TypeToJsonName(Type type)
            => $"{type.FullName}";
    }
}
