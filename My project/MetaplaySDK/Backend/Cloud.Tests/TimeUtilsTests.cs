// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using Metaplay.Core;
using NUnit.Framework;

namespace Cloud.Tests
{
    [TestFixture]
    public class TimeUtilsTests
    {
        [Test]
        public void IsWithinDailyWindowHandlesBadInput()
        {
            // Don't allow windowStart to be -ve
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: -11, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 1, minutes: 0, seconds: 0)
                ));

            // Don't allow windowStart to be outside a single day
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 24, minutes: 0, seconds: 1),
                windowLength: new TimeSpan(hours: 1, minutes: 0, seconds: 0)
                ));


            // Don't allow length to be 0 or -ve
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 1),
                windowLength: new TimeSpan(hours: 0, minutes: 0, seconds: 0)
                ));
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 1),
                windowLength: new TimeSpan(hours: -1, minutes: 0, seconds: 0)
                ));

            // Don't allow length to be greater than a day
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 1),
                windowLength: new TimeSpan(hours: 24, minutes: 0, seconds: 1)
                ));
            Assert.Throws<ArgumentException>(() => TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 1),
                windowLength: -(new TimeSpan(hours: 24, minutes: 0, seconds: 1))
                ));
        }

        [Test]
        public void IsWithinDailyWindow()
        {
            // Time is before window
            Assert.IsFalse(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 1, minutes: 0, seconds: 0)
                ));

            // Time is inside window
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 11, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 1, minutes: 0, seconds: 0)
                ));

            // Time is after window
            Assert.IsFalse(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 12, 00, 01)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 1, minutes: 0, seconds: 0)
                ));
        }

        [Test]
        public void IsWithinDailyWindowWhenWindowStraddlesMidnight()
        {
            // Time is before window
            Assert.IsFalse(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 22, 00, 00)),
                windowStart: new TimeSpan(hours: 23, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 2, minutes: 0, seconds: 0)
                ));

            // Time is inside window (before midnight)
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 23, 00, 00)),
                windowStart: new TimeSpan(hours: 23, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 2, minutes: 0, seconds: 0)
                ));

            // Time is inside window (after midnight)
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 9, 0, 00, 00)),
                windowStart: new TimeSpan(hours: 23, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 2, minutes: 0, seconds: 0)
                ));

            // Time is after window
            Assert.IsFalse(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 9, 2, 00, 00)),
                windowStart: new TimeSpan(hours: 23, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 2, minutes: 0, seconds: 0)
                ));
        }

        [Test]
        public void IsWithinDailyWindowWhenIs24Hours()
        {
            // Time is before window start
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 9, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 24, minutes: 0, seconds: 0)
                ));

            // Time is at window start
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 10, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 24, minutes: 0, seconds: 0)
                ));

            // Time is after window start
            Assert.IsTrue(TimeUtils.IsWithinDailyWindow(
                time: MetaTime.FromDateTime(new DateTime(2021, 6, 8, 10, 00, 00)),
                windowStart: new TimeSpan(hours: 11, minutes: 0, seconds: 0),
                windowLength: new TimeSpan(hours: 24, minutes: 0, seconds: 0)
                ));
        }
    }
}
