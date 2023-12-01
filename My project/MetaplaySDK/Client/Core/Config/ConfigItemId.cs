// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Identifies an item within a game config.
    /// Note that depending on the context, these ids are not necessarily canonical;
    /// i.e. multiple different ids might identify the same item:
    /// - Ids might have different <see cref="ItemType"/> but still refer to the same item,
    ///   because base and derived config item types resolve to the same library
    ///   (for example: SDK code refers to e.g. InAppPurchaseInfoBase
    ///    and user code refers to the concrete InAppPurchaseInfo).
    /// - Ids might have different <see cref="Key"/> but still refer to the same item,
    ///   due to config item aliases.
    /// Canonicalization can be performed, where needed, by using the base-vs-derived
    /// type information, and alias mappings.
    /// </summary>
    public struct ConfigItemId : IEquatable<ConfigItemId>
    {
        public readonly Type ItemType;
        public readonly object Key;

        public ConfigItemId(Type itemType, object key)
        {
            ItemType = itemType;
            Key = key;
        }

        public override bool Equals(object obj) => obj is ConfigItemId other && Equals(other);
        public bool Equals(ConfigItemId other) => ItemType == other.ItemType && KeyEquals(Key, other.Key);

        bool KeyEquals(object keyA, object keyB)
        {
            if (keyA is null)
                return keyB is null;
            return keyA.Equals(keyB);
        }

        public static bool operator ==(ConfigItemId left, ConfigItemId right) => left.Equals(right);
        public static bool operator !=(ConfigItemId left, ConfigItemId right) => !(left == right);

        public override int GetHashCode() => Util.CombineHashCode(ItemType?.GetHashCode() ?? 0, Key?.GetHashCode() ?? 0);

        public override string ToString() => $"({ItemType}: {(Key == null ? "<null>" : Util.ObjectToStringInvariant(Key))})";
    }
}
