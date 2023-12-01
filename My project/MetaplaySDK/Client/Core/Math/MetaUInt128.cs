// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Globalization;

namespace Metaplay.Core.Math
{
    /// <summary>
    /// Represents a 128-bit unsigned integer value.
    /// </summary>
    public struct MetaUInt128 : IEquatable<MetaUInt128>, IComparable<MetaUInt128>, IComparable
    {
        public ulong    High;
        public ulong    Low;

        public static MetaUInt128 Zero => new MetaUInt128();
        public static MetaUInt128 One => new MetaUInt128(0, 1);

        public MetaUInt128(ulong high, ulong low) { High = high; Low = low; }

        public static MetaUInt128 FromUInt(uint v) => new MetaUInt128(0ul, v);

        public static bool operator ==(MetaUInt128 a, MetaUInt128 b) => (a.High == b.High) && (a.Low == b.Low);
        public static bool operator !=(MetaUInt128 a, MetaUInt128 b) => (a.High != b.High) || (a.Low != b.Low);
        public static bool operator <(MetaUInt128 a, MetaUInt128 b) => a.CompareTo(b) < 0;
        public static bool operator <=(MetaUInt128 a, MetaUInt128 b) => a.CompareTo(b) <= 0;
        public static bool operator >(MetaUInt128 a, MetaUInt128 b) => a.CompareTo(b) > 0;
        public static bool operator >=(MetaUInt128 a, MetaUInt128 b) => a.CompareTo(b) >= 0;

        public static MetaUInt128 operator ~(MetaUInt128 a) => new MetaUInt128(~a.High, ~a.Low);
        public static MetaUInt128 operator |(MetaUInt128 a, MetaUInt128 b) => new MetaUInt128(a.High | b.High, a.Low | b.Low);
        public static MetaUInt128 operator &(MetaUInt128 a, MetaUInt128 b) => new MetaUInt128(a.High & b.High, a.Low & b.Low);
        public static MetaUInt128 operator ^(MetaUInt128 a, MetaUInt128 b) => new MetaUInt128(a.High ^ b.High, a.Low ^ b.Low);

        public static MetaUInt128 operator <<(MetaUInt128 a, int sh)
        {
            if (sh <= 0) // negative values clamp to zero
                return a;
            else if (sh < 64)
                return new MetaUInt128((a.High << sh) + (a.Low >> (64 - sh)), a.Low << sh);
            else if (sh < 128)
                return new MetaUInt128(a.Low << (sh - 64), 0ul);
            else // 128 or higher
                return Zero;
        }

        public static MetaUInt128 operator >>(MetaUInt128 a, int sh)
        {
            if (sh <= 0) // negative values clamp to zero
                return a;
            else if (sh < 64)
                return new MetaUInt128(a.High >> sh, (a.High << (64 - sh)) + (a.Low >> sh));
            else if (sh < 128)
                return new MetaUInt128(0ul, a.High >> (sh - 64));
            else // 128 or higher
                return Zero;
        }

        public override readonly int GetHashCode() => (High.GetHashCode() ^ (long)Low).GetHashCode();

        public override readonly bool Equals(object obj) => (obj is MetaUInt128 other) ? (this == other) : false;

        public override readonly string ToString() => string.Format(CultureInfo.InvariantCulture, "0x{0:X16}{1:X16}", High, Low);

        public readonly bool Equals(MetaUInt128 other) => (High == other.High) && (Low == other.Low);

        public readonly int CompareTo(MetaUInt128 other)
        {
            int c = High.CompareTo(other.High);
            return (c != 0) ? c : Low.CompareTo(other.Low);
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is MetaUInt128 other)
                return CompareTo(other);
            else if (obj is null)
                return 1;
            // don't allow comparisons with other numeric or non-numeric types.
            throw new ArgumentException("MetaUInt128 can only be compared against another MetaUInt128.");
        }
    }
}
