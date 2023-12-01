// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Globalization;

namespace Metaplay.Core
{
    /// <summary>
    /// Represents a deterministic wall clock time. Intenal representation is milliseconds since
    /// Unix epoch (midnight on 1970/Jan/1 UTC) as int64.
    ///
    /// Roughly corresponds to <see cref="DateTime"/>, but can be used within deterministic logic.
    /// </summary>
    [MetaSerializable]
    public struct MetaTime : IEquatable<MetaTime>, IComparable<MetaTime>, IComparable
    {
        // \note Only intended for skipping forward in time for debugging purposes when running the server on localhost. Using this may cause any kind of undefined behavior.
        public static MetaDuration          DebugTimeOffset = MetaDuration.Zero;

        public static readonly MetaTime     Epoch           = new MetaTime(0);
        public static readonly DateTime     DateTimeEpoch   = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        /// <summary>
        /// Get the current time on this computer.
        /// </summary>
        /// <remarks>
        /// Do not use this in shared game logic, such as PlayerActions!
        /// The result depends on the computer's clock, and thus will be non-deterministic
        /// and different between the client and the server.
        /// In client-server shared game logic, use deterministic game logic time instead,
        /// such as <see cref="Metaplay.Core.Player.IPlayerModelBase.CurrentTime"/>.
        /// </remarks>
        public static MetaTime              Now             => FromDateTime(DateTime.UtcNow) + DebugTimeOffset;

        [MetaMember(1)] public long         MillisecondsSinceEpoch  { get; set; }

        private MetaTime(long millisecondsSinceEpoch)
        {
            MillisecondsSinceEpoch = millisecondsSinceEpoch;
        }

        public static MetaTime FromMillisecondsSinceEpoch(long millisecondsSinceEpoch) => new MetaTime(millisecondsSinceEpoch);
        public static MetaTime FromDateTime(DateTime dt) => new MetaTime((dt - DateTimeEpoch).Ticks / TimeSpan.TicksPerMillisecond);

        public readonly DateTime ToDateTime() => DateTimeEpoch + TimeSpan.FromTicks(MillisecondsSinceEpoch * TimeSpan.TicksPerMillisecond);

        public static MetaTime     operator +(MetaTime time, MetaDuration duration) => new MetaTime(time.MillisecondsSinceEpoch + duration.Milliseconds);
        public static MetaTime     operator -(MetaTime time, MetaDuration duration) => new MetaTime(time.MillisecondsSinceEpoch - duration.Milliseconds);
        public static MetaTime     operator +(MetaDuration duration, MetaTime time) => new MetaTime(time.MillisecondsSinceEpoch + duration.Milliseconds);
        public static MetaDuration operator -(MetaTime a, MetaTime b) => new MetaDuration(a.MillisecondsSinceEpoch - b.MillisecondsSinceEpoch);

        public static bool operator ==(MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch == b.MillisecondsSinceEpoch;
        public static bool operator !=(MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch != b.MillisecondsSinceEpoch;
        public static bool operator < (MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch < b.MillisecondsSinceEpoch;
        public static bool operator <=(MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch <= b.MillisecondsSinceEpoch;
        public static bool operator > (MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch > b.MillisecondsSinceEpoch;
        public static bool operator >=(MetaTime a, MetaTime b) => a.MillisecondsSinceEpoch >= b.MillisecondsSinceEpoch;

        public static MetaTime Min(MetaTime a, MetaTime b) => new MetaTime(System.Math.Min(a.MillisecondsSinceEpoch, b.MillisecondsSinceEpoch));
        public static MetaTime Max(MetaTime a, MetaTime b) => new MetaTime(System.Math.Max(a.MillisecondsSinceEpoch, b.MillisecondsSinceEpoch));

        public readonly bool Equals(MetaTime other) => MillisecondsSinceEpoch == other.MillisecondsSinceEpoch;

        public override readonly bool Equals(object obj)
        {
            if (obj is MetaTime other)
                return other.MillisecondsSinceEpoch == MillisecondsSinceEpoch;

            return false;
        }

        public override readonly int GetHashCode() => MillisecondsSinceEpoch.GetHashCode();

        public override readonly string ToString()
        {
            // \note returns time in UTC
            DateTime dt = ToDateTime();
            return string.Format(CultureInfo.InvariantCulture, "{0}-{1:D2}-{2:D2} {3:D2}:{4:D2}:{5:D2}.{6:D3} Z", dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
        }

        /// <summary>
        /// Convert to ISO 8601 formatted string (eg, "2021-05-25T17:29:45.4910594Z").
        /// </summary>
        /// <returns></returns>
        public readonly string ToISO8601()
        {
            return ToDateTime().ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Regex for the date part of the date-time config parsing syntax.
        /// For simplicity, this is overly permissive.
        /// Strict format checking happens with DateTime.TryParseExact, in
        /// <see cref="ConfigParse"/>.
        /// </summary>
        static readonly ConfigLexer.CustomTokenSpec s_dateConfigToken = new ConfigLexer.CustomTokenSpec(@"([0-9]|-)+", "Date part of MetaTime");
        /// <summary>
        /// Regex for the time-of-day part of the date-time config parsing syntax.
        /// This is overly permissive for two reasons: simplicity, and so that
        /// some ill-formed times error out instead of accepting a well-formed
        /// prefix and leaving some remaining input. (E.g. 12:34:56:78 should fail
        /// instead of parsing as 12:34:56 and leaving :78 as remaining input.)
        /// Strict format checking happens with DateTime.TryParseExact, in
        /// <see cref="ConfigParse"/>.
        /// </summary>
        static readonly ConfigLexer.CustomTokenSpec s_timeOfDayConfigToken = new ConfigLexer.CustomTokenSpec(@"([0-9]|:|\.)+", "Time-of-day part of MetaTime");

        /// <summary>
        /// Date format given to DateTime.TryParseExact.
        /// </summary>
        static readonly string s_dateFormat = "yyyy-M-d";

        /// <summary>
        /// Time-of-day formats given to DateTime.TryParseExact.
        /// </summary>
        static readonly string[] s_timeOfDayFormats = new string[]
        {
            "H:m",
            "H:m:s.FFF",
        };

        /// <summary>
        /// Parse a MetaTime from the given input.
        /// This expects date-time syntax like <c>2022-6-20 12:34:56.789</c>
        /// (the fractional-seconds part is optional).
        ///
        /// For backwards compatibility, this also supports the legacy
        /// syntax of integer seconds since Unix epoch, but only in a
        /// very limited form (the integer must be the only token in
        /// the input).
        /// </summary>
        public static MetaTime ConfigParse(ConfigLexer lexer)
        {
            // Kludge to support the legacy "integer seconds since Unix epoch" syntax.
            // To avoid polluting the date-time parsing logic more than necessary,
            // support only a very limited case of the legacy syntax: integer literal
            // followed by end-of-input. This supports an individual MetaTime being
            // parsed from a config sheet cell, which is likely the only place this
            // legacy syntax is used.
            bool isLegacyIntegerFormat;
            {
                ConfigLexer tmpLexer = new ConfigLexer(lexer);
                isLegacyIntegerFormat = tmpLexer.TryParseToken(ConfigLexer.TokenType.IntegerLiteral)
                                        && tmpLexer.IsAtEnd;
            }

            if (isLegacyIntegerFormat)
                return FromMillisecondsSinceEpoch(lexer.ParseLongLiteral() * 1000);

            // The legacy format has been dealt with, so proceed with expecting date-time format.

            string dateStr = lexer.TryParseCustomToken(s_dateConfigToken)
                             ?? throw new ParseError($"Failed to parse MetaTime. Expected either date-time format (e.g. 2022-6-20 12:34:56) or non-negative integer seconds since Unix epoch (legacy syntax). Input: {lexer.GetRemainingInputInfo()}");

            if (!DateTime.TryParseExact(dateStr, s_dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
                throw new ParseError($"MetaTime: Failed to parse a date from string \"{dateStr}\". Expected format {s_dateFormat}, with a 4-digit year, and 1 or 2-digit month and day.");

            string timeOfDayStr = lexer.TryParseCustomToken(s_timeOfDayConfigToken)
                                  ?? throw new ParseError($"MetaTime: Time-of-day missing after date \"{dateStr}\". Expected a time-of-day such as 12:34:56 . Input: {lexer.GetRemainingInputInfo()}");

            if (!DateTime.TryParseExact(timeOfDayStr, s_timeOfDayFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime timeOfDay))
            {
                throw new ParseError($"MetaTime: Failed to parse a time-of-day from string \"{timeOfDayStr}\". Expected one of these formats: {string.Join(" or ", s_timeOfDayFormats)} ." +
                    " The fractional seconds part is optional and has up to 3 digits if present. The other parts should have 1 or 2 digits each.");
            }

            if (timeOfDayStr.EndsWith(".", StringComparison.Ordinal))
                throw new ParseError($"MetaTime: In time-of-day \"{timeOfDayStr}\", the period after the seconds should be omitted if no digits come after it.");

            // \note Sub-millisecond part was ruled out by timeOfDayFormat, so it's zero.
            //       MetaTime supports only millisecond resolution (at the time of writing this).
            DateTime dateTime = new DateTime(date.Year, date.Month, date.Day, timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second, timeOfDay.Millisecond);

            return FromDateTime(dateTime);
        }

        /// <summary>
        /// Parse a datetime string.
        /// This is intended for parsing ISO 8601 format, though the current implementation
        /// just uses DateTime.Parse in a simple manner, allowing also other formats.
        /// </summary>
        /// <remarks>
        /// If the input does not have a time offset specifier ("Z" or numeric offset like "+5"),
        /// then it is assumed to be in UTC.
        /// Alternatively, such inputs could be disallowed, as it does not clearly
        /// represent a specific point in time (which MetaTime is intended to represent).
        /// The current behavior is permissive instead.
        /// </remarks>
        public static MetaTime Parse(string str)
        {
            DateTime dt = DateTime.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (dt.Kind == DateTimeKind.Local)
                dt = dt.ToUniversalTime();
            else if (dt.Kind == DateTimeKind.Unspecified)
            {
                // \note This is not necessary because MetaTime.FromDateTime would produce the
                //       correct result anyway. Just making it explicit here.
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            return FromDateTime(dt);
        }

        public readonly int CompareTo(MetaTime other) => MillisecondsSinceEpoch.CompareTo(other.MillisecondsSinceEpoch);

        int IComparable.CompareTo(object obj) => (obj is MetaTime other) ? CompareTo(other) : 1;
    }
}
