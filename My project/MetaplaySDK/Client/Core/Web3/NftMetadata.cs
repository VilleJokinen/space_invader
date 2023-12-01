// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Web3
{
    // See (not all of this is implemented yet!):
    //  https://docs.opensea.io/docs/metadata-standards
    //  https://github.com/ethereum/EIPs/blob/master/EIPS/eip-721.md,
    //  https://github.com/ethereum/EIPs/blob/master/EIPS/eip-1155.md#erc-1155-metadata-uri-json-schema
    //  https://docs.opensea.io/docs/metadata-standards

    public enum NftMetadataCorePropertyId
    {
        Name,
        Description,
        ImageUrl,
        // \todo #nft Rest of core properties, both IMX and OpenSea
    }

    public enum NftPropertyType
    {
        Enum,
        Text,
        Boolean,
        Number,
        // \todo #nft Other types. At least, OpenSea supports dates.
        //       Also, OpenSea has various flavors (called "display_type")
        //       for numeric attributes - should those be distinct property
        //       types here, or another dimension?
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class NftMetadataCorePropertyAttribute : Attribute
    {
        public NftMetadataCorePropertyId? Id; // If null, this is determined based on c# property/field name

        public NftMetadataCorePropertyAttribute()
        {
            Id = null;
        }

        public NftMetadataCorePropertyAttribute(NftMetadataCorePropertyId id)
        {
            Id = id;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class NftMetadataCustomPropertyAttribute : Attribute
    {
        public readonly string Name; // If null, this is determined based on c# property/field name

        public NftMetadataCustomPropertyAttribute(string name = null)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Represents the metadata schema of a type of NFT, based on the
    /// <see cref="NftMetadataCorePropertyAttribute"/> and <see cref="NftMetadataCustomPropertyAttribute"/>
    /// annotations on the members of a <see cref="MetaNft"/>-derived class.
    /// <para>
    /// Call <see cref="GenerateMetadata(MetaNft)"/> to generate the metadata
    /// as a jsonifiable object. The generated metadata is intended to be
    /// compatible with both Immutable X and OpenSea metadata specifications;
    /// in particular, the custom (i.e. non-core) metadata properties are
    /// output both as top-level json properties (for Immutable X) as well as
    /// objects inside a top-level array called `attributes` (for OpenSea).
    /// </para>
    /// <para>
    /// This can also map metadata the other way, using <see cref="ApplyMetadataToNft(MetaNft, JObject)"/>:
    /// given metadata, set the members of a <see cref="MetaNft"/> instance.
    /// </para>
    /// </summary>
    public class NftMetadataSpec
    {
        readonly List<PropertySpec> _propertySpecs;
        readonly Dictionary<NftMetadataCorePropertyId, CorePropertySpec> _corePropertySpecs;

        abstract class PropertySpec
        {
            public readonly MemberInfo Member;

            protected PropertySpec(MemberInfo member)
            {
                Member = member;
            }
        }

        class CorePropertySpec : PropertySpec
        {
            public readonly NftMetadataCorePropertyId Id;

            public CorePropertySpec(MemberInfo member, NftMetadataCorePropertyId id)
                : base(member)
            {
                Id = id;
            }
        }

        class CustomPropertySpec : PropertySpec
        {
            public readonly NftPropertyType Type;
            public readonly string Name;

            public CustomPropertySpec(MemberInfo member, NftPropertyType type, string name)
                : base(member)
            {
                Type = type;
                Name = name;
            }
        }

        public NftMetadataSpec(Type nftType)
        {
            _propertySpecs = new List<PropertySpec>();

            foreach (MemberInfo member in nftType.EnumerateInstanceDataMembersInUnspecifiedOrder())
            {
                NftMetadataCorePropertyAttribute    coreAttr   = member.GetCustomAttribute<NftMetadataCorePropertyAttribute>();
                NftMetadataCustomPropertyAttribute  customAttr = member.GetCustomAttribute<NftMetadataCustomPropertyAttribute>();

                if (coreAttr == null && customAttr == null)
                    continue;

                if (coreAttr != null && customAttr != null)
                    throw new InvalidOperationException($"{member.ToMemberWithGenericDeclaringTypeString()} has both {nameof(NftMetadataCorePropertyAttribute)} and {nameof(NftMetadataCustomPropertyAttribute)} which is not supported");

                PropertySpec propertySpec;

                if (coreAttr != null)
                    propertySpec = new CorePropertySpec(member, GetCorePropertyId(member, coreAttr));
                else
                {
                    propertySpec = new CustomPropertySpec(
                        member,
                        type: GetPropertyType(member),
                        name: customAttr.Name ?? member.Name);
                }

                _propertySpecs.Add(propertySpec);
            }

            Util.CheckPropertyDuplicates(
                _propertySpecs.OfType<CorePropertySpec>(),
                prop => prop.Id,
                (propA, propB, propId) =>
                    throw new InvalidOperationException($"{nftType.ToGenericTypeString()} has duplicate members for the core metadata property '{propId}': members {propA.Member.Name} and {propB.Member.Name}"));

            Util.CheckPropertyDuplicates(
                _propertySpecs.OfType<CustomPropertySpec>(),
                prop => prop.Name,
                (propA, propB, propName) =>
                    throw new InvalidOperationException($"{nftType.ToGenericTypeString()} has duplicate members for the custom metadata property '{propName}': members {propA.Member.Name} and {propB.Member.Name}"));

            _corePropertySpecs = _propertySpecs.OfType<CorePropertySpec>().ToDictionary(coreProperty => coreProperty.Id);
        }

        public JObject GenerateMetadata(MetaNft nft)
        {
            JObject metadata = new JObject();
            JArray openSeaAttributes = new JArray();

            foreach (PropertySpec property in _propertySpecs)
            {
                object memberValue = property.Member.GetDataMemberGetValueOnDeclaringType()(nft);
                if (memberValue == null)
                    continue;

                string propertyName = GetPropertyJsonName(property);
                NftPropertyType propertyType = GetPropertyType(property);
                bool isCustomProperty = property is CustomPropertySpec;

                JToken jsonPropertyValue = ConvertMemberValueToJsonPropertyValue(memberValue, propertyType);

                // Add top-level JSON property, for Immutable X metadata format.
                metadata.Add(propertyName, jsonPropertyValue);

                // For custom properties, add entry to top-level `attributes` array (variable openSeaAttributes), for OpenSea metadata format.
                if (isCustomProperty)
                {
                    JObject openSeaAttribute = TryGetOpenSeaAttributeForValue(memberValue, propertyName, propertyType, jsonPropertyValue);

                    if (openSeaAttribute != null)
                        openSeaAttributes.Add(openSeaAttribute);
                }
            }

            metadata.Add("attributes", openSeaAttributes);
            return metadata;
        }

        /// <summary>
        /// Populate members of <paramref name="targetNft"/> based on metadata properties in <paramref name="metadata"/>.
        /// - Unknown properties in <paramref name="metadata"/> are ignored.
        /// - Properties missing from <paramref name="metadata"/> are left unpopulated in <paramref name="targetNft"/>.
        /// - Read-only properties are ignored.
        /// </summary>
        public void ApplyMetadataToNft(MetaNft targetNft, JObject metadata)
        {
            foreach (PropertySpec property in _propertySpecs)
            {
                NftPropertyType propertyType = GetPropertyType(property);
                string propertyName = GetPropertyJsonName(property);

                // Get top-level JSON property (as in Immutable X metadata format), if any.
                JToken jsonPropertyValue = metadata.GetValue(propertyName);
                if (jsonPropertyValue == null)
                    continue;

                Action<object, object> setter = property.Member.GetDataMemberSetValueOnDeclaringType();
                if (setter == null)
                    continue;

                Type memberType = property.Member.GetDataMemberType();
                object memberValue = ConvertJsonPropertyValueToMemberValue((JValue)jsonPropertyValue, propertyType, memberType);

                setter(targetNft, memberValue);
            }
        }

        public string TryGetName(MetaNft nft)
        {
            return (string)TryGetCorePropertyValue(NftMetadataCorePropertyId.Name, nft);
        }

        public string TryGetDescription(MetaNft nft)
        {
            return (string)TryGetCorePropertyValue(NftMetadataCorePropertyId.Description, nft);
        }

        public string TryGetImageUrl(MetaNft nft)
        {
            return (string)TryGetCorePropertyValue(NftMetadataCorePropertyId.ImageUrl, nft);
        }

        object TryGetCorePropertyValue(NftMetadataCorePropertyId propertyId, MetaNft nft)
        {
            if (_corePropertySpecs.TryGetValue(propertyId, out CorePropertySpec propertySpec))
                return propertySpec.Member.GetDataMemberGetValueOnDeclaringType()(nft);
            else
                return null;
        }

        static JToken ConvertMemberValueToJsonPropertyValue(object memberValue, NftPropertyType propertyType)
        {
            object value;
            if (memberValue is IMetaRef metaRef)
                value = metaRef.KeyObject;
            else
                value = memberValue;

            switch (propertyType)
            {
                case NftPropertyType.Enum:
                    return new JValue(((Enum)value).ToString());

                case NftPropertyType.Text:
                    if (value is string str)
                        return new JValue(str);
                    else if (value is IStringId stringId)
                        return new JValue(stringId.Value);
                    else
                        throw new MemberAccessException($"Unhandled C# type for {nameof(NftPropertyType)}.{NftPropertyType.Text}: {value.GetType().ToGenericTypeString()}");

                case NftPropertyType.Boolean:
                    return new JValue((bool)value);

                case NftPropertyType.Number:
                {
                    double doubleValue;
                    if (value is F32 memberValueF32)
                        doubleValue = memberValueF32.Double;
                    else if (value is F64 memberValueF64)
                        doubleValue = memberValueF64.Double;
                    else
                        doubleValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                    return new JValue(doubleValue);
                }

                default:
                    throw new MetaAssertException("Unhandled NFT property type {PropertyType}", propertyType);
            }
        }

        static object ConvertJsonPropertyValueToMemberValue(JToken jsonPropertyValue, NftPropertyType propertyType, Type memberType)
        {
            Type type;
            if (memberType.IsGenericTypeOf(typeof(MetaRef<>)))
                type = GetMetaRefKeyType(memberType);
            else
                type = memberType;

            object value;

            switch (propertyType)
            {
                case NftPropertyType.Enum:
                {
                    string enumStr = (string)((JValue)jsonPropertyValue).Value;
                    value = Enum.Parse(type, enumStr);
                    break;
                }

                case NftPropertyType.Text:
                {
                    string str = (string)((JValue)jsonPropertyValue).Value;
                    if (type == typeof(string))
                        value = str;
                    else if (type.ImplementsInterface(typeof(IStringId)))
                        value = StringIdUtil.CreateDynamic(type, str);
                    else
                        throw new MemberAccessException($"Unhandled C# type for {nameof(NftPropertyType)}.{NftPropertyType.Text}: {type.ToGenericTypeString()}");

                    break;
                }

                case NftPropertyType.Boolean:
                {
                    value = (bool)((JValue)jsonPropertyValue).Value;
                    break;
                }

                case NftPropertyType.Number:
                {
                    double doubleValue = Convert.ToDouble(((JValue)jsonPropertyValue).Value, CultureInfo.InvariantCulture);

                    if (type == typeof(F32))
                        value = F32.FromDouble(doubleValue);
                    else if (type == typeof(F64))
                        value = F64.FromDouble(doubleValue);
                    else
                        value = Convert.ChangeType(doubleValue, type, CultureInfo.InvariantCulture);

                    break;
                }

                default:
                    throw new MetaAssertException("Unhandled NFT property type {PropertyType}", propertyType);
            }

            object memberValue;
            if (memberType.IsGenericTypeOf(typeof(MetaRef<>)))
                memberValue = MetaRefUtil.CreateFromKey(memberType, value);
            else
                memberValue = value;

            return memberValue;
        }

        static JObject TryGetOpenSeaAttributeForValue(object memberValue, string propertyName, NftPropertyType propertyType, JToken propertyValue)
        {
            switch (propertyType)
            {
                case NftPropertyType.Enum:
                case NftPropertyType.Text:
                {
                    return new JObject
                    {
                        { "trait_type", propertyName },
                        { "value", propertyValue },
                    };
                }

                case NftPropertyType.Boolean:
                {
                    // \todo #nft OpenSea doesn't have booleans per se, so what to do?
                    //       Currently this uses OpenSea's support for attributes that
                    //       only have "value" (no "trait_type"), which we set as being
                    //       present based on the boolean.
                    if ((bool)memberValue)
                    {
                        return new JObject
                        {
                            { "value", new JValue(propertyName) },
                        };
                    }
                    else
                        return null;
                }

                case NftPropertyType.Number:
                {
                    return new JObject
                    {
                        { "trait_type", propertyName },
                        { "value", propertyValue },
                        { "display_type", new JValue("number") },
                    };
                }

                default:
                    throw new MetaAssertException("Unhandled NFT property type {PropertyType}", propertyType);
            }
        }

        static NftMetadataCorePropertyId GetCorePropertyId(MemberInfo member, NftMetadataCorePropertyAttribute propAttr)
        {
            NftMetadataCorePropertyId propertyId;

            if (propAttr.Id.HasValue)
                propertyId = propAttr.Id.Value;
            else
            {
                switch (member.Name.ToLowerInvariant())
                {
                    case "name":
                        propertyId = NftMetadataCorePropertyId.Name;
                        break;

                    case "description":
                        propertyId = NftMetadataCorePropertyId.Description;
                        break;

                    case "image":
                    case "imageurl":
                    case "image_url":
                        propertyId = NftMetadataCorePropertyId.ImageUrl;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Failed to deduce NFT metadata core property from member name '{member.Name}' (in {member.DeclaringType.ToGenericTypeString()})."
                            + $" Please either give the core property id explicitly to the {nameof(NftMetadataCorePropertyAttribute)} or adjust the member's name.");
                }
            }

            NftPropertyType expectedPropertyType = GetCorePropertyType(propertyId);
            NftPropertyType actualPropertyType = GetPropertyType(member);
            if (actualPropertyType != expectedPropertyType)
            {
                throw new InvalidOperationException(
                    $"NFT metadata core property {propertyId} is expected to have property type {expectedPropertyType}"
                    + $", but {member.ToMemberWithGenericDeclaringTypeString()} has type {member.GetDataMemberType()} which maps to property type {actualPropertyType}");
            }

            return propertyId;
        }

        static string GetPropertyJsonName(PropertySpec property)
        {
            if (property is CorePropertySpec coreProperty)
                return GetCorePropertyJsonName(coreProperty.Id);
            else
                return ((CustomPropertySpec)property).Name;
        }

        static NftPropertyType GetPropertyType(PropertySpec property)
        {
            if (property is CorePropertySpec coreProperty)
                return GetCorePropertyType(coreProperty.Id);
            else
                return ((CustomPropertySpec)property).Type;
        }

        static string GetCorePropertyJsonName(NftMetadataCorePropertyId propertyId)
        {
            switch (propertyId)
            {
                case NftMetadataCorePropertyId.Name:        return "name";
                case NftMetadataCorePropertyId.Description: return "description";
                case NftMetadataCorePropertyId.ImageUrl:    return "image";
                default:
                    throw new MetaAssertException("Unhandled NFT core property id {PropertyId}", propertyId);
            }
        }

        static NftPropertyType GetCorePropertyType(NftMetadataCorePropertyId propertyId)
        {
            switch (propertyId)
            {
                case NftMetadataCorePropertyId.Name:
                    return NftPropertyType.Text;

                case NftMetadataCorePropertyId.Description:
                    return NftPropertyType.Text;

                case NftMetadataCorePropertyId.ImageUrl:
                    return NftPropertyType.Text;

                default:
                    throw new MetaAssertException("Unhandled NFT core property id {PropertyId}", propertyId);
            }
        }

        static NftPropertyType GetPropertyType(MemberInfo member)
        {
            Type type = member.GetDataMemberType();

            return TryGetPropertyType(type)
                   ?? throw new InvalidOperationException($"Member {member.ToMemberWithGenericDeclaringTypeString()} has type {type.ToGenericTypeString()} which is not a supported type of an NFT metadata property");
        }

        static NftPropertyType? TryGetPropertyType(Type memberType)
        {
            if (memberType.IsEnum)
                return NftPropertyType.Enum;
            else if (memberType == typeof(string) || memberType.ImplementsInterface<IStringId>())
                return NftPropertyType.Text;
            else if (memberType == typeof(bool))
                return NftPropertyType.Boolean;
            else if (IsNumberType(memberType))
                return NftPropertyType.Number;
            else if (memberType.IsGenericTypeOf(typeof(MetaRef<>)))
            {
                // MetaRef maps to whatever its key maps to.
                return TryGetPropertyType(GetMetaRefKeyType(memberType));
            }
            else
                return null;
        }

        static bool IsNumberType(Type type)
        {
            return type == typeof(F32)
                || type == typeof(F64)
                || type == typeof(sbyte)
                || type == typeof(byte)
                || type == typeof(short)
                || type == typeof(ushort)
                || type == typeof(int)
                || type == typeof(uint)
                || type == typeof(long)
                || type == typeof(ulong)
                || type == typeof(float)
                || type == typeof(double);
        }

        static Type GetMetaRefKeyType(Type metaRefType)
        {
            Type configInfoType = metaRefType.GetGenericArguments().Single();
            Type configKeyType = configInfoType.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>)).Single();
            return configKeyType;
        }
    }
}
