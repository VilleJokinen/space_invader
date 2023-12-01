// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Metaplay.Core
{
    class EntityKindTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            if (sourceType == typeof(string))
                return true;

            return base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string str)
                return EntityKind.FromName(str);

            return base.ConvertFrom(context, culture, value);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            if (destinationType == typeof(string))
                return true;

            return base.CanConvertTo(context, destinationType);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is EntityKind entityKind)
                return entityKind.ToString();

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    /// <summary>
    /// Dynamic enumeration identifying a family of game entities. The entity kinds are used in
    /// <see cref="EntityId"/> with combination of value to uniquely identify entities.
    ///
    /// Typical game entity examples include Player, Guild, Matchmaker and Match. On the backend,
    /// there are additional entities not visible to the client, for example: InAppValidator,
    /// GlobalStateManager, PushNotifier, and so on.
    ///
    /// To register game-specific <see cref="EntityKind"/>s, please see the <c>EntityKindGame</c>
    /// for client/server shared entities, and <c>EntityKindCloudGame</c> for server-only entities.
    /// </code>
    /// </summary>
    [TypeConverter(typeof(EntityKindTypeConverter))]
    public struct EntityKind : IEquatable<EntityKind>
    {
        public const int MaxValue = 64; // 6 bits are used for storing EntityKind in EntityId (exclusive)

        public static readonly EntityKind None = FromValue(0); // \note This doesn't get included in the registry!

        public readonly int Value;

        public readonly string Name => EntityKindRegistry.GetName(this);

        public EntityKind(int value)
        {
            if (value < 0 || value >= MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));

            Value = value;
        }

        public static EntityKind FromValue(int value) => new EntityKind(value);
        public static EntityKind FromName(string name) => EntityKindRegistry.FromName(name);

        public static bool operator ==(EntityKind a, EntityKind b) => a.Value == b.Value;
        public static bool operator !=(EntityKind a, EntityKind b) => a.Value != b.Value;

        public bool Equals(EntityKind other) => Value == other.Value;

        public override readonly bool Equals(object obj) => (obj is EntityKind other) ? (this == other) : false;
        public override readonly int GetHashCode() => Value;
        public override readonly string ToString() => Name;
    }

    /// <summary>
    /// Set of multiple EntityKinds. Stored internally as 64-bit ulong bitmask.
    /// </summary>
    public struct EntityKindMask : IEquatable<EntityKindMask>
    {
        public ulong Mask { get; private set; }

        public static EntityKindMask None => new EntityKindMask(0ul);
        public static EntityKindMask All => new EntityKindMask(EntityKindRegistry.AllValues.Where(kind => kind != EntityKind.None));

        public bool IsEmpty => Mask == 0ul;

        public EntityKindMask(ulong mask) { Mask = mask; }
        public EntityKindMask(IEnumerable<EntityKind> kinds)
        {
            // Compute the mask
            ulong mask = 0ul;
            foreach (EntityKind kind in kinds)
                mask |= 1ul << kind.Value;
            Mask = mask;
        }

        public static EntityKindMask FromEntityKind(EntityKind entityKind)
        {
            return new EntityKindMask(1ul << entityKind.Value);
        }

        public IEnumerable<EntityKind> GetKinds()
        {
            ulong mask = Mask;
            for (int ndx = 0; ndx < (int)EntityKind.MaxValue; ndx++)
            {
                if ((mask & (1ul << ndx)) != 0)
                    yield return EntityKind.FromValue(ndx);
            }
        }

        public static EntityKindMask Parse(IEnumerable<string> elems)
        {
            List<EntityKind> entityKinds =
                elems
                .Select(kindStr => EntityKind.FromName(kindStr))
                .ToList();

            return new EntityKindMask(entityKinds);
        }

        public static EntityKindMask ParseString(string str)
        {
            return Parse(str.Split(' '));
        }

        public static bool operator ==(EntityKindMask a, EntityKindMask b) => a.Mask == b.Mask;
        public static bool operator !=(EntityKindMask a, EntityKindMask b) => a.Mask != b.Mask;
        public static EntityKindMask operator ~(EntityKindMask a) => new EntityKindMask(~a.Mask);
        public static EntityKindMask operator &(EntityKindMask a, EntityKindMask b) => new EntityKindMask(a.Mask & b.Mask);
        public static EntityKindMask operator |(EntityKindMask a, EntityKindMask b) => new EntityKindMask(a.Mask | b.Mask);

        public readonly bool IsSet(EntityKind kind) => (Mask & (1ul << kind.Value)) != 0;
        public void Set(EntityKind kind) => Mask |= (1ul << kind.Value);

        public readonly bool Equals(EntityKindMask other) => Mask == other.Mask;

        public override readonly bool Equals(object obj) => (obj is EntityKindMask other) ? Mask == other.Mask : false;

        public override readonly int GetHashCode() => Mask.GetHashCode();

        public override readonly string ToString()
        {
            ulong mask = Mask;
            return string.Join(" | ",
                Enumerable.Range(0, EntityKind.MaxValue)
                .Where(ndx => (mask & (1ul << ndx)) != 0)
                .Select(ndx => EntityKind.FromValue(ndx).Name));
        }
    }
}
