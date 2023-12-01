// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Represents span of wall clock time, usable in game logic.
    /// Internal representation is in milliseconds (64-bit value), serializes as int64.
    ///
    /// Roughly corresponds to <see cref="TimeSpan"/>, but can be used in determinisic logic.
    /// </summary>
    [MetaSerializable]
    public struct MetaDuration : IEquatable<MetaDuration>, IComparable<MetaDuration>, IComparable
    {
        public static readonly MetaDuration Zero = new MetaDuration(0);

        [MetaMember(1)] public long Milliseconds { get; set; }

        public static MetaDuration FromDays(int days) { return new MetaDuration(days * (24 * 60 * 60 * 1000L)); }
        public static MetaDuration FromHours(int hours) { return new MetaDuration(hours * (60 * 60 * 1000L)); }
        public static MetaDuration FromMinutes(int minutes) { return new MetaDuration(minutes * (60 * 1000L)); }
        public static MetaDuration FromSeconds(long seconds) { return new MetaDuration(seconds * 1000L); }
        public static MetaDuration FromMilliseconds(long milliseconds) { return new MetaDuration(milliseconds); }

        public MetaDuration(long milliseconds) { Milliseconds = milliseconds; }

        public readonly int ToLocalTicks(int ticksPerSecond) => (int)(Milliseconds * ticksPerSecond / 1000);

        public enum RoundingMode
        {
            Floor,
            Ceil,
        }
        public static MetaDuration FromSeconds(F64 seconds, RoundingMode roundingMode)
        {
            switch(roundingMode)
            {
                case RoundingMode.Floor:
                {
                    long wholeSeconds = (seconds.Raw >> 32);
                    long fractionalMillis = ((seconds.Raw & 0xFFFF_FFFF) * 1000L) >> 32;
                    return new MetaDuration(1000L * wholeSeconds + fractionalMillis);
                }

                case RoundingMode.Ceil:
                {
                    long wholeSeconds = (seconds.Raw >> 32);
                    long fractionalMillis = (((seconds.Raw & 0xFFFF_FFFF) * 1000L) + 0xFFFF_FFFE) >> 32;
                    return new MetaDuration(1000L * wholeSeconds + fractionalMillis);
                }

                default:
                    throw new InvalidEnumArgumentException(nameof(roundingMode), (int)roundingMode, typeof(RoundingMode));
            }
        }
        public readonly F64 ToSecondsF64()
        {
            long wholeSeconds = Milliseconds / 1000;
            long remainderMillis = Milliseconds - wholeSeconds * 1000;

            if (wholeSeconds > Int32.MaxValue)
                return F64.FromInt(Int32.MaxValue);
            else if (wholeSeconds < Int32.MinValue)
                return F64.FromInt(Int32.MinValue);
            else
                return F64.FromInt((int)wholeSeconds) + F64.FromInt((int)remainderMillis) / F64.FromInt(1000);
        }

        public readonly double ToSecondsDouble() => Milliseconds / 1000.0;

        public static MetaDuration FromTimeSpan(TimeSpan span) => new MetaDuration(span.Ticks / TimeSpan.TicksPerMillisecond); // \todo: rounding modes
        public readonly TimeSpan ToTimeSpan() => TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * Milliseconds);

        public static bool operator ==(MetaDuration a, MetaDuration b) { return a.Milliseconds == b.Milliseconds; }
        public static bool operator !=(MetaDuration a, MetaDuration b) { return a.Milliseconds != b.Milliseconds; }
        public static bool operator >(MetaDuration a, MetaDuration b) { return a.Milliseconds > b.Milliseconds; }
        public static bool operator >=(MetaDuration a, MetaDuration b) { return a.Milliseconds >= b.Milliseconds; }
        public static bool operator <(MetaDuration a, MetaDuration b) { return a.Milliseconds < b.Milliseconds; }
        public static bool operator <=(MetaDuration a, MetaDuration b) { return a.Milliseconds <= b.Milliseconds; }
        public static MetaDuration operator +(MetaDuration a, MetaDuration b) { return new MetaDuration(a.Milliseconds + b.Milliseconds); }
        public static MetaDuration operator -(MetaDuration a, MetaDuration b) { return new MetaDuration(a.Milliseconds - b.Milliseconds); }
        public static MetaDuration operator -(MetaDuration a)  { return new MetaDuration(-a.Milliseconds); }
        public static MetaDuration operator *(MetaDuration a, int b) { return new MetaDuration(a.Milliseconds * b); }
        public static MetaDuration operator *(int a, MetaDuration b) { return new MetaDuration(a * b.Milliseconds); }
        public static float operator /(MetaDuration a, MetaDuration b) { return a.Milliseconds / (float)b.Milliseconds; }

        public static MetaDuration Max(MetaDuration a, MetaDuration b) => new MetaDuration(System.Math.Max(a.Milliseconds, b.Milliseconds));
        public static MetaDuration Min(MetaDuration a, MetaDuration b) => new MetaDuration(System.Math.Min(a.Milliseconds, b.Milliseconds));

        public readonly bool Equals(MetaDuration other) => Milliseconds == other.Milliseconds;

        public override readonly int GetHashCode() => Milliseconds.GetHashCode();

        public override readonly bool Equals(object obj)
        {
            if (obj is MetaDuration duration)
                return Milliseconds == duration.Milliseconds;

            return false;
        }

        /// <summary>
        /// Convert to culture-invariant, computer-friendly string with format 'd.hh:mm:ss.FFFFFFF'.
        /// Day is the largest unit that is used to avoid ambiguities.
        /// </summary>
        /// <remarks>
        /// There is no universally accepted standard format for durations. We're using the format above due to:
        /// - The format doesn't depend on the value, but always has the same components. This makes it safer to parse, eg, in analytics event pipelines.
        /// - The format is compatible with moment.js.
        /// - Using 7 digits as that's the precision of the C# 100ns ticks. A bit arbitrary decision.
        /// - Using '.' as the separator between days and hours as it seems more widely used (eg, moment.js uses it, C# uses both '.' and ':').
        /// - It's similar to TimeSpan.ToString("c"), except we always use the long format.
        /// </remarks>
        /// <returns></returns>
        // \todo [petri] this is more general than just JSON, is there a better name?
        public readonly string ToJsonString()
        {
            TimeSpan ts = ToTimeSpan();
            string sign = ts.Ticks < 0 ? "-" : "";
            long totalTicks = System.Math.Abs(ts.Ticks);
            long absTicks = totalTicks % TimeSpan.TicksPerSecond;
            ts = TimeSpan.FromTicks(totalTicks);
            return Invariant($"{sign}{ts.Days}.{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{absTicks:D7}"); // 7-digits is the 100ns tick resolution of TimeSpan
        }

        static Regex s_pattern = new Regex(@"^(\-)?(\d+)\.(\d{2}):(\d{2}):(\d{2})\.(\d{7})$", RegexOptions.Compiled);

        /// <summary>
        /// Parse from culture-invariant string in format 'd.hh:mm:ss.FFFFFFF'. Compatible with <see cref="ToJsonString()"/>.
        /// </summary>
        /// <returns></returns>
        public static MetaDuration ParseExactFromJson(string str)
        {
            if (str == null)
                throw new ArgumentNullException(str);

            Match m = s_pattern.Match(str);
            if (!m.Success)
                throw new FormatException($"String '{str}' is not a valid MetaDuration format");

            bool isNegative = m.Groups[1].Value == "-";
            int days = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int hours = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (hours >= 24)
                throw new FormatException($"Hours must be between 0 and 23, got {hours}");
            int minutes = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (minutes >= 60)
                throw new FormatException($"Minutes must be between 0 and 59, got {minutes}");
            int seconds = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            if (seconds >= 60)
                throw new FormatException($"Seconds must be between 0 and 59, got {seconds}");
            string fractionStr = m.Groups[6].Value;
            if (fractionStr.Length != 7)
                throw new FormatException($"Fraction must be exactly 7 digits long, got {fractionStr.Length} digits");
            long fraction = long.Parse(fractionStr, CultureInfo.InvariantCulture);

            MetaDuration duration = FromDays(days) + FromHours(hours) + FromMinutes(minutes) + FromSeconds(seconds) + FromMilliseconds(fraction / 10000); // \todo [petri] loses precision due to internal representation
            return isNegative ? -duration : duration;
        }

        public override readonly string ToString()
        {
            long totalSeconds = Milliseconds / 1000;
            long seconds = (totalSeconds % 60);
            long minutes = (totalSeconds / 60) % 60;
            long hours = (totalSeconds / 3600) % 24;
            long days = (totalSeconds / 86400);
            int milliseconds = (int)(Milliseconds % 1000);

            if (Milliseconds >= 24 * 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h {2}m {3}.{4:000}s", days, hours, minutes, seconds, milliseconds);
            else if (Milliseconds >= 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m {2}.{3:000}s", hours, minutes, seconds, milliseconds);
            else if (Milliseconds >= 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1}.{2:000}s", minutes, seconds, milliseconds);
            else if (Milliseconds >= 0)
                return string.Format(CultureInfo.InvariantCulture, "{0}.{1:000}s", seconds, milliseconds);
            else if (Milliseconds > -60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}.{1:000}s", -seconds, -milliseconds);
            else if (Milliseconds > -60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}m {1}.{2:000}s", -minutes, -seconds, -milliseconds);
            else if (Milliseconds > -24 * 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}h {1}m {2}.{3:000}s", -hours, -minutes, -seconds, -milliseconds);
            else
                return string.Format(CultureInfo.InvariantCulture, "-{0}d {1}h {2}m {3}.{4:000}s", -days, -hours, -minutes, -seconds, -milliseconds);
        }

        /// <summary>
        /// Like <see cref="ToString"/>, but only include the two most significant units.
        /// As an exception, milliseconds are never included.
        /// </summary>
        /// <returns></returns>
        public readonly string ToSimplifiedString()
        {
            long totalSeconds = Milliseconds / 1000;
            long seconds = (totalSeconds % 60);
            long minutes = (totalSeconds / 60) % 60;
            long hours = (totalSeconds / 3600) % 24;
            long days = (totalSeconds / 86400);

            if (Milliseconds >= 24 * 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", days, hours);
            else if (Milliseconds >= 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", hours, minutes);
            else if (Milliseconds >= 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", minutes, seconds);
            else if (Milliseconds >= 0)
                return string.Format(CultureInfo.InvariantCulture, "{0}s", seconds);
            else if (Milliseconds > -60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}s", -seconds);
            else if (Milliseconds > -60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}m {1}s", -minutes, -seconds);
            else if (Milliseconds > -24 * 60 * 60 * 1000)
                return string.Format(CultureInfo.InvariantCulture, "-{0}h {1}m", -hours, -minutes);
            else
                return string.Format(CultureInfo.InvariantCulture, "-{0}d {1}h", -days, -hours);
        }

        public readonly int CompareTo(MetaDuration other) => Milliseconds.CompareTo(other.Milliseconds);

        int IComparable.CompareTo(object obj) => (obj is MetaDuration other) ? CompareTo(other) : 1;
    }
}
