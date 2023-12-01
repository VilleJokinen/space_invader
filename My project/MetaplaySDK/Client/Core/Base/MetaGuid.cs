// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Represents a globally unique time-ordered identifier. Top 60 bits are the timestamp
    /// in 100ns resolution (DateTime.UtcNow.Ticks except with Unix epoch), the next 4 bits
    /// are reserved (must be zero), and bottom 64 bits are a random value.
    /// String representation is '0123456789abcde-0-0123456789abcedf' where first part is the
    /// timestamp, middle part is reserved (0), and the last part is the random value.
    /// Unix epoch is used for the raw values, so representable range is from 1970 Jan 1 onwards,
    /// until year 5623.
    /// </summary>
    public struct MetaGuid : IEquatable<MetaGuid>, IComparable<MetaGuid>, IComparable
    {
        public MetaUInt128 Value { get; private set; }

        public static readonly MetaGuid None = new MetaGuid(MetaUInt128.Zero);

        // Use Unix epoch as the zero time for MetaGuids.
        public static readonly DateTime MinDateTime = MetaTime.DateTimeEpoch;
        public static readonly DateTime MaxDateTime = new DateTime(((1L << 60) - 1) + MetaTime.DateTimeEpoch.Ticks, DateTimeKind.Utc);

        static readonly long EpochTicks  = MinDateTime.Ticks;

        public readonly bool IsValid => Value != MetaUInt128.Zero;

        public MetaGuid(MetaUInt128 value)
        {
            // Check that reserved bits are zero
            if ((value.High & 0xF) != 0)
                throw new ArgumentException("MetaGuid reserved bits must be zero", nameof(value));

            Value = value;
        }

        public readonly DateTime GetDateTime() => new DateTime((long)(Value.High >> 4) + EpochTicks, DateTimeKind.Utc);

        /// <summary>
        /// Convert a DateTime to a Unix-epoched 60-bit timestamp value, shifted to the higher bits of an ulong.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        static ulong ConvertDateTimeToTimestamp(DateTime timestamp)
        {
            if (timestamp.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Timestamp must be of DateTimeKind.Utc", nameof(timestamp));

            return (ulong)(timestamp.Ticks - EpochTicks) << 4;
        }

        /// <summary>
        /// Construct with current wall clock time.
        /// </summary>
        /// <returns></returns>
        public static MetaGuid New()
        {
            DateTime now = DateTime.UtcNow;
            ulong high = ConvertDateTimeToTimestamp(now);
            ulong low = GetRandomValue();
            return new MetaGuid(new MetaUInt128(high, low));
        }

        static void CheckTimestampRange(DateTime timestamp)
        {
            if (timestamp < MinDateTime)
                throw new ArgumentException($"Timestamp ({timestamp}) is too far in the past (beyond Unix epoch)", nameof(timestamp));
            if (timestamp > MaxDateTime)
                throw new ArgumentException($"Timestamp ({timestamp}) is too far in the future", nameof(timestamp));
        }

        /// <summary>
        /// Construct with specified time. Useful when multiple identifiers with identical time are needed.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static MetaGuid NewWithTime(DateTime timestamp)
        {
            CheckTimestampRange(timestamp);
            ulong high = ConvertDateTimeToTimestamp(timestamp);
            ulong low = GetRandomValue();
            return new MetaGuid(new MetaUInt128(high, low));
        }

        /// <summary>
        /// Construct with specified time and chosen "random" value. Useful when generating deterministic <c>MetaGuid</c>s.
        /// </summary>
        public static MetaGuid FromTimeAndValue(DateTime timestamp, ulong value)
        {
            CheckTimestampRange(timestamp);
            ulong high = ConvertDateTimeToTimestamp(timestamp);
            return new MetaGuid(new MetaUInt128(high, value));
        }

        /// <inheritdoc cref="FromTimeAndValue(DateTime, ulong)"/>
        public static MetaGuid FromTimeAndValue(MetaTime timestamp, ulong value) => FromTimeAndValue(timestamp.ToDateTime(), value);

        static ulong GetRandomValue()
        {
            // \todo [petri] .NET6 adds: return RandomNumberGenerator.GetBytes(8);

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
#if NETCOREAPP
                Span<byte> bytes = stackalloc byte[8];
                rng.GetBytes(bytes);
                return BitConverter.ToUInt64(bytes);
#else
                byte[] buffer = new byte[8];
                rng.GetBytes(buffer);
                return BitConverter.ToUInt64(buffer, startIndex: 0);
#endif
            }
        }

        public static bool operator ==(MetaGuid a, MetaGuid b) => a.Value == b.Value;
        public static bool operator !=(MetaGuid a, MetaGuid b) => a.Value != b.Value;
        public static bool operator <(MetaGuid a, MetaGuid b) => a.Value < b.Value;
        public static bool operator <=(MetaGuid a, MetaGuid b) => a.Value <= b.Value;
        public static bool operator >(MetaGuid a, MetaGuid b) => a.Value > b.Value;
        public static bool operator >=(MetaGuid a, MetaGuid b) => a.Value >= b.Value;

        public readonly bool Equals(MetaGuid other) => this == other;
        public override readonly bool Equals(object obj) => (obj is MetaGuid other) ? (this == other) : false;
        public override readonly int GetHashCode() => Value.GetHashCode();

        public override readonly string ToString()
        {
            ulong timestamp = Value.High >> 4;
            uint reserved = (uint)Value.High & 0xF;
            ulong random = Value.Low;
            return Invariant($"{timestamp:x15}-{reserved:x1}-{random:x16}");
        }

        static Regex s_pattern = new Regex(@"^([0-9a-f]{15})\-([0-9a-f]{1})\-([0-9a-f]{16})$", RegexOptions.Compiled);

        public static MetaGuid Parse(string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            Match m = s_pattern.Match(str);
            if (!m.Success)
                throw new FormatException($"String '{str}' is not a valid {nameof(MetaGuid)} format");

            ulong timestamp = Convert.ToUInt64(m.Groups[1].Value, 16);
            uint reserved = Convert.ToUInt32(m.Groups[2].Value, 16);
            ulong random = Convert.ToUInt64(m.Groups[3].Value, 16);

            if (reserved != 0)
                throw new FormatException($"String '{str}' has invalid reserved value (must be zero)");

            ulong high = (timestamp << 4) + reserved;
            return new MetaGuid(new MetaUInt128(high, random));
        }

        public readonly int CompareTo(MetaGuid other) => Value.CompareTo(other.Value);

        int IComparable.CompareTo(object obj) => (obj is MetaGuid other) ? CompareTo(other) : 1;
    }
}
