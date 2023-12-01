// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Server.ScheduledPlayerDeletion;
using NUnit.Framework;
using System;

namespace Metaplay.Server.Tests
{
    [TestFixture]
    class ScheduledPlayerDeletionSchedulingTests
    {
        [TestCase(0, 0)]
        [TestCase(21, 30)]
        [TestCase(21, 50)]
        public void Test(int offsetHours, int offsetMinutes)
        {
            TimeSpan offset = new TimeSpan(offsetHours, offsetMinutes, 0);

            // Current time is exactly at window start
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 2, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is exactly at window start, but lastStartTime is too close, and so bumps to next day
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 13, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is before window
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 1, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is before window, but lastStartTime is too close, and so bumps to next day
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 13, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 0, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is before window, but lastStartTime is too close (just barely), and so bumps to next day
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 13, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 21, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is before window, but lastStartTime is close enough to bump further into window but not past it
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 59, 59) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 20, 59, 59) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is well inside window
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 30, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 30, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 2, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is well inside window, but lastStartTime is too close, and so bumps to next day
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 13, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 30, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is well inside window, but lastStartTime is close enough to bump further into window but not past it
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 45, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 2, 30, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 11, 20, 45, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));

            // Current time is past current day's window
            Assert.AreEqual(
                MetaTime.FromDateTime(new DateTime(2022, 11, 13, 2, 0, 0) + offset),
                ScheduledPlayerDeletionManager.GetNextPeriodicStartTime(
                    currentTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 3, 0, 0) + offset),
                    lastStartTime: MetaTime.FromDateTime(new DateTime(2022, 11, 12, 1, 0, 0) + offset),
                    deletionSweepTimeOfDay: TimeSpan.FromHours(2) + offset));
        }
    }
}
