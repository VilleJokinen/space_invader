// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Cloud.Tests
{
    [TestFixture]
    public static class ScheduleTests
    {
        [Test]
        public static void TestOnceUtc()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 2, Months = 3, Days = 4, Hours = 10, Minutes = 20, Seconds = 30 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     null,
                numRepeats:     -1);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2022, 5, 14, 11, 22, 33) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges, playerLocalUtcOffsetHours: 2);
        }

        [Test]
        public static void TestOnceLocal()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Local,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 2, Months = 3, Days = 4, Hours = 10, Minutes = 20, Seconds = 30 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     null,
                numRepeats:     -1);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 9, 23, 2, 3), End = new DateTime(2022, 5, 14, 9, 22, 33) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges, playerLocalUtcOffsetHours: 2);
        }

        [Test]
        public static void TestYearly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 10 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 2, 10, 1, 2, 3), End = new DateTime(2022, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 2, 10, 1, 2, 3), End = new DateTime(2023, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 2, 10, 1, 2, 3), End = new DateTime(2024, 12, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiYearly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 4, Months = 10 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 5 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2024, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2029, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2030, 2, 10, 1, 2, 3), End = new DateTime(2034, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2035, 2, 10, 1, 2, 3), End = new DateTime(2039, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2040, 2, 10, 1, 2, 3), End = new DateTime(2044, 12, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestYearlyWrapping()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 6 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2021, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 7, 10, 1, 2, 3), End = new DateTime(2022, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 7, 10, 1, 2, 3), End = new DateTime(2023, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 7, 10, 1, 2, 3), End = new DateTime(2024, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 7, 10, 1, 2, 3), End = new DateTime(2025, 1, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestYearlyFullYearNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 1, 1, 0, 0, 0),
                duration:       new MetaCalendarPeriod{ Years = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 1, 1), End = new DateTime(2021, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 1), End = new DateTime(2022, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2022, 1, 1), End = new DateTime(2023, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2023, 1, 1), End = new DateTime(2024, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2024, 1, 1), End = new DateTime(2025, 1, 1) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiYearlyFullYearsNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 1, 1, 0, 0, 0),
                duration:       new MetaCalendarPeriod{ Years = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 5 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 1, 1), End = new DateTime(2025, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2025, 1, 1), End = new DateTime(2030, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2030, 1, 1), End = new DateTime(2035, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2035, 1, 1), End = new DateTime(2040, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2040, 1, 1), End = new DateTime(2045, 1, 1) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestYearlyFullYear()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2021, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 7, 10, 1, 2, 3), End = new DateTime(2022, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 7, 10, 1, 2, 3), End = new DateTime(2023, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 7, 10, 1, 2, 3), End = new DateTime(2024, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 7, 10, 1, 2, 3), End = new DateTime(2025, 7, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiYearlyFullYears()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 5 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2025, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 7, 10, 1, 2, 3), End = new DateTime(2030, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2030, 7, 10, 1, 2, 3), End = new DateTime(2035, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2035, 7, 10, 1, 2, 3), End = new DateTime(2040, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2040, 7, 10, 1, 2, 3), End = new DateTime(2045, 7, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestYearlyFullYearMinusDays()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 1, Days = -5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2021, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 7, 10, 1, 2, 3), End = new DateTime(2022, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 7, 10, 1, 2, 3), End = new DateTime(2023, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 7, 10, 1, 2, 3), End = new DateTime(2024, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 7, 10, 1, 2, 3), End = new DateTime(2025, 7, 5, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiYearlyFullYearsMinusDays()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 5, Days = -5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 5 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2025, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 7, 10, 1, 2, 3), End = new DateTime(2030, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2030, 7, 10, 1, 2, 3), End = new DateTime(2035, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2035, 7, 10, 1, 2, 3), End = new DateTime(2040, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2040, 7, 10, 1, 2, 3), End = new DateTime(2045, 7, 5, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestYearlyDaysOverLeapday()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Days = 25 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 1 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 3, 6, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 2, 10, 1, 2, 3), End = new DateTime(2022, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 2, 10, 1, 2, 3), End = new DateTime(2023, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 2, 10, 1, 2, 3), End = new DateTime(2024, 3, 6, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiYearlyDaysOverLeapday()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Years = 4, Days = 25 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Years = 5 },
                numRepeats:     5);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2024, 3, 6, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2029, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2030, 2, 10, 1, 2, 3), End = new DateTime(2034, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2035, 2, 10, 1, 2, 3), End = new DateTime(2039, 3, 7, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2040, 2, 10, 1, 2, 3), End = new DateTime(2044, 3, 6, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Days = 15 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 2, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 3, 10, 1, 2, 3), End = new DateTime(2020, 3, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 4, 10, 1, 2, 3), End = new DateTime(2020, 4, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 5, 10, 1, 2, 3), End = new DateTime(2020, 5, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 10, 1, 2, 3), End = new DateTime(2020, 6, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 7, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 10, 1, 2, 3), End = new DateTime(2020, 8, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 10, 1, 2, 3), End = new DateTime(2020, 9, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 10, 1, 2, 3), End = new DateTime(2020, 10, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 10, 1, 2, 3), End = new DateTime(2020, 11, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2020, 12, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 10, 1, 2, 3), End = new DateTime(2021, 1, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 2, 25, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiMonthly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 4, Days = 15 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 5 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 6, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 11, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 4, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 5, 10, 1, 2, 3), End = new DateTime(2021, 9, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 10, 10, 1, 2, 3), End = new DateTime(2022, 2, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 3, 10, 1, 2, 3), End = new DateTime(2022, 7, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 8, 10, 1, 2, 3), End = new DateTime(2022, 12, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 1, 10, 1, 2, 3), End = new DateTime(2023, 5, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 6, 10, 1, 2, 3), End = new DateTime(2023, 10, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 11, 10, 1, 2, 3), End = new DateTime(2024, 3, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 4, 10, 1, 2, 3), End = new DateTime(2024, 8, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 9, 10, 1, 2, 3), End = new DateTime(2025, 1, 25, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2025, 6, 25, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthlyWrapping()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 20, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Days = 15 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 20, 1, 2, 3), End = new DateTime(2020, 3, 6, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 3, 20, 1, 2, 3), End = new DateTime(2020, 4, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 4, 20, 1, 2, 3), End = new DateTime(2020, 5, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 5, 20, 1, 2, 3), End = new DateTime(2020, 6, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 20, 1, 2, 3), End = new DateTime(2020, 7, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 20, 1, 2, 3), End = new DateTime(2020, 8, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 20, 1, 2, 3), End = new DateTime(2020, 9, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 20, 1, 2, 3), End = new DateTime(2020, 10, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 20, 1, 2, 3), End = new DateTime(2020, 11, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 20, 1, 2, 3), End = new DateTime(2020, 12, 5, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 20, 1, 2, 3), End = new DateTime(2021, 1, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 20, 1, 2, 3), End = new DateTime(2021, 2, 4, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 20, 1, 2, 3), End = new DateTime(2021, 3, 7, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthlyFullMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 8, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 10, 1, 2, 3), End = new DateTime(2020, 9, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 10, 1, 2, 3), End = new DateTime(2020, 10, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 10, 1, 2, 3), End = new DateTime(2020, 11, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 10, 1, 2, 3), End = new DateTime(2020, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 10, 1, 2, 3), End = new DateTime(2021, 2, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 3, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 3, 10, 1, 2, 3), End = new DateTime(2021, 4, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 4, 10, 1, 2, 3), End = new DateTime(2021, 5, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 5, 10, 1, 2, 3), End = new DateTime(2021, 6, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 6, 10, 1, 2, 3), End = new DateTime(2021, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 7, 10, 1, 2, 3), End = new DateTime(2021, 8, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiMonthlyFullMonths()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 7, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 5 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 12, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 5, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 5, 10, 1, 2, 3), End = new DateTime(2021, 10, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2021, 10, 10, 1, 2, 3), End = new DateTime(2022, 3, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 3, 10, 1, 2, 3), End = new DateTime(2022, 8, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2022, 8, 10, 1, 2, 3), End = new DateTime(2023, 1, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 1, 10, 1, 2, 3), End = new DateTime(2023, 6, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 6, 10, 1, 2, 3), End = new DateTime(2023, 11, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2023, 11, 10, 1, 2, 3), End = new DateTime(2024, 4, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 4, 10, 1, 2, 3), End = new DateTime(2024, 9, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2024, 9, 10, 1, 2, 3), End = new DateTime(2025, 2, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2025, 7, 10, 1, 2, 3) },
                new DateTimeRange{ Start = new DateTime(2025, 7, 10, 1, 2, 3), End = new DateTime(2025, 12, 10, 1, 2, 3) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthlyUntilEndOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 1, Days = -9, Hours = -1, Minutes = -2, Seconds = -3 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 3, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 3, 10, 1, 2, 3), End = new DateTime(2020, 4, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 4, 10, 1, 2, 3), End = new DateTime(2020, 5, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 5, 10, 1, 2, 3), End = new DateTime(2020, 6, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 10, 1, 2, 3), End = new DateTime(2020, 7, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 8, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 10, 1, 2, 3), End = new DateTime(2020, 9, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 10, 1, 2, 3), End = new DateTime(2020, 10, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 10, 1, 2, 3), End = new DateTime(2020, 11, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 10, 1, 2, 3), End = new DateTime(2020, 12, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 1, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 10, 1, 2, 3), End = new DateTime(2021, 2, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 3, 1, 0, 0, 0) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiMonthlyUntilEndOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 5, Days = -9, Hours = -1, Minutes = -2, Seconds = -3 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 5 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 7, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 12, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 5, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 5, 10, 1, 2, 3), End = new DateTime(2021, 10, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 10, 10, 1, 2, 3), End = new DateTime(2022, 3, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2022, 3, 10, 1, 2, 3), End = new DateTime(2022, 8, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2022, 8, 10, 1, 2, 3), End = new DateTime(2023, 1, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 1, 10, 1, 2, 3), End = new DateTime(2023, 6, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 6, 10, 1, 2, 3), End = new DateTime(2023, 11, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 11, 10, 1, 2, 3), End = new DateTime(2024, 4, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2024, 4, 10, 1, 2, 3), End = new DateTime(2024, 9, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2024, 9, 10, 1, 2, 3), End = new DateTime(2025, 2, 1, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2025, 7, 1, 0, 0, 0) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthlyUntilDaysBeforeEndOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 1, Days = -11, Hours = -1, Minutes = -2, Seconds = -3 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 2, 28, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 3, 10, 1, 2, 3), End = new DateTime(2020, 3, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 4, 10, 1, 2, 3), End = new DateTime(2020, 4, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 5, 10, 1, 2, 3), End = new DateTime(2020, 5, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 10, 1, 2, 3), End = new DateTime(2020, 6, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 7, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 10, 1, 2, 3), End = new DateTime(2020, 8, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 10, 1, 2, 3), End = new DateTime(2020, 9, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 10, 1, 2, 3), End = new DateTime(2020, 10, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 10, 1, 2, 3), End = new DateTime(2020, 11, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2020, 12, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 10, 1, 2, 3), End = new DateTime(2021, 1, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 10, 1, 2, 3), End = new DateTime(2021, 2, 27, 0, 0, 0) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiMonthlyUntilDaysBeforeEndOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 10, 1, 2, 3),
                duration:       new MetaCalendarPeriod{ Months = 5, Days = -11, Hours = -1, Minutes = -2, Seconds = -3 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 5 },
                numRepeats:     13);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 2, 10, 1, 2, 3), End = new DateTime(2020, 6, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 10, 1, 2, 3), End = new DateTime(2020, 11, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 10, 1, 2, 3), End = new DateTime(2021, 4, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 5, 10, 1, 2, 3), End = new DateTime(2021, 9, 29, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2021, 10, 10, 1, 2, 3), End = new DateTime(2022, 2, 27, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2022, 3, 10, 1, 2, 3), End = new DateTime(2022, 7, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2022, 8, 10, 1, 2, 3), End = new DateTime(2022, 12, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 1, 10, 1, 2, 3), End = new DateTime(2023, 5, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 6, 10, 1, 2, 3), End = new DateTime(2023, 10, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2023, 11, 10, 1, 2, 3), End = new DateTime(2024, 3, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2024, 4, 10, 1, 2, 3), End = new DateTime(2024, 8, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2024, 9, 10, 1, 2, 3), End = new DateTime(2025, 1, 30, 0, 0, 0) },
                new DateTimeRange{ Start = new DateTime(2025, 2, 10, 1, 2, 3), End = new DateTime(2025, 6, 29, 0, 0, 0) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMonthlyLastDayOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 1, 31, 0, 0, 0),
                duration:       new MetaCalendarPeriod{ Days = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 1 },
                numRepeats:     14);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 1, 31), End = new DateTime(2020, 2, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 2, 29), End = new DateTime(2020, 3, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 3, 31), End = new DateTime(2020, 4, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 4, 30), End = new DateTime(2020, 5, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 5, 31), End = new DateTime(2020, 6, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 30), End = new DateTime(2020, 7, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 7, 31), End = new DateTime(2020, 8, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 8, 31), End = new DateTime(2020, 9, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 9, 30), End = new DateTime(2020, 10, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 10, 31), End = new DateTime(2020, 11, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 30), End = new DateTime(2020, 12, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 12, 31), End = new DateTime(2021, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2021, 1, 31), End = new DateTime(2021, 2, 1) },
                new DateTimeRange{ Start = new DateTime(2021, 2, 28), End = new DateTime(2021, 3, 1) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiMonthlyLastDayOfMonth()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 1, 31, 0, 0, 0),
                duration:       new MetaCalendarPeriod{ Days = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Months = 5 },
                numRepeats:     14);

            List<DateTimeRange> referenceRanges = new List<DateTimeRange>
            {
                new DateTimeRange{ Start = new DateTime(2020, 1, 31), End = new DateTime(2020, 2, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 6, 30), End = new DateTime(2020, 7, 1) },
                new DateTimeRange{ Start = new DateTime(2020, 11, 30), End = new DateTime(2020, 12, 1) },
                new DateTimeRange{ Start = new DateTime(2021, 4, 30), End = new DateTime(2021, 5, 1) },
                new DateTimeRange{ Start = new DateTime(2021, 9, 30), End = new DateTime(2021, 10, 1) },
                new DateTimeRange{ Start = new DateTime(2022, 2, 28), End = new DateTime(2022, 3, 1) },
                new DateTimeRange{ Start = new DateTime(2022, 7, 31), End = new DateTime(2022, 8, 1) },
                new DateTimeRange{ Start = new DateTime(2022, 12, 31), End = new DateTime(2023, 1, 1) },
                new DateTimeRange{ Start = new DateTime(2023, 5, 31), End = new DateTime(2023, 6, 1) },
                new DateTimeRange{ Start = new DateTime(2023, 10, 31), End = new DateTime(2023, 11, 1) },
                new DateTimeRange{ Start = new DateTime(2024, 3, 31), End = new DateTime(2024, 4, 1) },
                new DateTimeRange{ Start = new DateTime(2024, 8, 31), End = new DateTime(2024, 9, 1) },
                new DateTimeRange{ Start = new DateTime(2025, 1, 31), End = new DateTime(2025, 2, 1) },
                new DateTimeRange{ Start = new DateTime(2025, 6, 30), End = new DateTime(2025, 7, 1) },
            };

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestWeekly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 1, 2, 3), // Tuesday
                duration:       new MetaCalendarPeriod{ Days = 6, Hours = 10, Minutes = 20, Seconds = 30 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 7 },
                numRepeats:     130);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 130)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 1, 2, 3).AddDays(7*repeat), End = new DateTime(2020, 2, 17, 11, 22, 33).AddDays(7*repeat) })
                .ToList();

            foreach (DateTimeRange range in referenceRanges)
            {
                Assert.AreEqual(DayOfWeek.Tuesday, range.Start.DayOfWeek);
                Assert.AreEqual(DayOfWeek.Monday, range.End.DayOfWeek);
            }

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestWeeklyFullWeek()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 1, 2, 3), // Tuesday
                duration:       new MetaCalendarPeriod{ Days = 7 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 7 },
                numRepeats:     130);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 130)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 1, 2, 3).AddDays(7*repeat), End = new DateTime(2020, 2, 18, 1, 2, 3).AddDays(7*repeat) })
                .ToList();

            foreach (DateTimeRange range in referenceRanges)
            {
                Assert.AreEqual(DayOfWeek.Tuesday, range.Start.DayOfWeek);
                Assert.AreEqual(DayOfWeek.Tuesday, range.End.DayOfWeek);
            }

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestDaily()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 10, Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddDays(repeat), End = new DateTime(2020, 2, 11, 17, 19, 21).AddDays(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestDailyWrapping()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 20, Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddDays(repeat), End = new DateTime(2020, 2, 12, 3, 19, 21).AddDays(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestDailyFullDayNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 0, 0, 0),
                duration:       new MetaCalendarPeriod{ Days = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11).AddDays(repeat), End = new DateTime(2020, 2, 12).AddDays(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestDailyFullDay()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Days = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddDays(repeat), End = new DateTime(2020, 2, 12, 7, 8, 9).AddDays(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestDailyInfiniteRepeats()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 10, Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Days = 1 },
                numRepeats:     -1);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 2000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddDays(repeat), End = new DateTime(2020, 2, 11, 17, 19, 21).AddDays(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges, maxCount: 2000);
        }

        [Test]
        public static void TestHourly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddHours(repeat), End = new DateTime(2020, 2, 11, 7, 19, 21).AddHours(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiHourly()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 4, Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddHours(5*repeat), End = new DateTime(2020, 2, 11, 11, 19, 21).AddHours(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestHourlyWrapping()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 55, 9),
                duration:       new MetaCalendarPeriod{ Minutes = 11, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 55, 9).AddHours(repeat), End = new DateTime(2020, 2, 11, 8, 6, 21).AddHours(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestHourlyFullHourNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 0, 0),
                duration:       new MetaCalendarPeriod{ Hours = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 0, 0).AddHours(repeat), End = new DateTime(2020, 2, 11, 8, 0, 0).AddHours(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiHourlyFullHoursNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 0, 0),
                duration:       new MetaCalendarPeriod{ Hours = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 0, 0).AddHours(5*repeat), End = new DateTime(2020, 2, 11, 12, 0, 0).AddHours(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestHourlyFullHour()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddHours(repeat), End = new DateTime(2020, 2, 11, 8, 8, 9).AddHours(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestMultiHourlyFullHours()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Hours = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Hours = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddHours(5*repeat), End = new DateTime(2020, 2, 11, 12, 8, 9).AddHours(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMinute()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddMinutes(repeat), End = new DateTime(2020, 2, 11, 7, 8, 21).AddMinutes(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMultiMinutes()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Minutes = 4, Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddMinutes(5*repeat), End = new DateTime(2020, 2, 11, 7, 12, 21).AddMinutes(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMinuteWrapping()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 56),
                duration:       new MetaCalendarPeriod{ Seconds = 12 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 56).AddMinutes(repeat), End = new DateTime(2020, 2, 11, 7, 9, 8).AddMinutes(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMinuteFullMinuteNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 0),
                duration:       new MetaCalendarPeriod{ Minutes = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 0).AddMinutes(repeat), End = new DateTime(2020, 2, 11, 7, 9, 0).AddMinutes(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMultiMinutesFullMinutesNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 0),
                duration:       new MetaCalendarPeriod{ Minutes = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 0).AddMinutes(5*repeat), End = new DateTime(2020, 2, 11, 7, 13, 0).AddMinutes(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMinuteFullMinute()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Minutes = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddMinutes(repeat), End = new DateTime(2020, 2, 11, 7, 9, 9).AddMinutes(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMultiMinuteFullMinutes()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Minutes = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Minutes = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddMinutes(5*repeat), End = new DateTime(2020, 2, 11, 7, 13, 9).AddMinutes(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEverySecondFullSecondNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Seconds = 1 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Seconds = 1 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddSeconds(repeat), End = new DateTime(2020, 2, 11, 7, 8, 10).AddSeconds(repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void TestEveryMultiSecondFullSecondsNoOffset()
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:       MetaScheduleTimeMode.Utc,
                start:          new MetaCalendarDateTime(2020, 2, 11, 7, 8, 9),
                duration:       new MetaCalendarPeriod{ Seconds = 5 },
                endingSoon:     new MetaCalendarPeriod(),
                preview:        new MetaCalendarPeriod(),
                review:         new MetaCalendarPeriod(),
                recurrence:     new MetaCalendarPeriod{ Seconds = 5 },
                numRepeats:     1000);

            List<DateTimeRange> referenceRanges =
                Enumerable.Range(0, 1000)
                .Select(repeat => new DateTimeRange{ Start = new DateTime(2020, 2, 11, 7, 8, 9).AddSeconds(5*repeat), End = new DateTime(2020, 2, 11, 7, 8, 14).AddSeconds(5*repeat) })
                .ToList();

            TestWithScheduleAndReferences(schedule, referenceRanges);
        }

        [Test]
        public static void RandomTest()
        {
            RandomPCG rnd = RandomPCG.CreateFromSeed(seed: 1);

            for (int testNdx = 0; testNdx < 200; testNdx++)
            {
                retryRandomGeneration:
                MetaCalendarDateTime    scheduleStart   = CreateRandomDateTime(rnd);
                MetaCalendarPeriod      recurrence      = CreateRandomRecurrence(rnd);
                MetaCalendarPeriod      duration        = CreateRandomDuration(rnd, recurrence);
                long                    maxRepeats      = CalculateRoughMaxRepeats(scheduleStart, recurrence);

                int? numRepeats;
                switch (rnd.NextInt(5))
                {
                    case 0:     numRepeats = null;                                          break;
                    case 1:     numRepeats = rnd.NextInt((int)Math.Min(maxRepeats+1, 2));   break;
                    default:    numRepeats = rnd.NextInt((int)Math.Min(maxRepeats+1, 500)); break;
                }

                MetaScheduleBase schedule = new MetaRecurringCalendarSchedule(
                    timeMode:       MetaScheduleTimeMode.Utc,
                    start:          scheduleStart,
                    duration:       duration,
                    endingSoon:     new MetaCalendarPeriod(),
                    preview:        new MetaCalendarPeriod(),
                    review:         new MetaCalendarPeriod(),
                    recurrence:     recurrence,
                    numRepeats:     numRepeats);

                int numReferences = numRepeats ?? (int)Math.Min(maxRepeats, 500);

                List<DateTimeRange> referenceRanges = new List<DateTimeRange>();
                for (int i = 0; i < numReferences; i++)
                {
                    // \note Naive multiplied Add* calculation works because recurrence only has one nonzero component.
                    DateTime rangeStart =
                        scheduleStart.ToDateTime()
                        .AddYears(i * recurrence.Years)
                        .AddMonths(i * recurrence.Months)
                        .AddDays((long)i * recurrence.Days)
                        .AddHours((long)i * recurrence.Hours)
                        .AddMinutes((long)i * recurrence.Minutes)
                        .AddSeconds((long)i * recurrence.Seconds);

                    referenceRanges.Add(new DateTimeRange
                    {
                        Start = rangeStart,
                        End = duration.AddToDateTime(rangeStart),
                    });

                    // If ranges overlap, re-generate test case.
                    // It's easier to do it this way instead of ensuring we never
                    // generate non-overlapping schedules in the first place.
                    if (i > 0 && referenceRanges[i].Start < referenceRanges[i-1].End)
                        goto retryRandomGeneration;
                }

                TestWithScheduleAndReferences(schedule, referenceRanges, maxCount: numRepeats.HasValue ? 1_000_000 : numReferences);
            }
        }

        static MetaCalendarDateTime CreateRandomDateTime(RandomPCG rnd)
        {
            int year    = rnd.NextInt(5) == 0
                          ? 1 + rnd.NextInt(9999)
                          : 2000 + rnd.NextInt(50);

            int month   = 1 + rnd.NextInt(12);
            int day     = 1 + rnd.NextInt(DateTime.DaysInMonth(year, month));
            int hour    = rnd.NextInt(24);
            int minute  = rnd.NextInt(60);
            int second  = rnd.NextInt(60);

            return new MetaCalendarDateTime(year, month, day, hour, minute, second);
        }

        enum PeriodUnit
        {
            Years,
            Months,
            Days,
            Hours,
            Minutes,
            Seconds,
        }

        static MetaCalendarPeriod CreateRandomRecurrence(RandomPCG rnd)
        {
            PeriodUnit  unit        = rnd.Choice(EnumUtil.GetValues<PeriodUnit>());
            bool        allowLarge  = rnd.NextInt(3) == 0;

            switch (unit)
            {
                case PeriodUnit.Years:      return new MetaCalendarPeriod{ Years    = 1 + rnd.NextInt(10) };
                case PeriodUnit.Months:     return new MetaCalendarPeriod{ Months   = 1 + rnd.NextInt(allowLarge ? 10*12            : 12) };
                case PeriodUnit.Days:       return new MetaCalendarPeriod{ Days     = 1 + rnd.NextInt(allowLarge ? 10*366           : 31) };
                case PeriodUnit.Hours:      return new MetaCalendarPeriod{ Hours    = 1 + rnd.NextInt(allowLarge ? 10*366*24        : 24) };
                case PeriodUnit.Minutes:    return new MetaCalendarPeriod{ Minutes  = 1 + rnd.NextInt(allowLarge ? 10*366*24*60     : 60) };
                case PeriodUnit.Seconds:    return new MetaCalendarPeriod{ Seconds  = 1 + rnd.NextInt(allowLarge ? 10*366*24*60*60  : 60) };
                default:
                    throw new Exception();
            }
        }

        static MetaCalendarPeriod CreateRandomDuration(RandomPCG rnd, MetaCalendarPeriod recurrence)
        {
            if (rnd.NextInt(5) == 0)
                return recurrence;

            MetaCalendarPeriod duration = new MetaCalendarPeriod();

            // Duration must not be greater than recurrence. These bools keep track of things to ensure that.
            bool fitsAny = false;
            bool fitsFull;

            fitsAny |= recurrence.Years != 0;
            if (fitsAny) duration.Years = rnd.NextInt((int)recurrence.Years);
            fitsFull = fitsAny;

            fitsAny |= recurrence.Months != 0;
            if (fitsAny) duration.Months = rnd.NextInt(fitsFull ? 12 : (int)recurrence.Months);
            fitsFull = fitsAny;

            fitsAny |= recurrence.Days != 0;
            if (fitsAny) duration.Days = rnd.NextInt(fitsFull ? 28 : (int)recurrence.Days);
            fitsFull = fitsAny;

            fitsAny |= recurrence.Hours != 0;
            if (fitsAny) duration.Hours = rnd.NextInt(fitsFull ? 24 : (int)recurrence.Hours);
            fitsFull = fitsAny;

            fitsAny |= recurrence.Minutes != 0;
            if (fitsAny) duration.Minutes = rnd.NextInt(fitsFull ? 60 : (int)recurrence.Minutes);
            fitsFull = fitsAny;

            fitsAny |= recurrence.Seconds != 0;
            if (fitsAny) duration.Seconds = rnd.NextInt(fitsFull ? 60 : (int)recurrence.Seconds);
            fitsFull = fitsAny;

            if (duration.IsNone)
                duration.Seconds = 1;

            return duration;
        }

        static long CalculateRoughMaxRepeats(MetaCalendarDateTime start, MetaCalendarPeriod recurrence)
        {
            MetaDuration durationUntilMaxDateTime   = MetaDuration.FromTimeSpan(DateTime.MaxValue - start.ToDateTime());
            MetaDuration roughRecurrenceDuration    = PeriodToRoughUpperDurationEstimate(recurrence);

            return durationUntilMaxDateTime.Milliseconds / roughRecurrenceDuration.Milliseconds;
        }

        /// <summary>
        /// Calculate a rough upper estimate of the duration of the given period.
        /// The returned duration is no lower than the given period, ignoring leap seconds and the like.
        /// </summary>
        static MetaDuration PeriodToRoughUpperDurationEstimate(MetaCalendarPeriod period)
        {
            return MetaDuration.FromSeconds(period.Seconds)
                + MetaDuration.FromMinutes(period.Minutes)
                + MetaDuration.FromHours(period.Hours)
                + MetaDuration.FromDays(period.Days)
                + MetaDuration.FromDays(31 * period.Months)
                + MetaDuration.FromDays(366 * period.Years);
        }

        static void TestWithScheduleAndReferences(MetaScheduleBase schedule, List<DateTimeRange> referenceRanges, int playerLocalUtcOffsetHours = 0, int maxCount = 1_000_000)
        {
            int             scheduleLocalUtcOffsetHours = schedule.TimeMode == MetaScheduleTimeMode.Local ? playerLocalUtcOffsetHours : 0;
            MetaDuration    scheduleUtcOffset           = MetaDuration.FromHours(scheduleLocalUtcOffsetHours);

            List<MetaTimeRange> resultRanges = GetScheduleEnabledTimeRanges(schedule, scheduleUtcOffset, maxCount);
            CheckAgainstReferenceRanges(referenceRanges, resultRanges);
            TestPointsForRanges(schedule, resultRanges, scheduleUtcOffset);
            TestSchedulePreviousOccasions(schedule, resultRanges, scheduleUtcOffset);
            TestSpecialCasePreviousAndNextOccasions(schedule, resultRanges, scheduleUtcOffset);
        }

        static void TestSpecialCasePreviousAndNextOccasions(MetaScheduleBase schedule, List<MetaTimeRange> resultRanges, MetaDuration scheduleUtcOffset)
        {
            if (resultRanges.Count == 0)
            {
                MetaScheduleOccasionsQueryResult occ0 = schedule.QueryOccasions(new PlayerLocalTime(MetaTime.FromDateTime(DateTime.MinValue), scheduleUtcOffset));
                MetaScheduleOccasionsQueryResult occ1 = schedule.QueryOccasions(new PlayerLocalTime(MetaTime.FromDateTime(new DateTime(2021, 5, 7, 17, 18, 29)), scheduleUtcOffset));
                MetaScheduleOccasionsQueryResult occ2 = schedule.QueryOccasions(new PlayerLocalTime(MetaTime.FromDateTime(new DateTime(9995, 1, 1)), scheduleUtcOffset));

                Assert.IsNull(occ0.PreviousEnabledOccasion);
                Assert.IsNull(occ0.CurrentOrNextEnabledOccasion);

                Assert.IsNull(occ1.PreviousEnabledOccasion);
                Assert.IsNull(occ1.CurrentOrNextEnabledOccasion);

                Assert.IsNull(occ2.PreviousEnabledOccasion);
                Assert.IsNull(occ2.CurrentOrNextEnabledOccasion);
            }
            else
            {
                MetaScheduleOccasionsQueryResult occ0 = schedule.QueryOccasions(new PlayerLocalTime(MetaTime.FromDateTime(DateTime.MinValue), scheduleUtcOffset));
                MetaScheduleOccasionsQueryResult occ1 = schedule.QueryOccasions(new PlayerLocalTime(MetaTime.FromDateTime(new DateTime(9995, 1, 1)), scheduleUtcOffset));

                Assert.IsNull(occ0.PreviousEnabledOccasion);
                Assert.AreEqual(resultRanges.First(), occ0.CurrentOrNextEnabledOccasion.Value.EnabledRange);

                bool hasInfiniteRepeats = schedule is MetaRecurringCalendarSchedule recurring && !recurring.NumRepeats.HasValue;
                if (!hasInfiniteRepeats)
                {
                    Assert.AreEqual(resultRanges.Last(), occ1.PreviousEnabledOccasion.Value.EnabledRange);
                    Assert.IsNull(occ1.CurrentOrNextEnabledOccasion);
                }
            }
        }

        struct DateTimeRange
        {
            public DateTime Start;
            public DateTime End;
        }

        static List<MetaTimeRange> GetScheduleEnabledTimeRanges(MetaScheduleBase schedule, MetaDuration scheduleUtcOffset, int maxCount)
        {
            List<MetaTimeRange> resultRanges    = new List<MetaTimeRange>();
            MetaTime            currentTime     = MetaTime.FromDateTime(DateTime.MinValue);

            while (true)
            {
                if (resultRanges.Count == maxCount)
                    break;

                MetaTimeRange? timeRangeMaybe = schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(currentTime, scheduleUtcOffset))?.EnabledRange;

                if (!timeRangeMaybe.HasValue)
                    break;

                MetaTimeRange timeRange = timeRangeMaybe.Value;

                currentTime = timeRange.End;

                resultRanges.Add(timeRange);
            }

            return resultRanges;
        }

        static void TestSchedulePreviousOccasions(MetaScheduleBase schedule, List<MetaTimeRange> resultRanges, MetaDuration scheduleUtcOffset)
        {
            for (int i = resultRanges.Count-1; i >= 0; i--)
            {
                Assert.AreEqual(resultRanges[i], schedule.QueryOccasions(new PlayerLocalTime(resultRanges[i].End, scheduleUtcOffset)).PreviousEnabledOccasion?.EnabledRange);

                MetaTimeRange? previousRange = i > 0 ? resultRanges[i-1] : null;

                Assert.AreEqual(previousRange, schedule.QueryOccasions(new PlayerLocalTime(resultRanges[i].End - MetaDuration.FromMilliseconds(1), scheduleUtcOffset)).PreviousEnabledOccasion?.EnabledRange);
                Assert.AreEqual(previousRange, schedule.QueryOccasions(new PlayerLocalTime(resultRanges[i].Start, scheduleUtcOffset)).PreviousEnabledOccasion?.EnabledRange);
            }
        }

        static void TestPointsForRanges(MetaScheduleBase schedule, List<MetaTimeRange> ranges, MetaDuration scheduleUtcOffset)
        {
            foreach (MetaTimeRange range in ranges)
                TestPointsForRange(schedule, range, scheduleUtcOffset);
        }

        static void TestPointsForRange(MetaScheduleBase schedule, MetaTimeRange range, MetaDuration scheduleUtcOffset)
        {
            Assert.AreEqual(range, schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(range.Start, scheduleUtcOffset))?.EnabledRange);
            Assert.AreEqual(range, schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(range.End - MetaDuration.FromMilliseconds(1), scheduleUtcOffset))?.EnabledRange);
            Assert.AreNotEqual(range, schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(range.End, scheduleUtcOffset))?.EnabledRange);
            Assert.AreNotEqual(range, schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(range.End + MetaDuration.FromMinutes(1), scheduleUtcOffset))?.EnabledRange);

            const int NumInterpolatedPoints = 5;
            for (int i = 0; i < NumInterpolatedPoints; i++)
            {
                double      f       = (i + 0.5) / NumInterpolatedPoints;
                MetaTime    time    = range.Start + MetaDuration.FromMilliseconds((long)(f * (range.End - range.Start).Milliseconds));

                Assert.AreEqual(range, schedule.TryGetCurrentOrNextEnabledOccasion(new PlayerLocalTime(time, scheduleUtcOffset))?.EnabledRange);
            }
        }

        static void CheckAgainstReferenceRanges(List<DateTimeRange> referenceRanges, List<MetaTimeRange> resultRanges)
        {
            Assert.AreEqual(referenceRanges.Count, resultRanges.Count);

            for (int i = 0; i < referenceRanges.Count; i++)
            {
                Assert.AreEqual(MetaTime.FromDateTime(referenceRanges[i].Start), resultRanges[i].Start, Invariant($"Start, at range index {i}"));
                Assert.AreEqual(MetaTime.FromDateTime(referenceRanges[i].End), resultRanges[i].End, Invariant($"End, at range index {i}"));
            }
        }
    }
}
