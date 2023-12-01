// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Serialization
{
    // Deep compare implementations for MetaSerializerTypeRegistry contents, used for internal validation only.

    public class DeserializationConverterDeepCompare : EqualityComparer<MetaDeserializationConverter>
    {
        public static DeserializationConverterDeepCompare Instance = new DeserializationConverterDeepCompare();

        public override bool Equals(MetaDeserializationConverter x, MetaDeserializationConverter y)
        {
            return x.AcceptedWireDataType == y.AcceptedWireDataType &&
                x.SourceType == y.SourceType &&
                x.SourceDeserialization == y.SourceDeserialization &&
                x.TargetType == y.TargetType &&
                x.GetType() == y.GetType();
        }

        public override int GetHashCode(MetaDeserializationConverter obj)
        {
            throw new NotImplementedException();
        }
    }

    public class MetaSerializableMemberDeepCompare : EqualityComparer<MetaSerializableMember>
    {
        public static MetaSerializableMemberDeepCompare Instance = new MetaSerializableMemberDeepCompare();

        public override bool Equals(MetaSerializableMember x, MetaSerializableMember y)
        {
            return x.TagId == y.TagId &&
                   x.Flags == y.Flags &&
                   EqualityComparer<MemberInfo>.Default.Equals(x.MemberInfo, y.MemberInfo) &&
                   EqualityComparer<MethodInfo>.Default.Equals(x.OnDeserializationFailureMethod, y.OnDeserializationFailureMethod);
        }

        public override int GetHashCode(MetaSerializableMember obj)
        {
            throw new NotImplementedException();
        }
    }

    public class MetaSerializableTypeDeepCompare : EqualityComparer<MetaSerializableType>
    {
        public static MetaSerializableTypeDeepCompare Instance = new MetaSerializableTypeDeepCompare();

        public override bool Equals(MetaSerializableType x, MetaSerializableType y)
        {
            if (x.Type != y.Type)
                return false;
            if (x.Name != y.Name)
                return false;
            if (x.IsPublic != y.IsPublic)
                return false;
            if (x.UsesImplicitMembers != y.UsesImplicitMembers)
                return false;
            if (x.WireType != y.WireType)
                return false;
            if (x.HasSerializableAttribute != y.HasSerializableAttribute)
                return false;
            if (x.TypeCode != y.TypeCode)
                return false;
            if (x.Members != y.Members && !Enumerable.SequenceEqual(x.Members, y.Members, MetaSerializableMemberDeepCompare.Instance))
                return false;
            if (x.DerivedTypes != y.DerivedTypes && !Enumerable.SequenceEqual(x.DerivedTypes, y.DerivedTypes))
                return false;
            if (x.OnDeserializedMethods != y.OnDeserializedMethods && !Enumerable.SequenceEqual(x.OnDeserializedMethods, y.OnDeserializedMethods))
                return false;
            if (x.ConfigNullSentinelKey != y.ConfigNullSentinelKey)
                return false;
            if (x.DeserializationConverters != y.DeserializationConverters && !Enumerable.SequenceEqual(x.DeserializationConverters, y.DeserializationConverters, DeserializationConverterDeepCompare.Instance))
                return false;
            return true;

        }

        public override int GetHashCode(MetaSerializableType obj)
        {
            throw new NotImplementedException();
        }
    }

    public class MetaSerializerTypeInfoDeepCompare : EqualityComparer<MetaSerializerTypeInfo>
    {
        public override bool Equals(MetaSerializerTypeInfo x, MetaSerializerTypeInfo y)
        {
            if (x.FullTypeHash != y.FullTypeHash)
                return false;

            if (x.Specs.Count != y.Specs.Count)
                return false;

            foreach (Type t in x.Specs.Keys)
            {
                if (!y.Specs.ContainsKey(t))
                    return false;

                if (!MetaSerializableTypeDeepCompare.Instance.Equals(x.Specs[t], y.Specs[t]))
                {
                    DebugLog.Error("Type specs for {type} are different", t);
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode(MetaSerializerTypeInfo obj)
        {
            throw new NotImplementedException();
        }
    }
}
