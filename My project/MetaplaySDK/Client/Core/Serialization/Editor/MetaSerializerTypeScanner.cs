// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Profiling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Runtime reflection based <see cref="IMetaSerializerTypeInfoProvider"/>. Used in Server, BotClient and Unity Editor.
    /// </summary>
    public class MetaSerializerTypeScanner : IMetaSerializerTypeInfoProvider
    {
        Dictionary<Type, MetaSerializableType> _typeSpecs;
        TypeAttributeCache                     _attrCache          = new TypeAttributeCache();
        Dictionary<Type, string>               _typeSpecificErrors = new Dictionary<Type, string>()
        {
            {typeof(DateTime), "DateTime is not supported in MetaSerialization due to inconsistent behaviour (see https://learn.microsoft.com/en-us/dotnet/api/system.datetime.frombinary?view=net-7.0#local-time-adjustment). We recommend using DateTimeOffset or MetaTime instead."}
        };

        internal static Dictionary<Type, List<MemberInfo>> TypeMemberOverrides = new Dictionary<Type, List<MemberInfo>>()
        {
            {typeof(DateTimeOffset), new List<MemberInfo>(typeof(DateTimeOffset).GetDataMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, nameof(DateTimeOffset.Ticks), nameof(DateTimeOffset.Offset)))},
            {typeof(Version), new List<MemberInfo>(typeof(Version).GetDataMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, nameof(Version.Major), nameof(Version.Minor), nameof(Version.Build), nameof(Version.Revision)))},
        };

        MetaSerializableType GetTypeSpec(Type type)
        {
            if (_typeSpecs.TryGetValue(type, out MetaSerializableType typeSpec))
                return typeSpec;
            else
                throw new KeyNotFoundException($"Type {type.ToGenericTypeString()} has not been registered in MetaSerializerTypeRegistry");
        }

        bool TryGetTypeSpec(Type type, out MetaSerializableType typeSpec)
        {
            return _typeSpecs.TryGetValue(type, out typeSpec);
        }

        static uint GetStringHash(string str)
        {
            uint hash = 0;
            for (int ndx = 0; ndx < str.Length; ndx++)
                hash = hash * 18471 + str[ndx];
            return hash;
        }

        void TryPropagateTypePublic(Type type)
        {
            // Only handle registered types (ignore system types, etc.)
            if (_typeSpecs.TryGetValue(type, out MetaSerializableType typeSpec))
            {
                // Only propagate once
                if (!typeSpec.IsPublic)
                    PropagatePublic(typeSpec);
            }
        }

        void PropagatePublic(MetaSerializableType typeSpec)
        {
            Type type = typeSpec.Type;

            // Mark type as public
            typeSpec.IsPublic = true;

            // For IGameConfigData<> implementing classes, recurse into ConfigKey
            if (typeSpec.IsGameConfigData)
            {
                // For IGameConfigData<> implementing classes, register ConfigKey
                TryPropagateTypePublic(type.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0]);
            }

            // For GameConfigDataContent<>, recurse into contained config data type
            if (type.IsGenericTypeOf(typeof(GameConfigDataContent<>)))
                TryPropagateTypePublic(type.GetGenericArguments()[0]);

            // For MetaRef<>, recurse into key type
            if (type.IsGenericTypeOf(typeof(MetaRef<>)))
            {
                Type infoType = type.GetGenericArguments()[0];
                TryPropagateTypePublic(infoType.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0]);
            }

            // Recurse into base classes with [MetaSerializable] attribute
            if (type.IsClass && type.BaseType != null)
            {
                if (TryGetMetaSerializableAttribute(type.BaseType) != null)
                    TryPropagateTypePublic(type.BaseType);
            }

            // Recursively register interfaces with [MetaSerializable] attribute
            foreach (Type interfaceType in type.GetInterfaces())
            {
                if (TryGetMetaSerializableAttribute(interfaceType) != null)
                    TryPropagateTypePublic(interfaceType);
            }

            // Recurse into child types (members of concrete objects, key/value types of collections, etc.)
            if (typeSpec.IsConcreteObject)
            {
                // Recurse into children that have not yet been marked public
                foreach (MetaSerializableMember member in typeSpec.Members)
                    TryPropagateTypePublic(member.Type);
            }
            else if (typeSpec.IsCollection)
            {
                if (type.IsDictionary())
                {
                    (Type keyType, Type valueType) = type.GetDictionaryKeyAndValueTypes();
                    TryPropagateTypePublic(keyType);
                    TryPropagateTypePublic(valueType);
                }
                else // value collection
                {
                    Type elemType = type.GetCollectionElementType();
                    TryPropagateTypePublic(elemType);
                }
            }

            // Propagate to MetaSerialized<>, Nullable<>
            if (type.IsGenericTypeOf(typeof(MetaSerialized<>)) || type.IsGenericTypeOf(typeof(Nullable<>)))
            {
                // For MetaSerialized<>, Nullable<>, ..., register contained type
                TryPropagateTypePublic(type.GetGenericArguments()[0]);
            }
        }

        IEnumerable<MetaSerializableType> GetTypesInHierarchy(MetaSerializableType typeSpec)
        {
            yield return typeSpec;

            // Traverse to base types
            while (true)
            {
                Type baseType = typeSpec.Type.BaseType;
                if (baseType != null)
                {
                    TryGetTypeSpec(baseType, out typeSpec);
                    if (typeSpec != null)
                        yield return typeSpec;
                    else
                        break;
                }
                else
                    break;
            }
        }

        class TypeMemberTagReservation
        {
            public MetaSerializableType                 Type;
            public List<MetaReservedMembersAttribute>   ReservedRanges;
            public bool                                 AllowMembersOutsideReservation;

            public TypeMemberTagReservation(MetaSerializableType type, List<MetaReservedMembersAttribute> reservedRanges, bool allowMembersOutsideReservation)
            {
                Type = type;
                ReservedRanges = reservedRanges;
                AllowMembersOutsideReservation = allowMembersOutsideReservation;
            }
        }

        void ValidateTypeSpec(MetaSerializableType typeSpec)
        {
            // For concrete objects, check that no blocked member tag ids are in use (blocks act across class boundaries)
            if (typeSpec.IsConcreteObject)
            {
                // Collect all blocked tagIds from the full type hierarchy
                HashSet<int> blockedMemberIds = new HashSet<int>(
                    GetTypesInHierarchy(typeSpec)
                    .SelectMany(t => _attrCache.GetSealedAttributesOnExactType<MetaBlockedMembersAttribute>(t.Type))
                    .SelectMany(attrib => attrib.BlockedMemberIds));

                foreach (MetaSerializableMember member in typeSpec.Members)
                {
                    // Check against blocked tagIds
                    if (blockedMemberIds.Contains(member.TagId))
                        throw new InvalidOperationException($"Type {typeSpec.Name} member {member.Name} uses blocked tag #{member.TagId}");
                }
            }

            // For objects, check that tagId reservations are not broken
            if (typeSpec.IsConcreteObject)
            {
                // Collect all blocked and reserved tagIds from the full type hierarchy & check base classes against reservations in derived classes
                OrderedDictionary<MetaSerializableType, TypeMemberTagReservation> typeMemberReservations = new OrderedDictionary<MetaSerializableType, TypeMemberTagReservation>();

                // Collect reserved member ranges from self and all derived types.
                // \note Types which don't have any MetaReservedMembersAttribute won't be represented in typeMemberReservations.
                foreach (MetaSerializableType t in GetTypesInHierarchy(typeSpec))
                {
                    IEnumerable<MetaReservedMembersAttribute> attribs = _attrCache.GetSealedAttributesOnExactType<MetaReservedMembersAttribute>(t.Type);
                    if (!attribs.Any())
                        continue;

                    typeMemberReservations.Add(t, new TypeMemberTagReservation(
                        type: t,
                        reservedRanges: attribs.ToList(),
                        allowMembersOutsideReservation: _attrCache.HasSealedAttributeOnExactType<MetaAllowNonReservedMembersAttribute>(t.Type)));
                }

                // Check reserved ranges do not overlap
                foreach (TypeMemberTagReservation reservationA in typeMemberReservations.Values)
                {
                    foreach (TypeMemberTagReservation reservationB in typeMemberReservations.Values)
                    {
                        if (reservationA.Type == reservationB.Type)
                            continue;

                        foreach (MetaReservedMembersAttribute rangeA in reservationA.ReservedRanges)
                        {
                            foreach (MetaReservedMembersAttribute rangeB in reservationB.ReservedRanges)
                            {
                                if (rangeA.StartIndex < rangeB.EndIndex && rangeA.EndIndex > rangeB.StartIndex)
                                    throw new InvalidOperationException($"Overlapping reserved ranges of {reservationA.Type} with [MetaReservedMembers({rangeA.StartIndex},{rangeA.EndIndex})] and {reservationB.Type} with [MetaReservedMembers({rangeB.StartIndex},{rangeB.EndIndex})].");
                            }
                        }
                    }
                }

                // Check base type members against reserved tagIds (ignore reservations in declaring type)
                if (typeSpec.Members != null && typeMemberReservations.Count > 0)
                {
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        foreach (TypeMemberTagReservation reservation in typeMemberReservations.Values)
                        {
                            if (GetTypeSpec(member.DeclaringType) != reservation.Type)
                            {
                                foreach (MetaReservedMembersAttribute range in reservation.ReservedRanges)
                                {
                                    if (member.TagId >= range.StartIndex && member.TagId < range.EndIndex)
                                        throw new InvalidOperationException($"{member.DeclaringType.Name}.{member.Name} uses tag #{member.TagId} but this has been reserved to {reservation.Type} with [MetaReservedMembers({range.StartIndex},{range.EndIndex})]");
                                }
                            }
                        }
                    }
                }

                // Check that each type's members reside within the type's reservation,
                // unless no reservation is specified or members are explicitly permitted
                // to be outside the reservation (with [MetaAllowNonReservedMembers]).
                if (typeSpec.Members != null && typeMemberReservations.Count > 0)
                {
                    foreach (MetaSerializableMember member in typeSpec.Members)
                    {
                        if (typeMemberReservations.TryGetValue(GetTypeSpec(member.DeclaringType), out TypeMemberTagReservation reservation)
                            && !reservation.AllowMembersOutsideReservation)
                        {
                            bool memberIsInReservation = reservation.ReservedRanges.Any(range => member.TagId >= range.StartIndex && member.TagId < range.EndIndex);
                            if (!memberIsInReservation)
                            {
                                throw new InvalidOperationException(
                                    $"{member.DeclaringType.Name}.{member.Name} uses tag #{member.TagId} which is not covered by the [MetaReservedMembers(...)] attributes of {reservation.Type}."
                                    + $" Either adjust the reserved member ranges, add [MetaAllowNonReservedMembers], or remove the `[MetaReservedMembers(...)]` attributes.");
                            }
                        }
                    }
                }
            }

            // Check internal messages don't sneak in
            if (typeSpec.IsPublic)
            {
                MetaMessageAttribute attrib = _attrCache.TryGetSealedAttributeOnTypeOrAncestor<MetaMessageAttribute>(typeSpec.Type);
                if (attrib != null)
                {
                    if (attrib.Direction == MessageDirection.ClientInternal || attrib.Direction == MessageDirection.ServerInternal)
                        throw new InvalidOperationException($"Internal message {typeSpec.Name} may not be in SharedNamespaces or refered by a shared message, action or model. Internal messages are client or server specific, and are not allowed in shared code.");
                }
            }

            // Check classes have at least one serializeable member, unless it has no fields at all or if it is intentionally allowed.
            if (typeSpec.IsConcreteObject && _attrCache.TryGetSealedAttributeOnTypeOrAncestor<MetaAllowNoSerializedMembers>(typeSpec.Type) == null
                && !typeSpec.IsSystemNullable && !typeSpec.IsGameConfigData && !typeSpec.IsGameConfigDataContent && !typeSpec.IsMetaRef)
            {
                bool hasNoMembersAtAll;
                bool hasNoDirectMembers;
                if (typeSpec.Members.Count == 0)
                {
                    hasNoMembersAtAll = true;
                    hasNoDirectMembers = true;
                }
                else
                {
                    hasNoMembersAtAll = false;
                    hasNoDirectMembers = typeSpec.Members.All(member => member.DeclaringType != typeSpec.Type);
                }

                bool IsWritableFieldOrProperty(MemberInfo dataMember)
                {
                    // read-only property
                    if (dataMember is PropertyInfo propertyInfo && propertyInfo.GetSetMethodOnDeclaringType() == null)
                        return false;
                    // read-only field
                    if (dataMember is FieldInfo fieldInfo && fieldInfo.IsInitOnly)
                        return false;
                    if (_attrCache.HasSealedAttributeOnMember<IgnoreDataMemberAttribute>(dataMember))
                        return false;
                    if (_attrCache.HasSealedAttributeOnMember<CompilerGeneratedAttribute>(dataMember))
                        return false;
                    return true;
                }

                if (hasNoMembersAtAll)
                {
                    // Check the type have any fields or properties at all.
                    bool hasSomeNonSerializedDataMember = typeSpec.Type
                        .EnumerateInstanceDataMembersInUnspecifiedOrder()
                        .Where(IsWritableFieldOrProperty)
                        .Any();

                    if (hasSomeNonSerializedDataMember)
                        throw new InvalidOperationException(
                            $"Type {typeSpec.Name} has no serialized members. This is most likely an error. Define members by annotating fields or properties with [MetaMember], or use MetaSerializableFlags.ImplicitMembers in [MetaSerializable()] arguments. "
                            + "If empty class is desired, you may enable it by adding [MetaAllowNoSerializedMembers] on the class.");
                }
                else if (hasNoDirectMembers && typeSpec.TypeCode == 0)
                {
                    // Check the type have any fields or properties at all, Directly.
                    bool hasSomeNonSerializedDirectDataMember = typeSpec.Type
                        .EnumerateInstanceDataMembersInUnspecifiedOrder()
                        .Where(dataMember => dataMember.DeclaringType == typeSpec.Type) // is a direct member
                        .Where(IsWritableFieldOrProperty)
                        .Any();

                    if (hasSomeNonSerializedDirectDataMember)
                        throw new InvalidOperationException(
                            $"Type {typeSpec.Name} has no serialized members except those provided by its base class, but the type is not marked as [MetaSerializableDerived]. This is most likely an error. Define members by annotating fields or properties with [MetaMember], or use MetaSerializableFlags.ImplicitMembers in [MetaSerializable()] arguments. "
                            + "If empty class is desired, you may enable it by adding [MetaAllowNoSerializedMembers] on the class.");
                }
            }

            // Check that MetaRefs within config data are reachable via serialization.
            // This is checked because MetaRef late-resolving is done with generated code,
            // piggybacked into the serializer generator, which does not traverse non-MetaMembers.
            if (typeSpec.IsGameConfigData || typeSpec.Type.IsDerivedFrom<GameConfigKeyValue>())
            {
                CheckMetaRefsSerializationReachability(
                    typeSpec.Type,
                    isRoot: true,
                    isReachable: true,
                    firstNonMetaMemberAccessPath: null,
                    accessPath: Enumerable.Repeat(typeSpec.Type.Name, 1));
            }
        }

        HashSet<(Type type, bool isRoot, bool isReachable)> _metaRefsSerializationReachabilityVisited = new HashSet<(Type type, bool isRoot, bool isReachable)>();
        /// <summary>
        /// Recursively check that <paramref name="type"/> does not contain MetaRefs that are not reachable via serialization.
        /// </summary>
        void CheckMetaRefsSerializationReachability(Type type, bool isRoot, bool isReachable, IEnumerable<string> firstNonMetaMemberAccessPath, IEnumerable<string> accessPath)
        {
            // Memoization and cycle avoidance.
            if (!_metaRefsSerializationReachabilityVisited.Add((type: type, isRoot: isRoot, isReachable: isReachable)))
                return;

            if (TaggedWireSerializer.IsBuiltinType(type)
#if NETCOREAPP // cloud
             || type == typeof(Akka.Actor.IActorRef)
#endif
             || type.ImplementsInterface<IDynamicEnum>()
             || type.ImplementsInterface<IStringId>()
             || type.IsEnum)
            {
                // Leaf type
            }
            else if (type.IsCollection())
            {
                // Descend into collection element (and key, for dicts) types.
                // A collection type is serializable assuming its contained type is, so isReachable is unmodified.

                if (type.IsDictionary())
                {
                    (Type keyType, Type valueType) = type.GetDictionaryKeyAndValueTypes();
                    CheckMetaRefsSerializationReachability(keyType, isRoot: false, isReachable, firstNonMetaMemberAccessPath, accessPath.Append($":Key"));
                    CheckMetaRefsSerializationReachability(valueType, isRoot: false, isReachable, firstNonMetaMemberAccessPath, accessPath.Append($":Value"));
                }
                else // value collection
                {
                    Type elemType = type.GetCollectionElementType();
                    CheckMetaRefsSerializationReachability(elemType, isRoot: false, isReachable, firstNonMetaMemberAccessPath, accessPath.Append($":Element"));
                }
            }
            else if (type.IsGenericTypeOf(typeof(Nullable<>)))
            {
                // Nullable: descend into underlying type.
                // A nullable type is serializable assuming its underlying type is, so isReachable is unmodified.
                CheckMetaRefsSerializationReachability(type.GetSystemNullableElementType(), isRoot: false, isReachable, firstNonMetaMemberAccessPath, accessPath.Append($".Value"));
            }
            else if (type.IsGenericTypeOf(typeof(MetaRef<>)))
            {
                // MetaRef: error if not reachable.

                if (!isReachable)
                {
                    throw new InvalidOperationException(
                        $"Encountered MetaRef<{type.GetGenericArguments()[0].Name}> in a non-serialized location: {string.Concat(accessPath)} ."
                        + $" This location is not serialized, because {string.Concat(firstNonMetaMemberAccessPath)} is not a [MetaMember] in a [MetaSerializable] type."
                        + $" To be resolvable, each MetaRef within a IGameConfigData or GameConfigKeyValue type must be reachable by serialization.");
                }
            }
            else if (
                (!type.IsAbstract && (isRoot || !type.ImplementsGenericInterface(typeof(IGameConfigData<>))))
                || type.IsGenericTypeOf(typeof(GameConfigDataContent<>)))
            {
                // By-members object: descend into type of each member (except getter-only members),
                // and consider non-MetaMembers as non-reachable. MetaMembers are reachable, assuming
                // the current access path is reachable.

                if (type.IsGenericTypeOf(typeof(GameConfigDataContent<>)))
                {
                    type = type.GetGenericArguments()[0];
                    accessPath = accessPath.Append(".ConfigData");
                }

                TryGetTypeSpec(type, out MetaSerializableType typeSpecMaybe);

                foreach (MemberInfo member in type.EnumerateInstanceDataMembersInUnspecifiedOrder())
                {
                    if (member.GetDataMemberSetValueOnDeclaringType() == null)
                        continue;

                    bool isMetaMember = typeSpecMaybe?.MemberByName.ContainsKey(member.Name) ?? false;
                    bool memberIsReachable = isReachable && isMetaMember;
                    IEnumerable<string> memberAccessPath = accessPath.Append($".{member.Name}");

                    CheckMetaRefsSerializationReachability(
                        type:                           member.GetDataMemberType(),
                        isRoot:                         false,
                        isReachable:                    memberIsReachable,
                        firstNonMetaMemberAccessPath:   isReachable && !isMetaMember ? memberAccessPath : firstNonMetaMemberAccessPath,
                        accessPath:                     memberAccessPath);
                }
            }
        }

        Dictionary<Type, MetaSerializableAttribute> _memoForTryGetMetaSerializableAttribute = new Dictionary<Type, MetaSerializableAttribute>();

        /// <summary>
        /// Find the <see cref="MetaSerializableAttribute"/>, if any, that should be used for <paramref name="type"/>.
        ///
        /// <para>
        /// The attribute may be specified in the type itself (including its base type chain), or one of the interfaces implemented by the type.
        /// </para>
        /// </summary>
        /// <param name="type">The type for which to find the <see cref="MetaSerializableAttribute"/>.</param>
        /// <returns>The <see cref="MetaSerializableAttribute"/> that should be used for <paramref name="type"/>, or null if no attribute is found.</returns>
        MetaSerializableAttribute TryGetMetaSerializableAttribute(Type type)
        {
            // Memoization. The hit rate is expected to be low, but InternalTryGetMetaSerializableAttribute is expensive so it's worth it.
            if (_memoForTryGetMetaSerializableAttribute.TryGetValue(type, out MetaSerializableAttribute cached))
                return cached;
            MetaSerializableAttribute attr = InternalTryGetMetaSerializableAttribute(type);
            if (!_memoForTryGetMetaSerializableAttribute.ContainsKey(type))
                _memoForTryGetMetaSerializableAttribute.Add(type, attr);
            return attr;
        }

        MetaSerializableAttribute InternalTryGetMetaSerializableAttribute(Type type)
        {
            // \note This always searches all the potential places where the MetaSerializableAttribute may be found
            //       (that is, does not early-exit when it finds it) in order to report an error in case multiple
            //       MetaSerializableAttributes are specified.

            // Search the type and its base classes.
            MetaSerializableAttribute mostDerivedMetaSerializable = null;
            Type mostDerivedMetaSerializableSource = null;
            List<(ISerializableTypeCodeProvider, Type)> typeCodeProvidersInHierarchy = new List<(ISerializableTypeCodeProvider, Type)>();
            for (Type ancestor = type; ancestor != null; ancestor = ancestor.BaseType)
            {
                MetaSerializableAttribute attribute = _attrCache.TryGetSealedAttributeOnExactType<MetaSerializableAttribute>(ancestor);

                if (attribute != null)
                {
                    if (mostDerivedMetaSerializable == null)
                    {
                        mostDerivedMetaSerializable = attribute;
                        mostDerivedMetaSerializableSource = ancestor;
                    }
                    else if (!attribute.Equals(mostDerivedMetaSerializable))
                    {
                        throw new InvalidOperationException($"Conflicting [MetaSerializable] parameters found for type {mostDerivedMetaSerializableSource.ToGenericTypeString()}: {mostDerivedMetaSerializable} conflicts with {attribute} in base class {ancestor.ToGenericTypeString()}.");
                    }
                }

                IEnumerable<ISerializableTypeCodeProvider> typeCodeProviders = _attrCache.GetDerivedAttributesOnExactType<ISerializableTypeCodeProvider>(ancestor);
                foreach (ISerializableTypeCodeProvider typeCodeProvider in typeCodeProviders)
                    typeCodeProvidersInHierarchy.Add((typeCodeProvider, ancestor));
            }

            // Search interfaces.
            MetaSerializableAttribute resultAttribute = mostDerivedMetaSerializable;
            Type resultAttributeSourceType = mostDerivedMetaSerializableSource; // Tracks where the attribute has been found.
            foreach (Type interfaceType in type.GetInterfaces())
            {
                MetaSerializableAttribute attributeInInterface = _attrCache.TryGetSealedAttributeOnTypeOrAncestor<MetaSerializableAttribute>(interfaceType);
                if (attributeInInterface != null)
                {
                    // If the interface is disabled, the interface type does not contribute to the attribute search.
                    // \note: Type is first checked for [MetaSerializable] before IsMetaFeatureEnabled() is invoked. This
                    //        allows type-checks to perform more complex operation that are possible during early type scan.
                    if (!MetaplayFeatureEnabledConditionAttribute.IsEnabledWithTypeAndAncestorAttributes(_attrCache.GetDerivedAttributesOnTypeOrAncestor<MetaplayFeatureEnabledConditionAttribute>(interfaceType)))
                        continue;

                    if (resultAttribute == null)
                    {
                        resultAttribute = attributeInInterface;
                        resultAttributeSourceType = interfaceType;
                    }
                    else if (!attributeInInterface.Equals(resultAttribute))
                    {
                        string attributeSourceDescription;
                        if (resultAttributeSourceType == type)
                            attributeSourceDescription = "the type itself";
                        else
                            attributeSourceDescription = resultAttributeSourceType.ToGenericTypeString();

                        throw new InvalidOperationException($"Conflicting [MetaSerializable] parameters found for type {type.ToGenericTypeString()}: {resultAttribute} in {attributeSourceDescription} conflicts with {attributeInInterface} in interface {interfaceType.ToGenericTypeString()}.");
                    }
                }
            }

            // Early exit if not serializable.
            if (resultAttribute == null && typeCodeProvidersInHierarchy.Count == 0)
                return null;

            // If the type is disabled, it's not serializable
            // \note: Type is first searched for [MetaSerializable] before IsMetaFeatureEnabled() is invoked. This
            //        allows type-checks to perform more complex operation that are possible during early type scan.
            if (!MetaplayFeatureEnabledConditionAttribute.IsEnabledWithTypeAndAncestorAttributes(_attrCache.GetDerivedAttributesOnTypeOrAncestor<MetaplayFeatureEnabledConditionAttribute>(type)))
                return null;

            // Having an ISerializableTypeCodeProvider-implementing attribute
            // without having MetaSerializableAttribute is probably a mistake
            // (forgetting to derive from a MetaSerializableAttribute-equipped base class).
            if (typeCodeProvidersInHierarchy.Any() && resultAttribute == null)
            {
                (ISerializableTypeCodeProvider typeCodeProvider, Type typeWithAttribute) = typeCodeProvidersInHierarchy.First();
                throw new InvalidOperationException($"{typeWithAttribute.ToGenericTypeString()} has an {nameof(ISerializableTypeCodeProvider)}-implementing attribute ({typeCodeProvider.GetType().Name}) but does not derive from a type with {nameof(MetaSerializableAttribute)}");
            }

            // Check if there is missing Derived Attribute or some other
            // ISerializableTypeCodeProvider. This happens if there is an
            // MetaSerializable attribute, but it is not on the most derived type.
            // Abstract types are ignored as they cannot be constructed and their
            // subtypes must pass this same validation.
            if (resultAttribute != null && resultAttributeSourceType != type && !typeCodeProvidersInHierarchy.Any() && !type.IsAbstract)
            {
                if (resultAttributeSourceType.IsAbstract)
                    throw new InvalidOperationException($"Type {type.ToGenericTypeString()} extends [MetaSerializable] base class or interface {resultAttributeSourceType.ToGenericTypeString()} but is missing [MetaSerializableDerived] or any other {nameof(ISerializableTypeCodeProvider)}-implementing attribute.");
                else
                    throw new InvalidOperationException($"Type {type.ToGenericTypeString()} has [MetaSerializable] base class {resultAttributeSourceType.ToGenericTypeString()} but doesn't itself have the [MetaSerializable] attribute. Please either add [MetaSerializable] on {type.ToGenericTypeString()} to make it serializable, or alternatively if you intend to serialize it via the base class, make the base class abstract and add [MetaSerializableDerived(...)] on {type.ToGenericTypeString()}.");
            }

            // Check type code providers agree on the type code
            if (typeCodeProvidersInHierarchy.Count > 1)
            {
                (ISerializableTypeCodeProvider firstTypeCodeProvider, Type firstTypeWithAttribute) = typeCodeProvidersInHierarchy.First();
                foreach ((ISerializableTypeCodeProvider typeCodeProvider, Type typeWithAttribute) in typeCodeProvidersInHierarchy.Skip(1))
                {
                    if (typeCodeProvider.TypeCode != firstTypeCodeProvider.TypeCode)
                        throw new InvalidOperationException($"Type {type.ToGenericTypeString()} has {firstTypeCodeProvider.GetType().Name} with TypeCode = {firstTypeCodeProvider.TypeCode} in {firstTypeWithAttribute.ToGenericTypeString()} but also {typeCodeProvider.GetType().Name} in {typeWithAttribute.ToGenericTypeString()} with conflicting TypeCode = {typeCodeProvider.TypeCode}.");
                }
            }

            return resultAttribute;
        }

        class TypeRegistrationError : Exception
        {
            public Type FailingType { get; private set; }
            public List<Type> Parents { get; private set; }

            TypeRegistrationError(string msg, Exception innerException) : base(msg, innerException)
            {
            }

            public static TypeRegistrationError Create(Type type, Exception innerException)
            {
                TypeRegistrationError ex = new TypeRegistrationError($"Failed to process {type.ToNamespaceQualifiedTypeString()}.", innerException);
                ex.FailingType = type;
                ex.Parents = new List<Type>();
                return ex;
            }
            public static TypeRegistrationError Chain(TypeRegistrationError original, Type type)
            {
                List<Type> parents = new List<Type>();
                parents.Add(type);
                parents.AddRange(original.Parents);

                TypeRegistrationError ex = new TypeRegistrationError($"Failed to process {original.FailingType.ToNamespaceQualifiedTypeString()}. Required by {string.Join(" -> ", parents.Select(t => t.ToNamespaceQualifiedTypeString()))}.", original.InnerException);
                ex.FailingType = original.FailingType;
                ex.Parents = parents;
                return ex;
            }
        }

        MetaSerializableType TryRegisterType(Type type)
        {
            // Collapse all exceptions into single TypeRegistrationError that tracks
            // the access path chain. This is useful when investigating which type
            // incorrectly refers to a disabled type.
            try
            {
                return InternalTryRegisterType(type);
            }
            catch (TypeRegistrationError ex)
            {
                throw TypeRegistrationError.Chain(ex, type);
            }
            catch (Exception ex)
            {
                throw TypeRegistrationError.Create(type, ex);
            }
        }

        MetaSerializableType InternalTryRegisterType(Type type)
        {
            // Skip already registered types
            if (_typeSpecs.TryGetValue(type, out MetaSerializableType existing))
                return existing;

            if (_typeSpecificErrors.TryGetValue(type, out string error))
                throw new InvalidOperationException(error);

            // Resolve flags for type
            MetaSerializableAttribute serializableAttrib = TryGetMetaSerializableAttribute(type);

            bool isGameConfigData = type.ImplementsGenericInterface(typeof(IGameConfigData<>));

            // \note Some of the types below recurse into registering other, related types,
            //       such as class member types, collection element types, etc.
            //       When this is done, the current type itself should be registered first,
            //       in order to ensure that the "already registered" check above will get
            //       triggered and we don't error out due to duplicate registration.
            //       #serializer-duplicate-registration-issue
            //
            //       Currently, this doesn't hold for all cases where it's not trivial to achieve.
            //       For example, MetaRef's key type is registered before the MetaRef type
            //       itself. If a certain kind of cycle is formed involving the MetaRef type
            //       and its key type (a construct which might not really make sense), init
            //       will fail.

            // Only process user types (not system ones)
            if (TaggedWireSerializer.IsBuiltinType(type))
            {
                // ignore built-in types
                return null;
            }
#if NETCOREAPP // cloud
            else if (type == typeof(Akka.Actor.IActorRef))
            {
                // ignore IActorRef (another built-in)
                return null;
            }
#endif
            else if (type.ImplementsInterface<IDynamicEnum>())
            {
                if (serializableAttrib == null)
                    throw new InvalidOperationException($"DynamicEnum {type.ToGenericTypeString()} is missing [MetaSerializable] attribute");

                MetaSerializableType typeSpec = new MetaSerializableType(type, WireDataType.VarInt, serializableAttrib != null);
                DoRegisterType(type, typeSpec);
                return typeSpec;
            }
            else if (type.ImplementsInterface<IStringId>())
            {
                if (serializableAttrib == null)
                    throw new InvalidOperationException($"StringId {type.ToGenericTypeString()} is missing [MetaSerializable] attribute");

                Type stringIdType = type.TryGetGenericAncestor(typeof(StringId<>));
                if (stringIdType is null)
                    throw new InvalidOperationException($"{type.ToGenericTypeString()} implements IStringId, but is not derived from StringId<>");

                Type genericArgType = stringIdType.GetGenericArguments()[0];
                if (genericArgType != type)
                    throw new InvalidOperationException($"StringId {type.ToGenericTypeString()} should use itself as the generic argument, but is using {genericArgType.ToGenericTypeString()}");

                MetaSerializableType typeSpec = new MetaSerializableType(type, WireDataType.String, serializableAttrib != null);
                DoRegisterType(type, typeSpec);
                return typeSpec;
            }
            else if (type.IsCollection())
            {
                WireDataType wireType = TaggedWireSerializer.GetWireType(type, _typeSpecs);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                DoRegisterType(type, typeSpec);

                // Register also key and element types
                if (type.IsDictionary())
                {
                    (Type keyType, Type valueType) = type.GetDictionaryKeyAndValueTypes();
                    TryRegisterType(keyType);
                    TryRegisterType(valueType);
                }
                else // value collection
                {
                    Type elemType = type.GetCollectionElementType();
                    TryRegisterType(elemType);
                }

                return typeSpec;
            }
            else if (type.IsGenericTypeOf(typeof(Nullable<>)))
            {
                Type elemType = type.GetGenericArguments()[0];

                WireDataType wireType = elemType.IsEnum
                                        ? TaggedWireSerializer.PrimitiveTypeWrapNullable(TaggedWireSerializer.GetWireType(elemType.GetEnumUnderlyingType(), _typeSpecs))
                                        : WireDataType.NullableStruct;

                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                DoRegisterType(type, typeSpec);

                // Register also contained type
                TryRegisterType(elemType);

                return typeSpec;
            }
            else if (type.IsGenericTypeOf(typeof(GameConfigDataContent<>)))
            {
                Type            gameConfigDataType  = type.GetGenericArguments()[0];
                WireDataType    wireType            = GetObjectWireDataType(gameConfigDataType);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                DoRegisterType(type, typeSpec);

                // Register also contained config data type.
                // Assert consistency of GetObjectWireDataType and the wire type assigned by TryRegisterType.
                WireDataType registeredDataWireType = TryRegisterType(gameConfigDataType).WireType;
                if (registeredDataWireType != wireType)
                    throw new InvalidOperationException($"{type.ToGenericTypeString()} expected {gameConfigDataType.ToGenericTypeString()} to have {nameof(WireDataType)} {wireType}, but it has {registeredDataWireType}.");

                return typeSpec;
            }
            else if (type.IsGenericTypeOf(typeof(MetaRef<>)))
            {
                Type            infoType    = type.GetGenericArguments()[0];
                Type            keyType     = infoType.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0];

                // Need to register key type first, because we need its wire type
                // in order to register the MetaRef<...> type.
                // \todo Figure out the wire type some other way to avoid problems
                //       in cyclic cases. See comment above about #serializer-duplicate-registration-issue.
                TryRegisterType(keyType);

                WireDataType wireType = TaggedWireSerializer.GetWireType(keyType, _typeSpecs);

                // Register the MetaRef<...> type.
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                DoRegisterType(type, typeSpec);

                // Register also referred info type
                TryRegisterType(infoType);

                return typeSpec;
            }
            else if (serializableAttrib != null && type.IsEnum)
            {
                WireDataType wireType = TaggedWireSerializer.GetWireType(type.GetEnumUnderlyingType(), _typeSpecs);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);

                // Check Enum elements are distinct. It is most likely an error to have same value on different entries.
                HashSet<object> seenValues = new HashSet<object>();
                Array elementValues = type.GetEnumValues();
                for (int ndx = 0; ndx < elementValues.Length; ++ndx)
                {
                    if (seenValues.Add(elementValues.GetValue(ndx)))
                        continue;

                    MetaDebug.AssertFail("Enum {0} elements are not distinct. Element \"{1}\" value is not unique.", typeSpec.Type.ToNamespaceQualifiedTypeString(), type.GetEnumNames()[ndx]);
                }

                DoRegisterType(type, typeSpec);
                return typeSpec;
            }
            else if (type.ImplementsInterface<ITuple>())
            {
                // Create typeSpec for type
                WireDataType         wireType = GetObjectWireDataType(type);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                typeSpec.UsesConstructorDeserialization = true;

                // Register type
                // \note registering before recursing to children to avoid duplicate traversals
                DoRegisterType(type, typeSpec);

                if (type.GenericTypeArguments.Length > 7)
                    throw new InvalidOperationException($"Type has more than 7 elements, this is not supported in meta serialized tuples.");

                // scan members for more serialized types (including parameterized generic members)
                foreach (MemberInfo memberInfo in GetTupleMembers(type))
                    TryRegisterType(memberInfo.GetDataMemberType());

                return typeSpec;
            }
            else if (TypeMemberOverrides.TryGetValue(type, out List<MemberInfo> members))
            {
                // Create typeSpec for type
                WireDataType         wireType = GetObjectWireDataType(type);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);
                typeSpec.UsesConstructorDeserialization = true;

                // Register type
                // \note registering before recursing to children to avoid duplicate traversals
                DoRegisterType(type, typeSpec);

                // scan members for more serialized types (including parameterized generic members)
                foreach (MemberInfo memberInfo in members)
                {
                    TryRegisterType(memberInfo.GetDataMemberType());
                }

                return typeSpec;
            }
            else if ((serializableAttrib != null || isGameConfigData) && (type.IsClass || type.IsInterface || (type.IsValueType && !type.IsEnum && !type.IsPrimitive))) // user/config class/interface/struct
            {
                // Create typeSpec for type
                WireDataType wireType = GetObjectWireDataType(type);
                MetaSerializableType typeSpec = new MetaSerializableType(type, wireType, serializableAttrib != null);

                // For abstract types, initialize derived types dictionary
                if (type.IsAbstract)
                    typeSpec.DerivedTypes = new Dictionary<int, Type>();

                // Resolve UsesImplicitMembers and UsesConstructorDeserialization before constructor resolving to ensure we can find the correct one.
                if ((type.IsClass || type.IsValueType) && !type.IsPrimitive && !type.IsAbstract)
                {
                    if (serializableAttrib != null)
                    {
                        typeSpec.UsesImplicitMembers            = serializableAttrib.Flags.HasFlag(MetaSerializableFlags.ImplicitMembers);
                        bool hasDeserializationConstructorAttribute = TypeHasMetaDeserializationConstructor(type);
                        bool hasAutomaticConstructorDetectionFlag      = serializableAttrib.Flags.HasFlag(MetaSerializableFlags.AutomaticConstructorDetection);

                        foreach (ISerializableFlagsProvider attrib in _attrCache.GetDerivedAttributesOnTypeOrAncestor<ISerializableFlagsProvider>(type))
                        {
                            if (attrib.ExtraFlags.HasFlag(MetaSerializableFlags.ImplicitMembers))
                                typeSpec.UsesImplicitMembers = true;
                            if (attrib.ExtraFlags.HasFlag(MetaSerializableFlags.AutomaticConstructorDetection))
                                hasAutomaticConstructorDetectionFlag = true;
                        }

                        if (hasAutomaticConstructorDetectionFlag && hasDeserializationConstructorAttribute)
                            throw new InvalidOperationException($"Type '{type.FullName}' has both the {nameof(MetaSerializableFlags.AutomaticConstructorDetection)} flag and the '{nameof(MetaDeserializationConstructorAttribute)}' attribute, this is not supported. Please remove either the flag or the attribute.");

                        typeSpec.UsesConstructorDeserialization = hasAutomaticConstructorDetectionFlag | hasDeserializationConstructorAttribute;
                    }
                }

                // If class, must have parameterless ctor
                if (type.IsClass && !type.IsAbstract && !typeSpec.UsesConstructorDeserialization)
                {
                    ConstructorInfo ctorInfo = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);
                    MetaDebug.Assert(ctorInfo != null, "No empty ctor found for {0}", type.ToGenericTypeString());
                }

                // Register type
                // \note registering before recursing to children to avoid duplicate traversals
                DoRegisterType(type, typeSpec);

                // For MetaSerialized<>, register contained type
                if (type.IsGenericTypeOf(typeof(MetaSerialized<>)))
                    TryRegisterType(type.GetGenericArguments()[0]);

                // For IGameConfigData<> implementing classes, register ConfigKey
                if (isGameConfigData)
                    TryRegisterType(type.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0]);

                // If class or struct, scan members for more serialized types (including parameterized generic members)
                if ((type.IsClass || type.IsValueType) && !type.IsPrimitive && !type.IsAbstract)
                {
                    foreach (MemberInfo member in ResolveTypeMetaMembers(type, typeSpec.UsesImplicitMembers))
                        TryRegisterType(member.GetDataMemberType());
                }

                // Recursively register base classes with [MetaSerializable] attribute
                if (type.IsClass && type.BaseType != null)
                {
                    if (TryGetMetaSerializableAttribute(type.BaseType) != null)
                        TryRegisterType(type.BaseType);
                }

                // Recursively register interfaces with [MetaSerializable] attribute
                foreach (Type interfaceType in type.GetInterfaces())
                {
                    if (TryGetMetaSerializableAttribute(interfaceType) != null)
                        TryRegisterType(interfaceType);
                }

                return typeSpec;
            }
            else
                throw new InvalidOperationException($"Cannot register type {type.ToGenericTypeString()} for Serialization. The type is not a known built-in, [MetaSerializable] or custom serializeable (MetaMessage, ModelAction etc.), or the type is disabled with [MetaplayFeatureEnabledCondition].");
        }

        static bool TypeHasMetaDeserializationConstructor(Type type)
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(x=>x.GetCustomAttributes<MetaDeserializationConstructorAttribute>().Any());
        }

        /// <summary>
        /// Get the wire type of the given type, assuming that it
        /// is an "object" type in the serialization sense.
        /// Returns either <see cref="WireDataType.Struct"/>,
        /// <see cref="WireDataType.NullableStruct"/>,
        /// or <see cref="WireDataType.AbstractStruct"/>.
        /// </summary>
        /// <remarks>
        /// The given type must be an "object" type in the serialization
        /// sense - that is, a normal by-members-serialized class/struct
        /// and not for example a StringId or a GameConfigDataContent.
        /// This method assumes but does not validate that, and may
        /// return invalid results if that does not hold.
        /// </remarks>
        static WireDataType GetObjectWireDataType(Type type)
        {
            if (type.IsAbstract)
                return WireDataType.AbstractStruct;
            else if (type.IsClass)
                return WireDataType.NullableStruct;
            else
                return WireDataType.Struct;
        }

        void DoRegisterType(Type type, MetaSerializableType typeSpec)
        {
            if (_typeSpecs.ContainsKey(type))
                throw new InvalidOperationException($"Duplicate registration of type {type.ToGenericTypeString()} for serialization. This is probably a bug in the Metaplay SDK.");

            _typeSpecs.Add(type, typeSpec);
        }

        MetaMemberFlags ResolveDataMemberFlags(MemberInfo member, MetaMemberAttribute memberAttrib)
        {
            MetaMemberFlags flags = memberAttrib != null ? memberAttrib.Flags : MetaMemberFlags.None;
            foreach (MetaMemberFlagAttribute attrib in _attrCache.GetDerivedAttributesOnMember<MetaMemberFlagAttribute>(member))
                flags |= attrib.Flags;
            return flags;
        }

        void RegisterChildTypeForBase(Dictionary<Type, Dictionary<int, Type>> childTypesPerBase, Type baseType, int childTypeCode, Type childType)
        {
            if (!childTypesPerBase.TryGetValue(baseType, out Dictionary<int, Type> childTypes))
            {
                childTypes = new Dictionary<int, Type>();
                childTypesPerBase.Add(baseType, childTypes);

                MetaSerializableType baseTypeSpec = _typeSpecs[baseType];
                baseTypeSpec.DerivedTypes = childTypes;
            }

            if (childTypes.ContainsKey(childTypeCode))
                MetaDebug.AssertFail("Duplicate type code {0} for {1} and {2}", childTypeCode, childType.FullName, childTypes[childTypeCode].FullName);
            childTypes.Add(childTypeCode, childType);
        }

        static bool IsPublicNamespace(string nsToCheck, string[] sharedNamespaces)
        {
            return sharedNamespaces.Any(sharedNs => nsToCheck.StartsWith(sharedNs, StringComparison.Ordinal))
                || nsToCheck.StartsWith("Metaplay.Core", StringComparison.Ordinal);
        }

        static Assembly[] GetRelevantAssemblies(string[] assemblyNameNotIn = null)
        {
            // Find all assemblies and build a dictionary for all assemblies referring to a certain assembly
            Dictionary<string, List<Assembly>> assembliesReferringToAssembly = new Dictionary<string, List<Assembly>>();
            foreach (Assembly assembly in TypeScanner.GetOwnAssemblies())
            {
                if (assemblyNameNotIn != null && assemblyNameNotIn.Contains(assembly.GetName().Name))
                    continue;

                foreach (AssemblyName assemblyName in assembly.GetReferencedAssemblies())
                {
                    if (!assembliesReferringToAssembly.ContainsKey(assemblyName.FullName))
                        assembliesReferringToAssembly.Add(assemblyName.FullName, new List<Assembly>());
                    assembliesReferringToAssembly[assemblyName.FullName].Add(assembly);
                }
            }

            // Choose [MetaSerializable], and all assemblies referring to that transitively.
            Dictionary<string, Assembly> visibleAssemblies = new Dictionary<string, Assembly>();
            Queue<Assembly> queue = new Queue<Assembly>();
            Assembly root = typeof(MetaSerializableAttribute).Assembly;
            queue.Enqueue(root);

            while (queue.TryDequeue(out Assembly visibleAssembly))
            {
                // If already visited, next
                if (visibleAssemblies.ContainsKey(visibleAssembly.FullName))
                    continue;
                visibleAssemblies.Add(visibleAssembly.FullName, visibleAssembly);

                // New assembly, walk to all assemblies depending on this
                if (assembliesReferringToAssembly.TryGetValue(visibleAssembly.FullName, out List<Assembly> referrers))
                {
                    foreach (Assembly referrer in referrers)
                        queue.Enqueue(referrer);
                }
            }

            return visibleAssemblies.Values.ToArray();
        }

        protected virtual bool ShouldSkipTypeInScanning(Type type)
        {
            return false;
        }

        void ScanTypes(string[] sharedNamespaces, string[] assemblyNameNotIn = null)
        {
            //Console.WriteLine("Scanning for all serializable types..");
            Assembly[] assemblies = GetRelevantAssemblies(assemblyNameNotIn);

            using (ProfilerScope.Create("MetaSerializerTypeScanner.RegisterTypes"))
            {
                // Register all types from relevant assemblies
                foreach (Assembly assembly in assemblies)
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (ShouldSkipTypeInScanning(type))
                            continue;

                        // Skip generic types (only supported when parameters given, as members in other classes/structs)
                        if (type.IsGenericType)
                            continue;

                        // Must have MetaSerializable attribute
                        MetaSerializableAttribute serializableAttrib = TryGetMetaSerializableAttribute(type);
                        if (serializableAttrib == null)
                            continue;

                        // Resolve whether type is public based on namespace
                        bool isPublicType = type.Namespace != null && IsPublicNamespace(type.Namespace, sharedNamespaces);

                        // Register type
                        MetaSerializableType spec = TryRegisterType(type);

                        // Mark the type as public
                        if (isPublicType && spec != null)
                            spec.IsPublic = true;
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ScanTypeProviders"))
            {
                // Scan [MetaSerializableTypeProvider] classes and their [MetaSerializableTypeGetter] methods,
                // and register the types reported by those methods.
                // \todo [nuutti] Whether this mechanism is the best way to achieve what we want for the config patching
                //                is unsure. Investigate other options. In any case, clean this up a bit. #config-patch
                foreach (Assembly assembly in assemblies)
                {
                    foreach (Type providerType in assembly.GetTypes())
                    {
                        if (ShouldSkipTypeInScanning(providerType))
                            continue;

                        if (providerType.IsGenericType)
                            continue;

                        MetaSerializableTypeProviderAttribute typeProviderAttribute = _attrCache.TryGetSealedAttributeOnTypeOrAncestor<MetaSerializableTypeProviderAttribute>(providerType);
                        if (typeProviderAttribute == null)
                            continue;

                        IEnumerable<MethodInfo> typeGetterMethods =
                            GetAncestorsBasemostFirst(providerType)
                                .SelectMany(ancestor => ancestor.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                .Where(method => _attrCache.HasSealedAttributeOnMethod<MetaSerializableTypeGetterAttribute>(method));

                        foreach (MethodInfo typeGetter in typeGetterMethods)
                        {
                            if (!typeGetter.IsStatic)
                                throw new InvalidOperationException($"[MetaSerializableTypeGetter] method must be static, but {typeGetter.ToMemberWithGenericDeclaringTypeString()} isn't");
                            if (typeGetter.GetParameters().Length != 0)
                                throw new InvalidOperationException($"[MetaSerializableTypeGetter] method cannot take parameters, but {typeGetter.ToMemberWithGenericDeclaringTypeString()} does");
                            if (!typeof(IEnumerable<Type>).IsAssignableFrom(typeGetter.ReturnType))
                                throw new InvalidOperationException($"[MetaSerializableTypeGetter] method must return a type assignable to IEnumerable<Type>, but {typeGetter.ToMemberWithGenericDeclaringTypeString()} returns {typeGetter.ReturnType.ToGenericTypeString()}");

                            IEnumerable<Type> typesToRegister = (IEnumerable<Type>)typeGetter.InvokeWithoutWrappingError(obj: null, parameters: null);
                            foreach (Type typeToRegister in typesToRegister)
                            {
                                bool isPublicType = typeToRegister.Namespace != null && IsPublicNamespace(typeToRegister.Namespace, sharedNamespaces);
                                MetaSerializableType spec = TryRegisterType(typeToRegister);
                                // Mark the type as public
                                if (isPublicType && spec != null)
                                    spec.IsPublic = true;
                            }
                        }
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.CheckIgnoredAssemblies"))
            {
                if (assemblyNameNotIn != null)
                {
                    foreach (KeyValuePair<Type, MetaSerializableType> metaSerializableType in _typeSpecs)
                    {
                        if (assemblyNameNotIn.Contains(metaSerializableType.Value.Type.Assembly.GetName().Name))
                        {
                            MetaDebug.AssertFail("Type {0} is defined in an ignored assembly {1}, but non-ignored assembly depends on it.", metaSerializableType.Key.ToNamespaceQualifiedTypeString(), metaSerializableType.Value.Type.Assembly.GetName().Name);
                        }
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveClassTypeCodes"))
            {
                // Resolve typeCodes for all derived classes
                Dictionary<Type, Dictionary<int, Type>> abstractClassChildTypes = new Dictionary<Type, Dictionary<int, Type>>();
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    // Handle custom attributes with behavior overrides
                    ISerializableTypeCodeProvider typeCodeProvider = null;
                    foreach (ISerializableTypeCodeProvider attr in _attrCache.GetDerivedAttributesOnTypeOrAncestor<ISerializableTypeCodeProvider>(type))
                    {
                        typeCodeProvider = attr;
                    }

                    // Abstract types cannot have TypeCodeProvider ([MetaSerializableDerived])
                    if (typeCodeProvider != null && type.IsAbstract)
                        throw new InvalidOperationException($"Abstract type {type.ToGenericTypeString()} specifies TypeCode -- TypeCodes are only allowed on non-abstract classes");

                    // Store typeCode (if has provider)
                    if (typeCodeProvider != null)
                    {
                        // Store typeCode for given type
                        int typeCode = typeCodeProvider.TypeCode;
                        if (typeCode <= 0)
                            throw new InvalidOperationException($"Type code of a derived class must be positive, but {type.ToGenericTypeString()} has type code {typeCode}");
                        typeSpec.TypeCode = typeCode;

                        // Store typeCode to all base classes (assuming has any)
                        for (Type baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                        {
                            if (baseType == typeof(object) || baseType == typeof(Exception))
                                continue;

                            MetaDebug.Assert(baseType.IsAbstract, "Non-abstract classes can only derive from abstract classes: {0} cannot derive from {1}", type.Name, baseType.Name);

                            if (TryGetMetaSerializableAttribute(baseType) != null)
                                RegisterChildTypeForBase(abstractClassChildTypes, baseType, typeCode, type);
                        }

                        // Store typeCode to serializable interfaces
                        foreach (Type interfaceType in type.GetInterfaces())
                        {
                            if (TryGetMetaSerializableAttribute(interfaceType) != null)
                                RegisterChildTypeForBase(abstractClassChildTypes, interfaceType, typeCode, type);
                        }
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveMembers"))
            {
                // Resolve all non-abstract class/struct members
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    // Process members (fields/props) of non-abstract classes/structs
                    if (typeSpec.IsConcreteObject)
                    {
                        // Resolve the members that are MetaMembers.
                        bool             useImplicitMembers = typeSpec.UsesImplicitMembers;

                        List<MemberInfo> metaMembers           = null;
                        bool             hasTypeMemberOverride = TypeMemberOverrides.TryGetValue(typeSpec.Type, out metaMembers);
                        if (!typeSpec.IsTuple && !hasTypeMemberOverride)
                             metaMembers = ResolveTypeMetaMembers(type, useImplicitMembers);

                        // If using implicit members, resolve the tag ranges for the
                        // types in the class hierarchy, and set up a running id for each.
                        Dictionary<Type, (int Start, int End)>  typeImplicitTagRanges   = null;
                        Dictionary<Type, int>                   typeImplicitRunningTags = null;
                        if (useImplicitMembers)
                        {
                            typeImplicitTagRanges = ResolveImplicitMemberTagRanges(type, metaMembers);
                            typeImplicitRunningTags = typeImplicitTagRanges.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Start);
                        }

                        bool forceImplicitMembers = false;
                        if (typeSpec.IsTuple)
                        {
                            metaMembers          = GetTupleMembers(type).ToList();
                            forceImplicitMembers = true;
                        }
                        else if (hasTypeMemberOverride)
                        {
                            forceImplicitMembers = true;
                        }

                        if (forceImplicitMembers)
                        {
                            // Use implicit tagIds as the user can't define the order
                            useImplicitMembers    = true;
                            typeImplicitTagRanges = new Dictionary<Type, (int Start, int End)>() {{type, (1, metaMembers.Count + 1)}};
                            typeImplicitRunningTags = typeImplicitTagRanges.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Start);
                        }

                        // Create MetaSerializableMembers

                        Dictionary<int, string>         tagToMemberName = new Dictionary<int, string>();
                        List<MetaSerializableMember>    memberSpecs     = new List<MetaSerializableMember>();

                        foreach (MemberInfo metaMember in metaMembers)
                        {
                            // Create member spec (implicit members overrides custom attrib)
                            MetaMemberAttribute     memberAttrib    = _attrCache.TryGetSealedAttributeOnMember<MetaMemberAttribute>(metaMember);
                            Func<object, object>    getValue        = metaMember.GetDataMemberGetValueOnDeclaringType();
                            Action<object, object>  setValue        = metaMember.GetDataMemberSetValueOnDeclaringType();

                            MetaDebug.Assert(getValue != null, "[MetaMember] {0}.{1} does not have a getter", typeSpec.Name, metaMember.Name);
                            MetaDebug.Assert(setValue != null || typeSpec.UsesConstructorDeserialization, "[MetaMember] {0}.{1} does not have a setter", typeSpec.Name, metaMember.Name);

                            if (metaMember is FieldInfo field && field.IsInitOnly && !typeSpec.UsesConstructorDeserialization)
                                throw new InvalidOperationException($"[MetaMember] Field {typeSpec.Name}.{field.Name} is readonly, this is only supported using constructor based deserialization! Make it a property with {{ get; private set; }} or enable constructor based deserialization (use either [MetaDeserializationConstructor] or [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]).");

                            // Resolve tag id and flags: either implicit or from attributes

                            int             memberTagId;
                            MetaMemberFlags memberFlags;

                            if (useImplicitMembers)
                            {
                                memberTagId = typeImplicitRunningTags[metaMember.DeclaringType]++;

                                // Combining implicit/explicit tags: Allowed if they both result in the exact same member tag.
                                if (memberAttrib != null && memberAttrib.TagId != memberTagId)
                                {
                                    MetaDebug.AssertFail("{0} member {1} has ambiguous TagId. MetaMember resolves to {2}, implicit numbering resolves to {3}. TagIds must match.", type.Name, metaMember.Name, memberAttrib.TagId, memberTagId);
                                }

                                (int Start, int End) range = typeImplicitTagRanges[metaMember.DeclaringType];
                                if (memberTagId >= range.End)
                                    throw new InvalidOperationException($"{type.ToGenericTypeString()} has too many members for its [MetaImplicitMembersRange]: range is ({range.Start}, {range.End}), but {metaMember.ToMemberWithGenericDeclaringTypeString()} would get tag {memberTagId}");

                                memberFlags = MetaMemberFlags.None;
                            }
                            else
                            {
                                memberTagId = memberAttrib.TagId;
                                memberFlags = ResolveDataMemberFlags(metaMember, memberAttrib);
                            }

                            MetaDebug.Assert(memberTagId > 0, "Member {0} has nonpositive tag id - this should've been checked already", metaMember.Name);

                            // Guard against duplicate tag ids
                            if (tagToMemberName.ContainsKey(memberTagId))
                                MetaDebug.AssertFail("Duplicate [MetaMember({0})] tag in {1}: {2} and {3}", memberTagId, type.Name, metaMember.Name, tagToMemberName[memberTagId]);
                            tagToMemberName.Add(memberTagId, metaMember.Name);

                            // Append member spec
                            MaxCollectionSizeAttribute maxCollectionSizeAttribute   = _attrCache.TryGetSealedAttributeOnMember<MaxCollectionSizeAttribute>(metaMember);
                            AddedInVersionAttribute         addedInVersion   = _attrCache.TryGetSealedAttributeOnMember<AddedInVersionAttribute>(metaMember);
                            RemovedInVersionAttribute       removedInVersion = _attrCache.TryGetSealedAttributeOnMember<RemovedInVersionAttribute>(metaMember);
                            string                          globalNamespaceQualifiedTypeString;
                            Type                            memberDataType = metaMember.GetDataMemberType();
                            if (TryGetTypeSpec(memberDataType, out MetaSerializableType memberSpec))
                                globalNamespaceQualifiedTypeString = memberSpec.GlobalNamespaceQualifiedTypeString;
                            else
                                globalNamespaceQualifiedTypeString = memberDataType.ToGlobalNamespaceQualifiedTypeString();


                            int maxCollectionSize = maxCollectionSizeAttribute?.Size ?? MetaSerializationContext.DefaultMaxCollectionSize;
                            MetaSerializableMember.SerializerGenerationOnlyInfo serializerInfo = new MetaSerializableMember.SerializerGenerationOnlyInfo(
                                globalNamespaceQualifiedTypeString: globalNamespaceQualifiedTypeString,
                                addedInLogicVersion: addedInVersion?.LogicVersion,
                                removedInLogicVersion: removedInVersion?.LogicVersion,
                                maxCollectionSize: maxCollectionSize);

                            memberSpecs.Add(new MetaSerializableMember(memberTagId, memberFlags, metaMember, serializerInfo));
                        }

                        // Sort by member tag id.
                        // This determines the order in which the members are serialized. Sorting by tag id
                        // ensures checksum compatibility if members are reordered or moved between base and
                        // derived, as long as explicit member tag ids are used.
                        memberSpecs = memberSpecs.OrderBy(member => member.TagId).ToList();

                        // Store members
                        typeSpec.Members = memberSpecs;
                        typeSpec.MemberByTagId = memberSpecs.ToDictionary(member => member.TagId);
                        typeSpec.MemberByName = memberSpecs.ToDictionary(member => member.Name);
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveOnDeserialized"))
            {
                // Resolve all non-abstract class/struct [MetaOnDeserialized]-marked methods
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    if (typeSpec.IsConcreteObject)
                    {
                        List<MethodInfo> onDeserializedMethods = ResolveTypeMetaOnDeserializedMethods(type);

                        // Some validation
                        foreach (MethodInfo method in onDeserializedMethods)
                        {
                            if (method.IsStatic)
                                throw new InvalidOperationException($"[MetaOnDeserialized] methods cannot be static, but {method.ToMemberWithGenericDeclaringTypeString()} is");
                            IEnumerable<Type> paramTypes = method.GetParameters().Select(p => p.ParameterType);
                            bool paramsOk = paramTypes.Count() == 0
                                         || paramTypes.SequenceEqual(new Type[]{ typeof(MetaOnDeserializedParams) });
                            if (!paramsOk)
                                throw new InvalidOperationException($"[MetaOnDeserialized] methods must take either no parameters, or exactly one parameter which must be of type {nameof(MetaOnDeserializedParams)}, but {method.ToMemberWithGenericDeclaringTypeString()} takes {string.Join(", ", paramTypes.Select(p => p.ToGenericTypeString()))}");
                            if (method.ReturnType != typeof(void))
                                throw new InvalidOperationException($"[MetaOnDeserialized] methods must return void, but {method.ToMemberWithGenericDeclaringTypeString()} returns {method.ReturnType.ToGenericTypeString()}");
                        }

                        typeSpec.OnDeserializedMethods = onDeserializedMethods;
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveOnMemberDeserializationFailure"))
            {
                // Resolve all non-abstract class/struct [MetaOnMemberDeserializationFailure] method references on members
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    if (typeSpec.IsConcreteObject)
                    {
                        foreach (MetaSerializableMember memberSpec in typeSpec.Members)
                        {
                            MemberInfo                                  member              = memberSpec.MemberInfo;
                            MetaOnMemberDeserializationFailureAttribute onFailureAttribute  = _attrCache.TryGetSealedAttributeOnMember<MetaOnMemberDeserializationFailureAttribute>(member);
                            if (onFailureAttribute == null)
                                continue;

                            string      methodName  = onFailureAttribute.MethodName;
                            MethodInfo  method      = member.DeclaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (method == null)
                                throw new InvalidOperationException($"[MetaOnMemberDeserializationFailure] method {methodName} on {member.ToMemberWithGenericDeclaringTypeString()} not found in {member.DeclaringType.ToGenericTypeString()}");
                            if (!method.IsStatic)
                                throw new InvalidOperationException($"[MetaOnMemberDeserializationFailure] methods must be static, but {methodName} on {member.ToMemberWithGenericDeclaringTypeString()} isn't");
                            if (method.ContainsGenericParameters)
                                throw new InvalidOperationException($"[MetaOnMemberDeserializationFailure] methods cannot take type parameters, but {methodName} on {member.ToMemberWithGenericDeclaringTypeString()} does");
                            IEnumerable<Type> paramTypes = method.GetParameters().Select(p => p.ParameterType);
                            if (!paramTypes.SequenceEqual(new Type[]{ typeof(MetaMemberDeserializationFailureParams) }))
                                throw new InvalidOperationException($"[MetaOnMemberDeserializationFailure] methods must take exactly one parameter and it must be of type {nameof(MetaMemberDeserializationFailureParams)}, but {methodName} on {member.ToMemberWithGenericDeclaringTypeString()} takes {string.Join(", ", paramTypes.Select(p => p.ToGenericTypeString()))}");
                            if (!member.GetDataMemberType().IsAssignableFrom(method.ReturnType))
                                throw new InvalidOperationException($"[MetaOnMemberDeserializationFailure] methods must return type that is assignable to the member's type, but {methodName} on {member.ToMemberWithGenericDeclaringTypeString()} returns {method.ReturnType.ToGenericTypeString()} which is not assignable to {member.GetDataMemberType().ToGenericTypeString()}");

                            memberSpec.OnDeserializationFailureMethod = method;
                        }
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveGameConfigNullSentinel"))
            {
                // For IGameConfigData types, resolve null sentinel keys.
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    if (typeSpec.IsGameConfigData)
                    {
                        MemberInfo[] sentinelMembers = type.GetMember("ConfigNullSentinelKey", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        if (sentinelMembers.Length == 0)
                            continue;

                        Type configKeyType = type.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0];
                        if (configKeyType.CanBeNull())
                            throw new InvalidOperationException($"ConfigNullSentinelKey should only be defined for game config data types where the key type is not nullable: {type.ToGenericTypeString()} has nullable key type {configKeyType.ToGenericTypeString()}");

                        if (sentinelMembers.Length != 1)
                            throw new InvalidOperationException($"Multiple ConfigNullSentinelKey members found in {typeSpec.Name}");

                        MemberInfo sentinelMember = sentinelMembers.Single();

                        // \todo [nuutti] The checks here are redundant since the errors would be caught at init time anyway.
                        //       The purpose of these checks is only to produce more useful error messages.
                        //       Should maybe add some helpers for this kind of checking.

                        if (!(sentinelMember.MemberType == MemberTypes.Property || sentinelMember.MemberType == MemberTypes.Field))
                            throw new InvalidOperationException($"ConfigNullSentinelKey must be a property or a field, but {sentinelMember.ToMemberWithGenericDeclaringTypeString()} is {sentinelMember.MemberType}");

                        if (!configKeyType.IsAssignableFrom(sentinelMember.GetDataMemberType()))
                            throw new InvalidOperationException($"ConfigNullSentinelKey must be assignable to the config key type: {sentinelMember.ToMemberWithGenericDeclaringTypeString()} is of type {sentinelMember.GetDataMemberType().ToGenericTypeString()} which is not assignable to {configKeyType.ToGenericTypeString()}");

                        if (sentinelMember is PropertyInfo sentinelProperty)
                        {
                            MethodInfo getter = sentinelProperty.GetGetMethod(nonPublic: true);
                            if (getter == null)
                                throw new InvalidOperationException($"ConfigNullSentinelKey must have a getter: {sentinelMember.ToMemberWithGenericDeclaringTypeString()}");
                            if (!getter.IsStatic)
                                throw new InvalidOperationException($"ConfigNullSentinelKey must be static: {sentinelMember.ToMemberWithGenericDeclaringTypeString()}");
                        }
                        else if (sentinelMember is FieldInfo sentinelField)
                        {
                            if (!sentinelField.IsStatic)
                                throw new InvalidOperationException($"ConfigNullSentinelKey must be static: {sentinelMember.ToMemberWithGenericDeclaringTypeString()}");
                        }

                        typeSpec.ConfigNullSentinelKey = sentinelMember.GetDataMemberGetValueOnDeclaringType()(null);
                    }
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.ResolveDeserializationConverters"))
            {
                // Resolve deserialization converters
                foreach ((Type type, MetaSerializableType typeSpec) in _typeSpecs)
                {
                    IEnumerable<MetaDeserializationConverterAttributeBase> converterAttributes = _attrCache.GetDerivedAttributesOnExactType<MetaDeserializationConverterAttributeBase>(type);
                    if (!converterAttributes.Any())
                    {
                        typeSpec.DeserializationConverters = Array.Empty<MetaDeserializationConverter>();
                        continue;
                    }

                    List<MetaDeserializationConverter> converters = new List<MetaDeserializationConverter>();
                    Dictionary<WireDataType, MetaDeserializationConverterAttributeBase> wireTypeToConverterAttribute = new Dictionary<WireDataType, MetaDeserializationConverterAttributeBase>();

                    foreach (MetaDeserializationConverterAttributeBase converterAttribute in converterAttributes)
                    {
                        converterAttribute.ValidateForTargetType(type, _typeSpecs);

                        foreach (MetaDeserializationConverter converter in converterAttribute.CreateConverters(type))
                        {
                            if (converter.AcceptedWireDataType == typeSpec.WireType)
                            {
                                throw new InvalidOperationException(
                                    $"{type.ToGenericTypeString()}: {converterAttribute} accepts wire type {converter.AcceptedWireDataType}"
                                    + " which is the same as the type's normal wire type. Wire type conflicts are not permitted.");
                            }

                            if (wireTypeToConverterAttribute.TryGetValue(converter.AcceptedWireDataType, out MetaDeserializationConverterAttributeBase otherAttribute))
                            {
                                throw new InvalidOperationException(
                                    $"{type.ToGenericTypeString()}: {converterAttribute} and {otherAttribute} both accept wire type {converter.AcceptedWireDataType}."
                                    + " Wire type conflicts are not permitted.");
                            }

                            if (converter.SourceType == converter.TargetType
                                && converter.SourceDeserialization == MetaDeserializationConverter.SourceDeserializationKind.Normal)
                            {
                                throw new InvalidOperationException($"{type.ToGenericTypeString()}: {converterAttribute} has source identical to target");
                            }

                            if (!type.IsAssignableFrom(converter.TargetType))
                                throw new InvalidOperationException($"{type.ToGenericTypeString()}: {converterAttribute} has incompatible target type {converter.TargetType}");

                            converters.Add(converter);
                            wireTypeToConverterAttribute.Add(converter.AcceptedWireDataType, converterAttribute);
                        }
                    }

                    typeSpec.DeserializationConverters = converters.ToArray();
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.PropagatePublic"))
            {
                // Propagate Public flag to members of Public types
                foreach (MetaSerializableType typeSpec in _typeSpecs.Values)
                {
                    if (typeSpec.IsPublic)
                        PropagatePublic(typeSpec);
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.FindConstructor"))
            {
                foreach (MetaSerializableType typeSpec in _typeSpecs.Values)
                {
                    if (typeSpec.UsesConstructorDeserialization)
                        typeSpec.DeserializationConstructor = typeSpec.FindBestMatchingConstructor();
                }
            }

            using (ProfilerScope.Create("MetaSerializerTypeScanner.Validate"))
            {
                // Final validation of types
                foreach (MetaSerializableType typeSpec in _typeSpecs.Values)
                    ValidateTypeSpec(typeSpec);
            }

            // Dump all types (and whether they're public or not)
            //List<MetaSerializableType> sorted = _typeSpecs.Values.OrderBy(spec => spec.Type.FullName).ToList();
            //foreach (MetaSerializableType typeSpec in sorted)
            //{
            //    Type type = typeSpec.Type;
            //    bool isPublic = typeSpec.Flags.HasFlag(MetaSerializableFlags.Public);
            //    DebugLog.Info("{0}{1}{2} {3}", isPublic ? "public " : "", typeSpec.IsCustomSerialized ? "custom " : "", type.Namespace, type.ToGenericTypeString());
            //    if (typeSpec.Members != null)
            //    {
            //        foreach (MetaSerializableMember member in typeSpec.Members)
            //            DebugLog.Info("  #{0}: {1} : {2}", member.TagId, member.Name, member.Type.ToGenericTypeString());
            //    }
            //}
        }

        /// <summary>
        /// Resolve ancestors of rootType (including rootType itself), basemost first.
        /// </summary>
        static List<Type> GetAncestorsBasemostFirst(Type rootType)
        {
            List<Type> ancestors = new List<Type>();
            for (Type ancestor = rootType; ancestor != null; ancestor = ancestor.BaseType)
                ancestors.Add(ancestor);
            ancestors.Reverse();

            return ancestors;
        }

        List<MemberInfo> GetTupleMembers(Type type)
        {
            List<MemberInfo> members = new List<MemberInfo>();
            for (int i = 0; i < type.GenericTypeArguments.Length; i++)
            {
                string    fieldName = Invariant($"Item{i + 1}");
                MemberInfo info = type.GetField(fieldName);
                if (info == null)
                    info = type.GetProperty(fieldName);

                if (info == null)
                    throw new InvalidOperationException($"Unable to find Tuple member '{fieldName}' in '{type.FullName}'.");

                members.Add(info);
            }

            return members;
        }

        /// <summary>
        /// Resolve the MetaMembers of <paramref name="rootType"/>, including all of
        /// the types in the class hierarchy. Members are included regardless of
        /// their accessibility levels; in particular, the privates of base classes
        /// are included as well.
        ///
        /// A member is a MetaMember if it is a data member (i.e. property or field),
        /// and either <paramref name="useImplicitMembers"/> is true or the member
        /// has <see cref="MetaMemberAttribute"/>.
        ///
        /// In the returned list, the members are ordered like this:
        /// Primarily they are ordered by their declaring type, such that the basemost
        /// declaring type comes first, and <paramref name="rootType"/> comes last.
        /// Secondarily, properties come before fields.
        /// Tertiarily, the members are in whatever order <see cref="Type.GetProperties"/>
        /// and <see cref="Type.GetFields"/> return them; this order is technically
        /// unspecified, but in practice, this appears to be the declaration order of
        /// the members (in cases where an obvious declaration order exists).
        ///
        /// Additionally this method does some validation that MetaMembers are not
        /// used in troublesome ways:
        /// - A MetaMember member must not be overridable: if a MetaMember is virtual,
        ///   it must be sealed (i.e. final). This avoids unclear cases where a member
        ///   gets its MetaMemberness from two different definitions, one in a base
        ///   and another in a derived class.
        /// - A MetaMember cannot have the same name as another member (other than
        ///   the other definitions of the same member, in the case of virtuals).
        /// </summary>
        List<MemberInfo> ResolveTypeMetaMembers(Type rootType, bool useImplicitMembers)
        {
            // Resolve MetaMembers among the data members of the ancestors.
            // Also keep track of name -> member mapping, so that we can validate
            // that no MetaMember shares its name with another member.

            List<MemberInfo>                metaMembers     = new List<MemberInfo>();
            Dictionary<string, MemberInfo>  memberByName    = new Dictionary<string, MemberInfo>();

            foreach (Type ancestor in GetAncestorsBasemostFirst(rootType))
            {
                IEnumerable<MemberInfo> ancestorDataMembers =
                    ancestor.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Cast<MemberInfo>()
                    .Concat(ancestor.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

                foreach (MemberInfo dataMember in ancestorDataMembers)
                {
                    bool        hasMetaMemberAttribute          = _attrCache.HasSealedAttributeOnMember<MetaMemberAttribute>(dataMember);
                    bool        hasIgnoreDataMemberAttribute    = _attrCache.HasSealedAttributeOnMember<IgnoreDataMemberAttribute>(dataMember);
                    bool        hasCompilerGeneratedAttribute   = _attrCache.HasSealedAttributeOnMember<CompilerGeneratedAttribute>(dataMember);
                    bool        memberIsIgnored                 = hasIgnoreDataMemberAttribute || hasCompilerGeneratedAttribute;

                    if (hasMetaMemberAttribute && hasIgnoreDataMemberAttribute)
                        throw new InvalidOperationException($"[MetaMember] should not have [IgnoreDataMember]: {dataMember.ToMemberWithGenericDeclaringTypeString()}");
                    if (hasMetaMemberAttribute && hasCompilerGeneratedAttribute)
                        throw new InvalidOperationException($"[MetaMember] should not have [CompilerGenerated]: {dataMember.ToMemberWithGenericDeclaringTypeString()}");

                    bool isMetaMember = !memberIsIgnored && (useImplicitMembers || hasMetaMemberAttribute);

                    if (isMetaMember)
                    {
                        if (dataMember.DataMemberIsOverridable())
                        {
                            bool isOverride = dataMember != dataMember.GetDataMemberBaseDefinition();
                            if (isOverride) // More helpful error message when we're dealing with an override instead of the base definition
                                throw new InvalidOperationException($"[MetaMember] overrides must be sealed: {dataMember.ToMemberWithGenericDeclaringTypeString()}");
                            else
                                throw new InvalidOperationException($"[MetaMember] is not supported on non-sealed abstract/virtual members: {dataMember.ToMemberWithGenericDeclaringTypeString()}");
                        }

                        if (memberByName.TryGetValue(dataMember.Name, out MemberInfo namesake))
                        {
                            if (dataMember.GetDataMemberBaseDefinition() != namesake.GetDataMemberBaseDefinition())
                                throw new InvalidOperationException($"[MetaMember] {dataMember.ToMemberWithGenericDeclaringTypeString()} has the same name as member {namesake.ToMemberWithGenericDeclaringTypeString()}, which is not supported");
                        }

                        metaMembers.Add(dataMember);
                    }
                    else
                    {
                        if (memberByName.TryGetValue(dataMember.Name, out MemberInfo namesake))
                        {
                            bool namesakeIsMetaMember = useImplicitMembers || _attrCache.HasSealedAttributeOnMember<MetaMemberAttribute>(namesake);

                            if (namesakeIsMetaMember)
                            {
                                if (namesake.GetDataMemberBaseDefinition() != dataMember.GetDataMemberBaseDefinition())
                                    throw new InvalidOperationException($"[MetaMember] {namesake.ToMemberWithGenericDeclaringTypeString()} has the same name as member {dataMember.ToMemberWithGenericDeclaringTypeString()}, which is not supported");
                            }
                            else
                            {
                                // Non-MetaMembers are allowed to have same names among themselves.
                            }
                        }
                    }

                    memberByName[dataMember.Name] = dataMember;
                }
            }

            return metaMembers;
        }

        /// <summary>
        /// Resolve the MetaOnDeserialized methods of <paramref name="rootType"/>,
        /// including all of the types in the class hierarchy.
        ///
        /// This is analogous to <see cref="ResolveTypeMetaMembers"/>.
        /// It uses the same class hierarchy order, and same restrictions on
        /// virtuals and name duplicates.
        /// </summary>
        // \todo This is modified copypaste from ResolveTypeMetaMembers,
        //       maybe should unify.
        List<MethodInfo> ResolveTypeMetaOnDeserializedMethods(Type rootType)
        {
            // Resolve MetaOnDeserialized methods among the methods of the ancestors.
            // Also keep track of name -> method mapping, so that we can validate
            // that no MetaOnDeserialized method shares its name with another method.

            List<MethodInfo>                onDeserializedMethods   = new List<MethodInfo>();
            Dictionary<string, MethodInfo>  methodByName            = new Dictionary<string, MethodInfo>();

            foreach (Type ancestor in GetAncestorsBasemostFirst(rootType))
            {
                IEnumerable<MethodInfo> ancestorMethods = ancestor.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (MethodInfo method in ancestorMethods)
                {
                    bool isOnDeserializedMethod = _attrCache.HasSealedAttributeOnMethod<MetaOnDeserializedAttribute>(method);

                    if (isOnDeserializedMethod)
                    {
                        if (method.MethodIsOverridable())
                        {
                            bool isOverride = method != method.GetDataMemberBaseDefinition();
                            if (isOverride) // More helpful error message when we're dealing with an override instead of the base definition
                               throw new InvalidOperationException($"[MetaOnDeserialized] overrides must be sealed: {method.ToMemberWithGenericDeclaringTypeString()}");
                            else
                               throw new InvalidOperationException($"[MetaOnDeserialized] is not supported on non-sealed abstract/virtual methods: {method.ToMemberWithGenericDeclaringTypeString()}");
                        }

                        if (methodByName.TryGetValue(method.Name, out MethodInfo namesake))
                        {
                            if (method.GetBaseDefinition() != namesake.GetBaseDefinition())
                                throw new InvalidOperationException($"[MetaOnDeserialized] method {method.ToMemberWithGenericDeclaringTypeString()} has the same name as method {namesake.ToMemberWithGenericDeclaringTypeString()}, which is not supported");
                        }

                        onDeserializedMethods.Add(method);
                    }
                    else
                    {
                        if (methodByName.TryGetValue(method.Name, out MethodInfo namesake))
                        {
                            bool namesakeIsOnDeserializedMethod = _attrCache.HasSealedAttributeOnMethod<MetaOnDeserializedAttribute>(namesake);

                            if (namesakeIsOnDeserializedMethod)
                            {
                                if (namesake.GetBaseDefinition() != method.GetBaseDefinition())
                                    throw new InvalidOperationException($"[MetaOnDeserialized] method {namesake.ToMemberWithGenericDeclaringTypeString()} has the same name as method {method.ToMemberWithGenericDeclaringTypeString()}, which is not supported");
                            }
                            else
                            {
                                // Non-MetaOnDeserialized methods are allowed to have same names among themselves.
                            }
                        }
                    }

                    methodByName[method.Name] = method;
                }
            }

            return onDeserializedMethods;
        }

        /// <summary>
        /// Resolve the the member tag id ranges to use for each of the declaring types
        /// among metaMembers, for when using implicit members.
        /// Additionally, a range is resolved for <paramref name="rootType"/> regardless
        /// of whether it is the declaring type of any of the members.
        /// Each type gets its id range from a <see cref="MetaImplicitMembersRangeAttribute"/>,
        /// except that the concrete (most-derived) type (i.e. rootType) is allowed to omit the attribute,
        /// in which case it gets its range from a <see cref="MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute"/>
        /// defined on any base class or interface.
        /// </summary>
        Dictionary<Type, (int Start, int End)> ResolveImplicitMemberTagRanges(Type rootType, IEnumerable<MemberInfo> metaMembers)
        {
            // Find ranges for all types that appear as declaring types in metaMembers

            Dictionary<Type, (int, int)> tagRanges = new Dictionary<Type, (int, int)>();

            foreach (Type currentType in metaMembers.Select(m => m.DeclaringType).Append(rootType)) // \note rootType is considered, regardless of whether it declares any members.
            {
                if (tagRanges.ContainsKey(currentType)) // Skip if already seen in metaMembers
                    continue;

                // Resolve range for ancestor

                (int Start, int End) range;

                MetaImplicitMembersRangeAttribute rangeAttr = _attrCache.TryGetSealedAttributeOnExactType<MetaImplicitMembersRangeAttribute>(currentType);
                if (rangeAttr != null)
                {
                    range = (rangeAttr.StartIndex, rangeAttr.EndIndex);
                }
                else if (currentType != rootType)
                {
                    throw new InvalidOperationException(
                        $"Type {rootType.ToGenericTypeString()} is declared using ImplicitMembers which requires that all ancestor classes must, if they declare any field, be annotated with [{nameof(MetaImplicitMembersRangeAttribute)}]. "
                        + $"An ancestor class {currentType.ToGenericTypeString()} declares a field and but has no [{nameof(MetaImplicitMembersRangeAttribute)}] annotation. "
                        + "Add the annotation to allocate a distinct range in the ancestor. Range allocation is required to avoid ID allocations from interfering with each other.");
                }
                else
                {
                    MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute defaultRangeAttr = _attrCache.TryGetSealedAttributeOnTypeOrAncestor<MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute>(currentType);
                    if (defaultRangeAttr == null)
                    {
                        foreach (Type iFace in rootType.GetInterfaces())
                        {
                            defaultRangeAttr = _attrCache.TryGetSealedAttributeOnExactType<MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute>(iFace);
                            if (defaultRangeAttr != null)
                                break;
                        }
                    }

                    if (defaultRangeAttr == null)
                        throw new InvalidOperationException($"Using ImplicitMembers for {rootType.ToGenericTypeString()}, but it doesn't have {nameof(MetaImplicitMembersRangeAttribute)} and there's no {nameof(MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute)} on any base class or interface");

                    range = (defaultRangeAttr.StartIndex, defaultRangeAttr.EndIndex);
                }

                // Check the range doesn't overlap with any other

                foreach ((Type otherType, (int Start, int End) otherRange) in tagRanges)
                {
                    if (range.Start < otherRange.End && otherRange.Start < range.End)
                    {
                        throw new InvalidOperationException(
                            $"Ranges for implicit member tag ids overlap between {currentType.ToGenericTypeString()} and {otherType.ToGenericTypeString()}:"
                            + Invariant($" ({range.Start}, {range.End}) and ({otherRange.Start}, {otherRange.End})"));

                    }
                }

                // Add

                tagRanges.Add(currentType, range);
            }

            return tagRanges;
        }

        static uint ComputeTypeHash(MetaSerializableType typeSpec)
        {
            // Compute hash for type
            string typeName = typeSpec.Name;
            uint typeHash = GetStringHash(typeName) + 117 * (uint)typeSpec.TypeCode;

            // Hash all members (for object types)
            if (typeSpec.Members != null)
            {
                foreach (MetaSerializableMember member in typeSpec.Members)
                {
                    if (!member.Flags.HasFlag(MetaMemberFlags.Hidden))
                        typeHash = typeHash * 17 + GetStringHash(member.Name) + GetStringHash(member.Type.ToGenericTypeString()) + (uint)member.TagId;
                }
            }

            return typeHash;
        }

        static uint ComputeProtocolHash(IEnumerable<MetaSerializableType> typeSpecs)
        {
            uint fullProtocolHash = 0;

            // Iterate all non-internal types in sorted order
            List<MetaSerializableType> sortedTypes = typeSpecs
                .Where(typeSpec => typeSpec.IsPublic)
                .OrderBy(typeSpec => typeSpec.Type.ToNamespaceQualifiedTypeString(), StringComparer.InvariantCulture)
                .ToList();
            for (int typeNdx = 0; typeNdx < sortedTypes.Count; typeNdx++)
            {
                MetaSerializableType typeSpec = sortedTypes[typeNdx];
                uint typeHash = ComputeTypeHash(typeSpec);
                fullProtocolHash = fullProtocolHash * 13 + typeHash;
                //DebugLog.Debug("Hash for #{0} {1}: 0x{2:X8} (cumulative=0x{3:X8}) [flags={4}]", typeNdx, typeSpec.Name, typeHash, fullProtocolHash, typeSpec.Flags);
            }

            return fullProtocolHash;
        }

        public MetaSerializerTypeInfo GetTypeInfo()
        {
            _typeSpecs = new Dictionary<Type, MetaSerializableType>();
            ScanTypes(MetaplayCore.Options.SharedNamespaces);
            MetaSerializerTypeInfo ret;
            ret.Specs = _typeSpecs;
            // Compute public (client-server) protocol hash
            ret.FullTypeHash = ComputeProtocolHash(_typeSpecs.Values);
            return ret;
        }
    }
}
