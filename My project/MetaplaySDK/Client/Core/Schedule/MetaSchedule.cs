// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;

namespace Metaplay.Core.Schedule
{
    public struct MetaTimeRange : IEquatable<MetaTimeRange>
    {
        public MetaTime Start;
        public MetaTime End;

        public MetaTimeRange(MetaTime start, MetaTime end)
        {
            Start   = start;
            End     = end;
        }

        public readonly bool Contains(MetaTime time) => time >= Start && time < End;

        public readonly bool Equals(MetaTimeRange other) => Start == other.Start && End == other.End;
        public override readonly bool Equals(object obj) => obj is MetaTimeRange other && Equals(other);
        public override readonly int GetHashCode() => Util.CombineHashCode(Start.GetHashCode(), End.GetHashCode());
        public static bool operator ==(MetaTimeRange a, MetaTimeRange b) => a.Equals(b);
        public static bool operator !=(MetaTimeRange a, MetaTimeRange b) => !(a == b);

        public override readonly string ToString() => $"{nameof(MetaTimeRange)}{{ Start={Start}, End={End} }}";
    }

    public struct MetaScheduleOccasion
    {
        public MetaTimeRange    EnabledRange;
        public MetaTime         EndingSoonStartsAt;
        public MetaTimeRange    VisibleRange;

        public MetaScheduleOccasion(MetaTimeRange enabledRange, MetaTime endingSoonStartsAt, MetaTimeRange visibleRange)
        {
            EnabledRange = enabledRange;
            EndingSoonStartsAt = endingSoonStartsAt;
            VisibleRange = visibleRange;
        }

        public readonly bool IsEnabledAt(MetaTime time) => EnabledRange.Contains(time);
        public readonly bool IsVisibleAt(MetaTime time) => VisibleRange.Contains(time);
        public readonly bool IsPreviewedAt(MetaTime time) => IsVisibleAt(time) && time < EnabledRange.Start;
        public readonly bool IsReviewedAt(MetaTime time) => IsVisibleAt(time) && time >= EnabledRange.End;
        public readonly bool IsEndingSoonAt(MetaTime time) => IsEnabledAt(time) && time >= EndingSoonStartsAt;
    };

    /// <summary>
    /// Results of a query from <see cref="MetaScheduleBase"/>,
    /// about schedule occasions related to a specific moment and utc offset.
    /// </summary>
    public struct MetaScheduleOccasionsQueryResult
    {
        /// <summary> The previously-enabled occasion that is no longer enabled, if any. </summary>
        public MetaScheduleOccasion? PreviousEnabledOccasion;
        /// <summary> The currently-enabled occasion if any, and otherwise the next enabled occasion if any. </summary>
        public MetaScheduleOccasion? CurrentOrNextEnabledOccasion;

        public MetaScheduleOccasionsQueryResult(MetaScheduleOccasion? previousEnabledOccasion, MetaScheduleOccasion? currentOrNextEnabledOccasion)
        {
            PreviousEnabledOccasion = previousEnabledOccasion;
            CurrentOrNextEnabledOccasion = currentOrNextEnabledOccasion;
        }
    }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    [Config.ParseAsDerivedType(typeof(MetaRecurringCalendarSchedule))]
    public abstract class MetaScheduleBase
    {
        [MetaMember(100)] public MetaScheduleTimeMode TimeMode { get; private set; }

        public MetaScheduleBase(){ }
        public MetaScheduleBase(MetaScheduleTimeMode timeMode)
        {
            TimeMode = timeMode;
        }

        /// <summary> Whether the schedule is enabled at the given time. </summary>
        public bool IsEnabledAt(PlayerLocalTime currentTime)
            => TryGetCurrentOrNextEnabledOccasion(currentTime)?.IsEnabledAt(currentTime.Time) ?? false;

        /// <summary>
        /// Given the current time, return the currently-enabled occasion if any,
        /// and otherwise the next enabled occasion if any.
        /// </summary>
        public MetaScheduleOccasion? TryGetCurrentOrNextEnabledOccasion(PlayerLocalTime currentTime)
            => QueryOccasions(currentTime).CurrentOrNextEnabledOccasion;

        /// <summary>
        /// Query info about occasions near the given time.
        /// For details, see comments in <see cref="MetaScheduleOccasionsQueryResult"/>.
        /// </summary>
        public abstract MetaScheduleOccasionsQueryResult QueryOccasions(PlayerLocalTime currentTime);

        protected DateTime ToScheduleLocalDateTime(PlayerLocalTime time)
        {
            TimeSpan sinceEpoch             = MetaDurationToTimeSpan(time.Time - MetaTime.Epoch);
            DateTime utcDateTime            = DateTimeEpoch + sinceEpoch;
            DateTime scheduleLocalDateTime  = utcDateTime + MetaDurationToTimeSpan(ScheduleUtcOffset(time.UtcOffset));

            return scheduleLocalDateTime;
        }

        protected MetaTime ScheduleLocalDateTimeToMetaTime(DateTime scheduleLocalDateTime, MetaDuration playerLocalUtcOffset)
        {
            DateTime utcDateTime    = scheduleLocalDateTime - MetaDurationToTimeSpan(ScheduleUtcOffset(playerLocalUtcOffset));
            TimeSpan sinceEpoch     = utcDateTime - DateTimeEpoch;
            MetaTime time           = MetaTime.Epoch + TimeSpanToMetaDuration(sinceEpoch);

            return time;
        }

        protected struct DateTimeRange
        {
            public DateTime Start;
            public DateTime End;

            public DateTimeRange(DateTime start, DateTime end)
            {
                Start   = start;
                End     = end;
            }

            public readonly bool Contains(DateTime time) => time >= Start && time < End;
        };

        protected MetaTimeRange ScheduleLocalDateTimeRangeToTimeRange(DateTimeRange range, MetaDuration playerLocalUtcOffset)
        {
            return new MetaTimeRange(
                ScheduleLocalDateTimeToMetaTime(range.Start, playerLocalUtcOffset),
                ScheduleLocalDateTimeToMetaTime(range.End, playerLocalUtcOffset));
        }

        protected MetaDuration ScheduleUtcOffset(MetaDuration playerLocalUtcOffset)
        {
            return TimeMode == MetaScheduleTimeMode.Local
                   ? playerLocalUtcOffset
                   : MetaDuration.Zero;
        }

        static protected TimeSpan MetaDurationToTimeSpan(MetaDuration metaDuration)
        {
            return TimeSpan.FromTicks(metaDuration.Milliseconds * TimeSpan.TicksPerMillisecond);
        }

        static protected MetaDuration TimeSpanToMetaDuration(TimeSpan timeSpan)
        {
            return MetaDuration.FromMilliseconds(timeSpan.Ticks / TimeSpan.TicksPerMillisecond);
        }

        protected static readonly DateTime DateTimeEpoch = new DateTime(1970, 1, 1);
    }

    [MetaSerializable]
    public enum MetaScheduleTimeMode
    {
        Utc,
        Local,
    }

    [MetaSerializableDerived(1)]
    public class MetaRecurringCalendarSchedule : MetaScheduleBase
    {
        /// <summary> Start of the first enabled time range. </summary>
        [MetaMember(1)] public MetaCalendarDateTime             Start       { get; private set; }
        /// <summary> Duration of one enabled time range. </summary>
        [MetaMember(2)] public MetaCalendarPeriod               Duration    { get; private set; }
        /// <summary> "Ending soon" duration before an enabled time range ends. </summary>
        [MetaMember(7)] public MetaCalendarPeriod               EndingSoon  { get; private set; }
        /// <summary> "Visibility" duration before an enabled time range starts. </summary>
        [MetaMember(3)] public MetaCalendarPeriod               Preview     { get; private set; }
        /// <summary> "Visibility" duration after an enabled time range ends. </summary>
        [MetaMember(4)] public MetaCalendarPeriod               Review      { get; private set; }
        [MetaMember(5)] public MetaCalendarPeriod?              Recurrence  { get; private set; } = null;
        [MetaMember(6)] public int?                             NumRepeats  { get; private set; }

        MetaRecurringCalendarSchedule(){ }
        public MetaRecurringCalendarSchedule(
            MetaScheduleTimeMode timeMode,
            MetaCalendarDateTime start,
            MetaCalendarPeriod duration,
            MetaCalendarPeriod endingSoon,
            MetaCalendarPeriod preview,
            MetaCalendarPeriod review,
            MetaCalendarPeriod? recurrence,
            int? numRepeats)
            : base(timeMode)
        {
            numRepeats = numRepeats < 0 ? (int?)null : numRepeats; // \note A bit permissive: allows also -1 to mean no repeat limit, even though null would be cleaner

            // Best-effort sanity checks for the various periods.
            // Based on conservative estimates, mainly intended to guard against obvious typos in configs.
            MetaDuration durationRoughUpper   = duration.RoughUpperEstimatedDuration();
            MetaDuration endingSoonRoughUpper = endingSoon.RoughUpperEstimatedDuration();
            MetaDuration endingSoonRoughLower = endingSoon.RoughLowerEstimatedDuration();
            MetaDuration previewRoughUpper    = preview.RoughUpperEstimatedDuration();
            MetaDuration reviewRoughUpper     = review.RoughUpperEstimatedDuration();

            if (durationRoughUpper <= MetaDuration.Zero)
                throw new ArgumentException($"Duration cannot be zero or negative: {duration}", nameof(duration));
            if (endingSoonRoughUpper < MetaDuration.Zero)
                throw new ArgumentException($"'Ending soon' period cannot be negative: {endingSoon}", nameof(endingSoon));
            if (previewRoughUpper < MetaDuration.Zero)
                throw new ArgumentException($"Preview cannot be negative: {preview}", nameof(preview));
            if (reviewRoughUpper < MetaDuration.Zero)
                throw new ArgumentException($"Review cannot be negative: {review}", nameof(review));
            if (endingSoonRoughLower > durationRoughUpper)
                throw new ArgumentException($"'Ending soon' period cannot be longer than duration: ending soon is {endingSoon}, duration is {duration}", nameof(endingSoon));

            if (recurrence.HasValue)
            {
                if (recurrence.Value.IsNone)
                    throw new ArgumentException($"Recurrence period must be non-zero: {recurrence.Value}", nameof(recurrence));
                else if (!IsValidRecurrencePeriod(recurrence.Value))
                    throw new ArgumentException($"Recurrence period must have exactly one non-zero unit, and must be positive: {recurrence.Value}", nameof(recurrence));

                // Best-effort check that preview+duration+review does not exceed recurrence.
                // Based on lower estimate of preview+duration+review and upper estimate of recurrence - might permit inputs that ideally shouldn't be permitted.
                // This is mainly intended to guard against obvious typos in configs.

                MetaDuration previewRoughLower    = preview.RoughLowerEstimatedDuration();
                MetaDuration durationRoughLower   = duration.RoughLowerEstimatedDuration();
                MetaDuration reviewRoughLower     = review.RoughLowerEstimatedDuration();
                MetaDuration recurrenceRoughUpper = recurrence.Value.RoughUpperEstimatedDuration();

                if (previewRoughLower + durationRoughLower + reviewRoughLower > recurrenceRoughUpper)
                    throw new ArgumentException($"Preview + duration + review cannot be longer than recurrence: preview={preview}, duration={duration}, review={review}, recurrence={recurrence.Value}", nameof(recurrence));
            }
            else
            {
                if (numRepeats.HasValue)
                    throw new ArgumentException($"NumRepeats must be omitted if recurrence is omitted", nameof(numRepeats));
            }

            Start       = start;
            Duration    = duration;
            EndingSoon  = endingSoon;
            Preview     = preview;
            Review      = review;
            Recurrence  = recurrence;
            NumRepeats  = numRepeats;
        }

        struct EnabledDateTimeRangesQueryResult
        {
            public DateTimeRange? Previous;
            public DateTimeRange? CurrentOrNext;

            public EnabledDateTimeRangesQueryResult(DateTimeRange? previous, DateTimeRange? currentOrNext)
            {
                Previous = previous;
                CurrentOrNext = currentOrNext;
            }
        }

        public override MetaScheduleOccasionsQueryResult QueryOccasions(PlayerLocalTime currentTime)
        {
            EnabledDateTimeRangesQueryResult result = QueryEnabledDateTimeRanges(ToScheduleLocalDateTime(currentTime));

            return new MetaScheduleOccasionsQueryResult(
                previousEnabledOccasion:        EnabledDateTimeRangeToOccasionMaybe(result.Previous, currentTime.UtcOffset),
                currentOrNextEnabledOccasion:   EnabledDateTimeRangeToOccasionMaybe(result.CurrentOrNext, currentTime.UtcOffset));
        }

        MetaScheduleOccasion? EnabledDateTimeRangeToOccasionMaybe(DateTimeRange? enabledDateTimeRangeMaybe, MetaDuration utcOffset)
        {
            if (enabledDateTimeRangeMaybe.HasValue)
                return EnabledDateTimeRangeToOccasion(enabledDateTimeRangeMaybe.Value, utcOffset);
            else
                return null;
        }

        MetaScheduleOccasion EnabledDateTimeRangeToOccasion(DateTimeRange enabledDateTimeRange, MetaDuration utcOffset)
        {
            DateTime        endingSoonStartDateTime = EndingSoon.SubtractFromDateTime(enabledDateTimeRange.End);
            DateTime        previewStart            = Preview.SubtractFromDateTime(enabledDateTimeRange.Start);
            DateTime        reviewEnd               = Review.AddToDateTime(enabledDateTimeRange.End);
            DateTimeRange   visibleDateTimeRange    = new DateTimeRange(previewStart, reviewEnd);

            return new MetaScheduleOccasion(
                enabledRange:       ScheduleLocalDateTimeRangeToTimeRange(enabledDateTimeRange, utcOffset),
                endingSoonStartsAt: ScheduleLocalDateTimeToMetaTime(endingSoonStartDateTime, utcOffset),
                visibleRange:       ScheduleLocalDateTimeRangeToTimeRange(visibleDateTimeRange, utcOffset));
        }

        EnabledDateTimeRangesQueryResult QueryEnabledDateTimeRanges(DateTime now)
        {
            DateTime firstStart = Start.ToDateTime();

            if (!Recurrence.HasValue)
            {
                DateTimeRange range = RangeStartingAt(firstStart);

                if (now < range.End)
                    return new EnabledDateTimeRangesQueryResult(previous: null, currentOrNext: range);
                else
                    return new EnabledDateTimeRangesQueryResult(previous: range, currentOrNext: null);
            }
            else
            {
                /*

                In a recurring schedule, the enabled ranges repeat at recurrence intervals that are of
                constant period (period as in MetaCalendarPeriod), though not necessarily constant-duration.

                Note: enabled ranges are assumed to not overlap each other. How this method behaves if they do
                is somewhat unspecified, though it shouldn't behave entirely unreasonably.

                Here we consider the timeline to be partitioned into contiguous "cycles", where the start datetimes of
                consecutive cycles differ by the recurrence interval. The exact meaning of "cycle" depends on
                the recurrence; see ComputeCycleIndex for details.

                The cycles are arranged such that each cycle contains the starting datetime of exactly one enabled range,
                except that cycles prior to the one containing the schedule's Start time contain no enabled ranges (but
                this is handled as a special case, so we can ignore it for the rest of this explanation),
                and cycles beyond the repeat limit contain no start datetimes of enabled ranges.

                The "cycle index" of a cycle is the chronological index of the enabled range whose start datetime
                it contains (or would contain if there was no repeat limit).

                Thus, the current or next enabled range (if any) has its start datetime in cycle with index C-1, C, or C+1,
                where C is the index of the cycle containing the current datetime. The code following this comment
                considers the relevant enabled ranges that start in these cycles.

                The return value contains also the previously-enabled range, i.e. the range that was previously enabled
                but is no longed enabled. That, if any, is equal to the range with the index one less than
                the index of the current range.

                Illustrations of recurring schedules on a timeline:
                  Cycle endpoints are marked with |
                  Enabled range starts are marked with ^
                  Enabled range ends are marked with '
                  Enabled ranges are otherwise marked with _

                Schedule with each enabled range falling entirely within one cycle:

                |-------------------------|-------------------------|-------------------------|
                   ^________________'        ^________________'        ^________________'

                Schedule with each enabled range overlapping two cycles:

                |-------------------------|-------------------------|-------------------------|
                _____'        ^________________'        ^________________'        ^___________

                Schedule with some of the enabled ranges falling entirely within one cycle,
                and some of them overlapping two cycles:

                |-------------------------|-------------------------|-------------------------|
                ___'          ^________'                   ^___________'     ^____________'


                In each scenario, whatever the current time, the relevant ranges to consider are found from the
                current cycle or its neighbor cycles.

                */

                // Special case: no enabled ranges at all.
                if (NumRepeats == 0)
                    return new EnabledDateTimeRangesQueryResult(previous: null, currentOrNext: null);

                // Special case: we're not at the first enabled range yet.
                if (now < firstStart)
                    return new EnabledDateTimeRangesQueryResult(previous: null, currentOrNext: GetEnabledRange(0));

                // Special case: we're past the end of the last enabled range.
                if (NumRepeats.HasValue)
                {
                    DateTimeRange lastRange = GetEnabledRange(NumRepeats.Value - 1);
                    if (now >= lastRange.End)
                        return new EnabledDateTimeRangesQueryResult(previous: lastRange, currentOrNext: null);
                }

                // Having made the special case checks above, we now know the current time
                // is at least the start time of the first enabled range,
                // and less than the end time of the last enabled range.
                // Therefore, we know that there does exist a current or next enabled range.

                MetaCalendarPeriod  recurrence                          = Recurrence.Value;
                int                 currentCycleIndex                   = ComputeCycleIndex(firstStart, recurrence, now);
                DateTimeRange?      rangeStartingInCurrentCycleMaybe    = TryGetEnabledRange(currentCycleIndex);

                if (!rangeStartingInCurrentCycleMaybe.HasValue || now < rangeStartingInCurrentCycleMaybe.Value.Start)
                {
                    // Current cycle's enabled range doesn't exist, or hasn't started yet.
                    // Check if previous cycle's enabled range is still ongoing.
                    // At this point, the enabled range with the previous cycle's index is known to exist
                    // (because otherwise current time would be earlier than the first enabled range,
                    // but we already checked for that).

                    int             previousCycleIndex              = currentCycleIndex - 1;
                    DateTimeRange   rangeStartingInPreviousCycle    = GetEnabledRange(previousCycleIndex);

                    if (now < rangeStartingInPreviousCycle.End)
                    {
                        // Previous cycle's range is still ongoing, so it is the currently-enabled range.
                        // The one previous to that, if any, is the previously-enabled range.
                        return new EnabledDateTimeRangesQueryResult(
                            previous:       TryGetEnabledRange(previousCycleIndex-1),
                            currentOrNext:  rangeStartingInPreviousCycle);
                    }
                    else
                    {
                        // Previous cycle's range has ended, so current cycle's range is the next enabled range.
                        // Previous cycle's range is the previously-enabled range.
                        return new EnabledDateTimeRangesQueryResult(
                            previous:       rangeStartingInPreviousCycle,
                            currentOrNext:  rangeStartingInCurrentCycleMaybe.Value);
                    }
                }
                else
                {
                    // Current cycle's enabled range has started.
                    // Check if it's still ongoing.

                    DateTimeRange rangeStartingInCurrentCycle = rangeStartingInCurrentCycleMaybe.Value;

                    if (now < rangeStartingInCurrentCycle.End)
                    {
                        // Current cycle's range is still ongoing, so it is the currently-enabled range.
                        // Previous cycle's range, if any, is the previously-enabled range.
                        return new EnabledDateTimeRangesQueryResult(
                            previous:       TryGetEnabledRange(currentCycleIndex-1),
                            currentOrNext:  rangeStartingInCurrentCycle);
                    }
                    else
                    {
                        // Current cycle's range has ended, so next cycle's range is the next enabled range.
                        // Current cycle's range is the previously-enabled range.
                        return new EnabledDateTimeRangesQueryResult(
                            previous:       rangeStartingInCurrentCycle,
                            currentOrNext:  GetEnabledRange(currentCycleIndex + 1));
                    }
                }
            }
        }

        /// <summary>
        /// Compute the index of the cycle at the datetime indicated by <paramref name="now"/>.
        /// The first (i.e. index 0) cycle is the one containing <paramref name="origin"/>.
        /// The boundaries of cycles depend on <paramref name="origin"/> and <paramref name="recurrence"/>.
        /// </summary>
        private static int ComputeCycleIndex(DateTime origin, MetaCalendarPeriod recurrence, DateTime now)
        {
            if (recurrence.Years != 0)
            {
                // N-year recurrence: Each cycle spans N consecutive calendar years, with the first cycle starting at the beginning of the calendar year containing origin.
                return (now.Year - origin.Year) / recurrence.Years;
            }

            if (recurrence.Months != 0)
            {
                // N-month recurrence: Each cycle spans N consecutive calendar months, with the first cycle starting at the beginning of the calendar month containing origin.
                return ((now.Year - origin.Year)*12 + (now.Month - origin.Month)) / recurrence.Months;
            }

            if (recurrence.Days != 0)
            {
                // N-day recurrence: Each cycle spans N days, with the first cycle starting at origin.
                return (now - origin).Days / recurrence.Days;
            }

            if (recurrence.Hours != 0)
            {
                // N-hour recurrence: Each cycle spans N hours, with the first cycle starting at origin.
                TimeSpan elapsed = now - origin;
                long elapsedTotalHours = (long)elapsed.Days*24L
                                       + (long)elapsed.Hours;
                return (int)(elapsedTotalHours / recurrence.Hours);
            }

            if (recurrence.Minutes != 0)
            {
                // N-minute recurrence: Each cycle spans N minutes, with the first cycle starting at origin.
                TimeSpan elapsed = now - origin;
                long elapsedTotalMinutes = (long)elapsed.Days*(24L*60L)
                                         + (long)elapsed.Hours*60L
                                         + (long)elapsed.Minutes;
                return (int)(elapsedTotalMinutes / recurrence.Minutes);
            }

            if (recurrence.Seconds != 0)
            {
                // N-second recurrence: Each cycle spans N seconds, with the first cycle starting at origin.
                TimeSpan elapsed = now - origin;
                long elapsedTotalSeconds = (long)elapsed.Days*(24L*60L*60L)
                                         + (long)elapsed.Hours*(60L*60L)
                                         + (long)elapsed.Minutes*60L
                                         + (long)elapsed.Seconds;
                return (int)(elapsedTotalSeconds / recurrence.Seconds);
            }

            throw new ArgumentException($"Invalid recurrence period; recurrence period must have exactly one non-zero unit, and must be positive: {recurrence}", nameof(recurrence));
        }

        /// <summary>
        /// Calculate the equivalent of <paramref name="epoch"/> in the cycle specified by <paramref name="cycleIndex"/>,
        /// in the cycle timeline defined by <paramref name="epoch"/> and <paramref name="recurrence"/>.
        /// </summary>
        private static DateTime ShiftEpochToCycle(DateTime epoch, MetaCalendarPeriod recurrence, int cycleIndex)
        {
            // \note See ComputeCycleIndex for the descriptions of the cycle timelines for the various recurrences.
            return recurrence.AddMultipliedToDateTime(epoch, multiplier: cycleIndex);
        }

        private static bool IsValidRecurrencePeriod(MetaCalendarPeriod period)
        {
            if (period.Years < 0)   return false;
            if (period.Months < 0)  return false;
            if (period.Days < 0)    return false;
            if (period.Hours < 0)   return false;
            if (period.Minutes < 0) return false;
            if (period.Seconds < 0) return false;

            int numNonZeros = (period.Years != 0   ? 1 : 0)
                            + (period.Months != 0  ? 1 : 0)
                            + (period.Days != 0    ? 1 : 0)
                            + (period.Hours != 0   ? 1 : 0)
                            + (period.Minutes != 0 ? 1 : 0)
                            + (period.Seconds != 0 ? 1 : 0);

            if (numNonZeros != 1)
                return false;

            return true;
        }



        private DateTimeRange RangeStartingAt(DateTime start)
        {
            return new DateTimeRange(
                start,
                Duration.AddToDateTime(start));
        }

        DateTimeRange GetEnabledRange(int rangeIndex)
        {
            return TryGetEnabledRange(rangeIndex).Value;
        }

        DateTimeRange? TryGetEnabledRange(int rangeIndex)
        {
            if (!Recurrence.HasValue)
            {
                if (rangeIndex == 0)
                    return RangeStartingAt(Start.ToDateTime());
                else
                    return null;
            }
            else
            {
                if (rangeIndex < 0)
                    return null;
                else if (NumRepeats.HasValue && rangeIndex >= NumRepeats.Value)
                    return null;
                else
                    return RangeStartingAt(ShiftEpochToCycle(Start.ToDateTime(), Recurrence.Value, rangeIndex));
            }
        }
    }
}
