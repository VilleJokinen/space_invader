// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Core.Model
{
    // MetaSerializableFlags

    [Flags]
    public enum MetaSerializableFlags
    {
        None                = 0,

        /// <summary>
        /// Automatic MetaMemberAttribute (with running tagId) is assigned to all fields/properties
        /// </summary>
        ImplicitMembers = 1 << 0,

        /// <summary>
        /// Automatically detect and invoke the constructor when deserializing into an instance, this requires a constructor that has the same parameters (names, types) as the MetaMembers in the class.
        /// </summary>
        AutomaticConstructorDetection = 1 << 1,
    }

    // MetaSerializableAttribute

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum)]
    public sealed class MetaSerializableAttribute : Attribute
    {
        public readonly MetaSerializableFlags   Flags;

        public MetaSerializableAttribute()
        {
        }

        public MetaSerializableAttribute(MetaSerializableFlags flags)
        {
            Flags = flags;
        }

        public override string ToString() => $"[MetaSerializable({Flags})]";

        public override bool Equals(object obj) => obj is MetaSerializableAttribute other && Flags == other.Flags;
        public override int GetHashCode() => Flags.GetHashCode();

        public static bool operator ==(MetaSerializableAttribute a, MetaSerializableAttribute b)
        {
            if (a is null)
                return b is null;
            return a.Equals(b);
        }

        public static bool operator !=(MetaSerializableAttribute left, MetaSerializableAttribute right) => !(left == right);
    }

    // MetaSerializableDerivedAttribute

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MetaSerializableDerivedAttribute : Attribute, ISerializableTypeCodeProvider
    {
        public int TypeCode { get; private set; }

        public MetaSerializableDerivedAttribute(int typeCode)
        {
            TypeCode = typeCode;
        }
    }

    [AttributeUsage(AttributeTargets.Constructor)]
    public sealed class MetaDeserializationConstructorAttribute : Attribute
    {
    }

    /// <summary>
    /// Defines a range to use for the automatically-allocated tag ids for members
    /// declared within a single class in a hierarchy when using <see cref="MetaSerializableFlags.ImplicitMembers"/>.
    /// When using <see cref="MetaSerializableFlags.ImplicitMembers"/>, this attribute
    /// is required on all classes in the hierarchy except concrete classes and on
    /// classes that declare no data members. On concrete classes, if this attribute
    /// is not used, a <see cref="MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute"/>
    /// is required to be present on any base class or interface.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class MetaImplicitMembersRangeAttribute : Attribute
    {
        public readonly int StartIndex; // Inclusive start index of range for implicit tagIds
        public readonly int EndIndex;   // Exclusive end index of range for implicit tagIds

        public MetaImplicitMembersRangeAttribute(int startIndex, int endIndex)
        {
            if (startIndex <= 0)
                throw new ArgumentException($"{nameof(MetaImplicitMembersRangeAttribute)}: start index must be positive (is {startIndex})", nameof(startIndex));
            if (startIndex > endIndex)
                throw new ArgumentException($"{nameof(MetaImplicitMembersRangeAttribute)}: start index ({startIndex}) cannot be greater than end index {endIndex}", nameof(startIndex));

            StartIndex = startIndex;
            EndIndex = endIndex;
        }
    }

    /// <summary>
    /// Attribute providing the default tag id range for implicit members in the
    /// most-derived class in a type hierarchy when <see cref="MetaSerializableFlags.ImplicitMembers"/>
    /// is used and <see cref="MetaImplicitMembersRangeAttribute"/> is omitted on
    /// the most-derived class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute : Attribute
    {
        public readonly int StartIndex; // Inclusive start index of range for implicit tagIds
        public readonly int EndIndex;   // Exclusive end index of range for implicit tagIds

        public MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute(int startIndex, int endIndex)
        {
            if (startIndex <= 0)
                throw new ArgumentException($"{nameof(MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute)}: start index must be positive (is {startIndex})", nameof(startIndex));
            if (startIndex > endIndex)
                throw new ArgumentException($"{nameof(MetaImplicitMembersDefaultRangeForMostDerivedClassAttribute)}: start index ({startIndex}) cannot be greater than end index {endIndex}", nameof(startIndex));

            StartIndex = startIndex;
            EndIndex = endIndex;
        }
    }

    /// <summary>
    /// Flags that can be given to members of serialized objects.
    /// </summary>
    [Flags]
    public enum MetaMemberFlags
    {
        // Flags for fields
        None                    = 0,
        Hidden                  = 1 << 0,               // Member is hidden (from client) and should not be sent over network
        NoChecksum              = 1 << 1,               // Member is ignored when computing checksums
        Transient               = 1 << 2,               // Member is transmitted between client/server, but not persisted in database
        ServerOnly              = Hidden | NoChecksum,  // Member is server-side only
        ExcludeFromGdprExport   = 1 << 3,               // Member will be excluded from player-facing data exports
    }

    /// <summary>
    /// Declares a member to be serialized with the given tagId.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MetaMemberAttribute : Attribute
    {
        public readonly int             TagId;
        public readonly MetaMemberFlags Flags;

        public MetaMemberAttribute(int tagId, MetaMemberFlags flags = MetaMemberFlags.None)
        {
            if (tagId <= 0)
                throw new ArgumentException($"MetaMemberAttribute's tagId must be positive (is {tagId})");

            TagId = tagId;
            Flags = flags;
        }
    }

    /// <summary>
    /// Using this attribute on a class forbids the usage of the specified MetaMember tagIds. When old tagIds are
    /// removed from a class/struct, it's a good idea to add them to the block list such that they cannot be
    /// accidentally used in the class. Re-using tagIds may lead to deserialization failures.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class MetaBlockedMembersAttribute : Attribute
    {
        public readonly int[] BlockedMemberIds;

        public MetaBlockedMembersAttribute(params int[] blockedMemberIds)
        {
            BlockedMemberIds = blockedMemberIds;
        }
    }

    /// <summary>
    /// Reserve a range of member tagIds for targeted class. Those tagIds cannot be used either in base
    /// classes or derived classes of target class. Useful for reserving a range of attributes for the use
    /// in a base class as it avoids conflicts where the derived classes would start using the same tagIds.
    /// </summary>
    /// <remarks>
    /// By default, this also works the other way around: if a type reserves any member ranges, that type
    /// can only use member tagIds that it has reserved. This is meant to prevent accidental usage of
    /// member tagIds which are intended to be left for subclasses. To disable this rule, use
    /// <see cref="MetaAllowNonReservedMembersAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class MetaReservedMembersAttribute : Attribute
    {
        public readonly int StartIndex; // Inclusive start index of reserved tagIds
        public readonly int EndIndex;   // Exclusive end index of reserved tagIds

        public MetaReservedMembersAttribute(int startNdx, int endNdx) { StartIndex = startNdx; EndIndex = endNdx; }
    }

    /// <summary>
    /// When used on a type with <see cref="MetaReservedMembersAttribute"/>, that type is permitted to
    /// have members outside its reserved member ranges.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MetaAllowNonReservedMembersAttribute : Attribute
    {
    }

    /// <summary>
    /// Overrides the default maximum size of a collection
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MaxCollectionSizeAttribute : Attribute
    {
        public readonly int Size;

        /// <param name="size">The maximum number of elements a collection can contain, default is see <see cref="MetaSerializationContext.DefaultMaxCollectionSize"/></param>
        public MaxCollectionSizeAttribute(int size) => Size = size;
    }

    /// <summary>
    /// Member was added in specified LogicVersion.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class AddedInVersionAttribute : Attribute
    {
        public readonly int LogicVersion;   // LogicVersion in which the member was introduced (inclusive)

        public AddedInVersionAttribute(int logicVersion) => LogicVersion = logicVersion;
    }

    /// <summary>
    /// Member was removed in specified LogicVersion.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class RemovedInVersionAttribute : Attribute
    {
        public readonly int LogicVersion;   // LogicVersion in which the member was removed (if version matches, member should be considered removed)

        public RemovedInVersionAttribute(int logicVersion) => LogicVersion = logicVersion;
    }

    /// <summary>
    /// Base class for giving a class property additional member flags.
    /// </summary>
    public abstract class MetaMemberFlagAttribute : Attribute
    {
        public abstract MetaMemberFlags Flags { get; }
    }

    /// <summary>
    /// Declares a field to be transient, meaning that it can be transported between client and server,
    /// but is not persisted in database.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class TransientAttribute : MetaMemberFlagAttribute
    {
        public override MetaMemberFlags Flags => MetaMemberFlags.Transient;
    }

    /// <summary>
    /// Property or field is server-side only. The property isn't included when serializing
    /// the containing object to be transmitted over the network to the client.
    /// </summary>
    /// <remarks>
    /// The attribute is useful for state that the client shouldn't ever see, for example,
    /// a secret RNG which is used for resolving contents of a gacha drop.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ServerOnlyAttribute : MetaMemberFlagAttribute
    {
        public override MetaMemberFlags Flags => MetaMemberFlags.ServerOnly;
    }

    /// <summary>
    /// Property or field is not included when computing checksum for the containing object.
    /// </summary>
    /// <remarks>
    /// The attribute is useful when a property can get modified by the client and the server
    /// which would cause a checksum comparison to fail.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class NoChecksumAttribute : MetaMemberFlagAttribute
    {
        public override MetaMemberFlags Flags => MetaMemberFlags.NoChecksum;
    }

    /// <summary>
    /// Declares a field to be excluded from potential GDPR data requests. This attribute
    /// may be used to hide fields that are sensitive but do not contain player's personal
    /// information, such as Random generator states. Additionally, the attribute can be
    /// used to prune irrelevant fields, such as many Transient fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ExcludeFromGdprExportAttribute : MetaMemberFlagAttribute
    {
        public override MetaMemberFlags Flags => MetaMemberFlags.ExcludeFromGdprExport;
    }

    /// <summary>
    /// Registers a method to be called just after the object has been deserialized.
    /// This is supported for the top-level object being deserialized by a deserialization
    /// method in <see cref="Metaplay.Core.Serialization.MetaSerialization"/>,
    /// as well as nested objects.
    /// </summary>
    /// <remarks>
    /// The method can optionally take a single parameter of type
    /// <see cref="Serialization.MetaOnDeserializedParams"/>.
    /// Alternatively, it can take no parameters.
    ///
    /// Multiple on-deserialized methods can be defined in a class hierarchy.
    /// They are called in class hierarchy order, basemost class first.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MetaOnDeserializedAttribute : Attribute
    {
    }

    /// <summary>
    /// Used on a serializable data member, this attribute specifies a method
    /// for handling deserialization failures occurring when deserializing that member.
    /// The method must be a static method in the same type as the member,
    /// it must take a single parameter of type <see cref="Serialization.MetaMemberDeserializationFailureParams"/>,
    /// and must return a type that is assignable to the type of the member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MetaOnMemberDeserializationFailureAttribute : Attribute
    {
        public readonly string MethodName;

        public MetaOnMemberDeserializationFailureAttribute(string methodName)
        {
            MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        }
    }

    /// <summary>
    /// Marks a class as having a <see cref="MetaSerializableTypeGetterAttribute"/>-equipped
    /// method. See <see cref="MetaSerializableTypeGetterAttribute"/> for more info.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class MetaSerializableTypeProviderAttribute : Attribute
    {
    }

    /// <summary>
    /// When a method is registered with this attribute (and is in a type
    /// equipped with <see cref="MetaSerializableTypeProviderAttribute"/>),
    /// it is called from the serializer type registry initialization
    /// to register additional MetaSerializable types, in addition to the
    /// types encountered by the normal type scanning.
    /// The purpose is to allow registering specific instantiations of
    /// generic types when they are not easily expressed statically.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MetaSerializableTypeGetterAttribute : Attribute
    {
    }

    // ISerializableTypeCodeProvider

    public interface ISerializableTypeCodeProvider
    {
        int TypeCode { get; }
    }

    // ISerializableFlagsProvider

    public interface ISerializableFlagsProvider
    {
        MetaSerializableFlags ExtraFlags { get; }
    }

    /// <summary>
    /// Types with no serializable fields are often unintentional and are disallowed by default. Setting this attribute
    /// allow serializations even if this type has no serializable fields.
    /// <para>
    /// Note that if the type has no fields at all (neither serializable nor non-serializable), they are allowed
    /// regardless of this flag. This avoids need to annotate completely empty types, such as abstract base classes.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum)]
    public sealed class MetaAllowNoSerializedMembers : Attribute
    {
    }
}
