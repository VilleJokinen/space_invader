// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Schedule
{
    [MetaSerializable]
    public struct MetaCalendarDateTime
    {
        [MetaMember(1)] public int Year     { get; private set; }
        [MetaMember(2)] public int Month    { get; private set; }
        [MetaMember(3)] public int Day      { get; private set; }
        [MetaMember(4)] public int Hour     { get; private set; }
        [MetaMember(5)] public int Minute   { get; private set; }
        [MetaMember(6)] public int Second   { get; private set; }

        #region Config parsing helpers

        // Helper properties so that spreadsheet parsing can have Schedule.Start.Date and Schedule.Start.Time in separate columns,
        // instead of either a single Schedule.Start column or separate columns for Schedule.Start.Year, Schedule.Start.Month, ... .
        // \todo Also implement parsing for an entire MetaCalendarDateTime to give the choice of putting it in a single column.

        public MetaCalendarDate Date
        {
            get => new MetaCalendarDate(Year, Month, Day);
            private set { Year = value.Year; Month = value.Month; Day = value.Day; }
        }

        public MetaCalendarTime Time
        {
            get => new MetaCalendarTime(Hour, Minute, Second);
            private set { Hour = value.Hour; Minute = value.Minute; Second = value.Second; }
        }

        #endregion

        public MetaCalendarDateTime(int year, int month, int day, int hour, int minute, int second)
        {
            Year    = year;
            Month   = month;
            Day     = day;
            Hour    = hour;
            Minute  = minute;
            Second  = second;

            // Validate parameters by trying to create a corresponding DateTime.
            try
            {
                _ = ToDateTime();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new ArgumentOutOfRangeException("Invalid date and/or time parameters", ex);
            }
        }

        public static MetaCalendarDateTime FromDateTime(DateTime dateTime)
        {
            return new MetaCalendarDateTime(
                dateTime.Year,
                dateTime.Month,
                dateTime.Day,
                dateTime.Hour,
                dateTime.Minute,
                dateTime.Second);
        }

        public readonly DateTime ToDateTime()
        {
            if(Year == 0)
                return DateTime.MinValue;

            return new DateTime(
                Year,
                Month,
                Day,
                Hour,
                Minute,
                Second);
        }

        public override string ToString() => Invariant($"{nameof(MetaCalendarDateTime)}{{ {Year}-{Month:00}-{Day:00}, {Hour:00}:{Minute:00}:{Second:00} }}");
    }

    public struct MetaCalendarDate
    {
        public int Year     { get; private set; }
        public int Month    { get; private set; }
        public int Day      { get; private set; }

        public MetaCalendarDate(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public static MetaCalendarDate ConfigParse(ConfigLexer lexer)
        {
            // \todo Partial duplicate of MetaTime.ConfigParse. Implement MetaTime.ConfigParse using MetaCalendarDate.ConfigParse and MetaCalendarTime.ConfigParse.

            string dateStr = lexer.TryParseCustomToken(s_dateConfigToken)
                             ?? throw new ParseError($"Failed to parse date: expected format like 2022-6-20 . Input: {lexer.GetRemainingInputInfo()}");

            if (!DateTime.TryParseExact(dateStr, s_dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
                throw new ParseError($"Failed to parse date from string \"{dateStr}\". Expected format {s_dateFormat}, with a 4-digit year, and 1 or 2-digit month and day.");

            return new MetaCalendarDate(date.Year, date.Month, date.Day);
        }

        /// <summary>
        /// Regex for the date.
        /// For simplicity, this is overly permissive.
        /// Strict format checking happens with DateTime.TryParseExact, in
        /// <see cref="ConfigParse"/>.
        /// </summary>
        static readonly ConfigLexer.CustomTokenSpec s_dateConfigToken = new ConfigLexer.CustomTokenSpec(@"([0-9]|-)+", "Date");

        /// <summary>
        /// Date format given to DateTime.TryParseExact.
        /// </summary>
        static readonly string s_dateFormat = "yyyy-M-d";
    }

    public struct MetaCalendarTime
    {
        public int Hour     { get; private set; }
        public int Minute   { get; private set; }
        public int Second   { get; private set; }

        public MetaCalendarTime(int hour, int minute, int second)
        {
            Hour = hour;
            Minute = minute;
            Second = second;
        }

        public static MetaCalendarTime ConfigParse(ConfigLexer lexer)
        {
            // \todo Partial duplicate of MetaTime.ConfigParse. Implement MetaTime.ConfigParse using MetaCalendarDate.ConfigParse and MetaCalendarTime.ConfigParse.

            string timeStr = lexer.TryParseCustomToken(s_timeConfigToken)
                             ?? throw new ParseError($"Failed to parse time-of-day: expected format like 12:34:56 . Input: {lexer.GetRemainingInputInfo()}");

            if (!DateTime.TryParseExact(timeStr, s_timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time))
            {
                throw new ParseError($"Failed to parse a time-of-day from string \"{timeStr}\". Expected one of these formats: {string.Join(" or ", s_timeFormats)} ." +
                    " The fractional seconds part is optional and has up to 3 digits if present. The other parts should have 1 or 2 digits each.");
            }

            if (timeStr.EndsWith(".", StringComparison.Ordinal))
                throw new ParseError($"In time-of-day \"{timeStr}\", the period after the seconds should be omitted if no digits come after it.");

            // \note Sub-millisecond part was ruled out by s_timeFormats, so it's zero.
            //       MetaTime supports only millisecond resolution (at the time of writing this).

            // \todo Add milliseconds to MetaCalendarTime for consistency with MetaTime.
            //       Then, MetaTime.ConfigParse can also be implemented with MetaCalendarDate.ConfigParse and MetaCalendarTime.ConfigParse.
            if (time.Millisecond != 0)
                throw new NotImplementedException($"{nameof(MetaCalendarTime)} millisecond support not implemented: {timeStr}");

            return new MetaCalendarTime(time.Hour, time.Minute, time.Second);
        }

        /// <summary>
        /// Regex for time-of-day.
        /// This is overly permissive for two reasons: simplicity, and so that
        /// some ill-formed times error out instead of accepting a well-formed
        /// prefix and leaving some remaining input. (E.g. 12:34:56:78 should fail
        /// instead of parsing as 12:34:56 and leaving :78 as remaining input.)
        /// Strict format checking happens with DateTime.TryParseExact, in
        /// <see cref="ConfigParse"/>.
        /// </summary>
        static readonly ConfigLexer.CustomTokenSpec s_timeConfigToken = new ConfigLexer.CustomTokenSpec(@"([0-9]|:|\.)+", "Time-of-day");

        /// <summary>
        /// Time-of-day formats given to DateTime.TryParseExact.
        /// </summary>
        static readonly string[] s_timeFormats = new string[]
        {
            "H:m",
            "H:m:s.FFF",
        };
    }

    [MetaSerializable]
    [TypeConverter(typeof(MetaCalendarPeriodTypeConverter))]
    public struct MetaCalendarPeriod
    {
        [MetaMember(1)] public int Years;
        [MetaMember(2)] public int Months;
        [MetaMember(3)] public int Days;
        [MetaMember(4)] public int Hours;
        [MetaMember(5)] public int Minutes;
        [MetaMember(6)] public int Seconds;

        public MetaCalendarPeriod(int years, int months, int days, int hours, int minutes, int seconds)
        {
            Years   = years;
            Months  = months;
            Days    = days;
            Hours   = hours;
            Minutes = minutes;
            Seconds = seconds;
        }

        public readonly DateTime AddToDateTime(DateTime start)
        {
            return AddMultipliedToDateTime(start, multiplier: 1);
        }

        public readonly DateTime SubtractFromDateTime(DateTime start)
        {
            return AddMultipliedToDateTime(start, multiplier: -1);
        }

        public readonly DateTime AddMultipliedToDateTime(DateTime start, int multiplier)
        {
            return start.AddYears(multiplier * Years)
                        .AddMonths(multiplier * Months)
                        .AddDays((long)multiplier * (long)Days)
                        .AddHours((long)multiplier * (long)Hours)
                        .AddMinutes((long)multiplier * (long)Minutes)
                        .AddSeconds((long)multiplier * (long)Seconds);
        }

        public bool IsNone => Years     == 0
                           && Months    == 0
                           && Days      == 0
                           && Hours     == 0
                           && Minutes   == 0
                           && Seconds   == 0;

        public override string ToString() => Invariant($"{nameof(MetaCalendarPeriod)}{{ Years={Years}, Months={Months}, Days={Days}, Hours={Hours}, Minutes={Minutes}, Seconds={Seconds} }}");

        /// <summary>
        /// Calculate a rough lower estimate of the duration of the given period.
        /// The returned duration is no greater than the given period, ignoring leap seconds and the like.
        /// </summary>
        public MetaDuration RoughLowerEstimatedDuration()
        {
            return MetaDuration.FromSeconds(Seconds)
                + MetaDuration.FromMinutes(Minutes)
                + MetaDuration.FromHours(Hours)
                + MetaDuration.FromDays(Days)
                + MetaDuration.FromDays(28 * Months)
                + MetaDuration.FromDays(365 * Years);
        }

        /// <summary>
        /// Calculate a rough upper estimate of the duration of the given period.
        /// The returned duration is no lower than the given period, ignoring leap seconds and the like.
        /// </summary>
        public MetaDuration RoughUpperEstimatedDuration()
        {
            return MetaDuration.FromSeconds(Seconds)
                + MetaDuration.FromMinutes(Minutes)
                + MetaDuration.FromHours(Hours)
                + MetaDuration.FromDays(Days)
                + MetaDuration.FromDays(31 * Months)
                + MetaDuration.FromDays(366 * Years);
        }

        public static MetaCalendarPeriod operator +(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return new MetaCalendarPeriod(
                lhs.Years   + rhs.Years,
                lhs.Months  + rhs.Months,
                lhs.Days    + rhs.Days,
                lhs.Hours   + rhs.Hours,
                lhs.Minutes + rhs.Minutes,
                lhs.Seconds + rhs.Seconds);
        }

        public static MetaCalendarPeriod operator -(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return new MetaCalendarPeriod(
                lhs.Years   - rhs.Years,
                lhs.Months  - rhs.Months,
                lhs.Days    - rhs.Days,
                lhs.Hours   - rhs.Hours,
                lhs.Minutes - rhs.Minutes,
                lhs.Seconds - rhs.Seconds);
        }

        public static bool operator >(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return lhs.RoughLowerEstimatedDuration() > rhs.RoughLowerEstimatedDuration();
        }

        public static bool operator <(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return lhs.RoughLowerEstimatedDuration() < rhs.RoughLowerEstimatedDuration();
        }

        public static bool operator >=(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return lhs.RoughLowerEstimatedDuration() >= rhs.RoughLowerEstimatedDuration();
        }

        public static bool operator <=(MetaCalendarPeriod lhs, MetaCalendarPeriod rhs)
        {
            return lhs.RoughLowerEstimatedDuration() <= rhs.RoughLowerEstimatedDuration();
        }

        public static bool operator ==(MetaCalendarPeriod left, MetaCalendarPeriod right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MetaCalendarPeriod left, MetaCalendarPeriod right)
        {
            return !left.Equals(right);
        }

        public bool Equals(MetaCalendarPeriod other)
        {
            return Years == other.Years && Months == other.Months && Days == other.Days && Hours == other.Hours && Minutes == other.Minutes && Seconds == other.Seconds;
        }

        public override bool Equals(object obj)
        {
            return obj is MetaCalendarPeriod other && Equals(other);
        }
        public override int GetHashCode()
        {
            return Util.CombineHashCode(
                Years,
                Months,
                Days,
                Hours,
                Minutes,
                Seconds);
        }

        public static MetaCalendarPeriod ConfigParse(ConfigLexer lexer)
        {
            // \todo This is modified copypaste from ConfigParser.ParseMetaDurationUnitFormat.
            //       Share the implementation.

            int? years          = null;
            int? months         = null;
            int? days           = null;
            int? hours          = null;
            int? minutes        = null;
            F64? seconds        = null;

            while (true)
            {
                ConfigLexer.Token token = lexer.CurrentToken;

                if (token.Type == ConfigLexer.TokenType.IntegerLiteral)
                {
                    int     value   = lexer.ParseIntegerLiteral();
                    string  unit    = lexer.ParseIdentifier();

                    switch (unit)
                    {
                        case "y":   SetValueCheckNotAlready(ref years,      value,              "years");   break;
                        case "mo":  SetValueCheckNotAlready(ref months,     value,              "months");  break;
                        case "d":   SetValueCheckNotAlready(ref days,       value,              "days");    break;
                        case "h":   SetValueCheckNotAlready(ref hours,      value,              "hours");   break;
                        case "m":   SetValueCheckNotAlready(ref minutes,    value,              "minutes"); break;
                        case "s":   SetValueCheckNotAlready(ref seconds,    F64.FromInt(value), "seconds"); break;
                        default:
                            throw new ParseError($"Invalid time period unit '{unit}' (at value {value})");
                    }
                }
                else if (token.Type == ConfigLexer.TokenType.FloatLiteral)
                {
                    // \todo Make sure milliseconds do not suffer from imprecision due to F64.
                    //       Change to explicit integer parsing of the fractional part?
                    F64     value   = F64.Parse(lexer.GetTokenString(token));
                    lexer.Advance();
                    string  unit    = lexer.ParseIdentifier();

                    switch (unit)
                    {
                        case "s": SetValueCheckNotAlready(ref seconds, value, "seconds"); break;
                        default:
                            throw new ParseError($"Invalid time period unit '{unit}' for a non-integer value {value}. Non-integer values are only supported for the 's' unit.");
                    }
                }
                else
                    break;
            }

            if (!(years.HasValue || months.HasValue || days.HasValue || hours.HasValue || minutes.HasValue || seconds.HasValue))
                throw new ParseError("Time period must have at least one component");

            // \todo Add milliseconds to MetaCalendarPeriod for consistency with MetaDuration.
            //       Then, ConfigParser.ParseMetaDurationUnitFormat can also be implemented with MetaCalendarPeriod.ConfigParse.
            if (seconds.HasValue && F64.Fract(seconds.Value) != 0)
                throw new NotImplementedException($"{nameof(MetaCalendarPeriod)} millisecond support not implemented");

            MetaCalendarPeriod period = new MetaCalendarPeriod(years ?? 0, months ?? 0, days ?? 0, hours ?? 0, minutes ?? 0, F64.FloorToInt(seconds ?? F64.Zero));

            return period;
        }

        static void SetValueCheckNotAlready<T>(ref T? dst, T src, string name) where T : struct
        {
            if (dst.HasValue)
                throw new ParseError($"Value for {name} specified multiple times (previously {dst}, now {src})");
            dst = src;
        }
    }

    public class MetaCalendarPeriodTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            if (sourceType == typeof(string))
                return true;

            return base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            if (destinationType == typeof(string))
                return true;

            return base.CanConvertTo(context, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string str)
                return MetaCalendarPeriod.ConfigParse(new ConfigLexer(str));

            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
            {
                MetaCalendarPeriod period = (MetaCalendarPeriod)value;
                StringBuilder sb = new StringBuilder();
                if (period.Years > 0)
                    sb.Append(Invariant($"{period.Years}y "));
                if (period.Months > 0)
                    sb.Append(Invariant($"{period.Months}mo "));
                if (period.Days > 0)
                    sb.Append(Invariant($"{period.Days}d "));
                if (period.Hours > 0)
                    sb.Append(Invariant($"{period.Hours}h "));
                if (period.Minutes > 0)
                    sb.Append(Invariant($"{period.Minutes}m "));
                if (period.Seconds > 0)
                    sb.Append(Invariant($"{period.Seconds}s "));

                if (sb.Length > 0) // Remove last space
                    sb.Remove(sb.Length - 1, 1);
                return sb.ToString();
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
