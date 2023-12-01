// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Server.League;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Server.Tests
{
    [TestFixture]
    class LeagueTests
    {
        public class LeagueTestOptions : LeagueManagerOptions
        {
            public LeagueTestOptions()
            {
                DivisionDesiredParticipantCount = 10;
                DivisionMaxParticipantCount     = 10;
            }
        }

        LeagueTestOptions Options { get; set; } = new LeagueTestOptions();

        [Test]
        public void TestDivisionParticipantCount()
        {
            LeagueDivisionParticipantCountState counts = new LeagueDivisionParticipantCountState(Options, 2);
            Assert.AreEqual(2, counts.Ranks.Count);

            // No participants or divisions
            Assert.AreEqual(0, counts.Ranks[0].DivisionParticipantCounts.Count);
            Assert.AreEqual(0, counts.Ranks[1].DivisionParticipantCounts.Count);
            Assert.False(counts.TryGetNonFullDivisionForRank(0, out _));
            Assert.False(counts.TryGetNonFullDivisionForRank(1, out _));

            // Add participants
            counts.AddParticipant(0, 0);
            Assert.AreEqual(1, counts.Ranks[0].DivisionParticipantCounts.Count);
            Assert.AreEqual(1, counts.Ranks[0].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(1, counts.Ranks[0].NonFullDivisions.Count);
            Assert.AreEqual(0, counts.Ranks[0].NonFullDivisions.First());

            counts.AddParticipant(0, 0);
            Assert.AreEqual(1, counts.Ranks[0].DivisionParticipantCounts.Count);
            Assert.AreEqual(2, counts.Ranks[0].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(1, counts.Ranks[0].NonFullDivisions.Count);

            Assert.True(counts.TryGetNonFullDivisionForRank(0, out _));

            // Add participants to higher division
            counts.AddParticipant(1, 2);
            Assert.AreEqual(3, counts.Ranks[1].DivisionParticipantCounts.Count);
            Assert.AreEqual(0, counts.Ranks[1].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(0, counts.Ranks[1].DivisionParticipantCounts[1].Count);
            Assert.AreEqual(1, counts.Ranks[1].DivisionParticipantCounts[2].Count);
            Assert.AreEqual(3, counts.Ranks[1].NonFullDivisions.Count);

            Assert.True(counts.TryGetNonFullDivisionForRank(1, out _));

            // Fill a division
            for (int i = 0; i < Options.DivisionMaxParticipantCount; i++)
            {
                counts.AddParticipant(1, 0);
                Assert.AreEqual(i + 1, counts.Ranks[1].DivisionParticipantCounts[0].Count);
            }

            Assert.AreEqual(2, counts.Ranks[1].NonFullDivisions.Count);

            // Remove participant
            counts.RemoveParticipant(1, 0);
            Assert.AreEqual(Options.DivisionMaxParticipantCount - 1, counts.Ranks[1].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(3, counts.Ranks[1].NonFullDivisions.Count);

            // Set participant counts
            counts.SetParticipantCount(0, 0, 10);
            Assert.False(counts.TryGetNonFullDivisionForRank(0, out _));
            Assert.AreEqual(10, counts.Ranks[0].DivisionParticipantCounts[0].Count);

            // Set participant count under max
            counts.SetParticipantCount(0, 0, 5);
            Assert.True(counts.TryGetNonFullDivisionForRank(0, out _));

            // Create new division in between
            counts.SetParticipantCount(0, 2, 10);
            Assert.AreEqual(3, counts.Ranks[0].DivisionParticipantCounts.Count);
            Assert.AreEqual(5, counts.Ranks[0].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(0, counts.Ranks[0].DivisionParticipantCounts[1].Count);
            Assert.AreEqual(10, counts.Ranks[0].DivisionParticipantCounts[2].Count);

            // Fill all divisions
            counts.SetParticipantCount(0, 0, 12);
            counts.SetParticipantCount(0, 1, 15);
            Assert.False(counts.TryGetNonFullDivisionForRank(0, out _));

            // Remove but not under max
            counts.SetParticipantCount(0, 1, 10);
            Assert.AreEqual(10, counts.Ranks[0].DivisionParticipantCounts[1].Count);
            Assert.False(counts.TryGetNonFullDivisionForRank(0, out _));

            // Create new in between, test not full.
            counts.SetParticipantCount(0, 4, 10);
            Assert.True(counts.TryGetNonFullDivisionForRank(0, out _));

            // Set in rank 1
            counts.SetParticipantCount(1, 1, 77);
            Assert.AreEqual(77, counts.Ranks[1].DivisionParticipantCounts[1].Count);
        }

        [Test]
        public void TestInitializeFromDivisionAssignments()
        {
            int                 numRanks    = 3;
            List<DivisionIndex> assignments = new List<DivisionIndex>();

            for (int i = 0; i < Options.DivisionMaxParticipantCount; i++)
                assignments.Add(new DivisionIndex(0, 0, 0, 0));

            for (int i = 0; i < 3; i++)
            {
                // Rank 1 has 3 divisions, 2 participants each
                assignments.Add(new DivisionIndex(0, 0, 1, i));
                assignments.Add(new DivisionIndex(0, 0, 1, i));
            }

            // Rank 2 has 4 divisions, 1 participants each except for two middle ones
            assignments.Add(new DivisionIndex(0, 0, 2, 0));
            assignments.Add(new DivisionIndex(0, 0, 2, 3));


            LeagueDivisionParticipantCountState counts = new LeagueDivisionParticipantCountState(Options, numRanks, assignments);

            Assert.AreEqual(numRanks, counts.Ranks.Count);

            // Check rank 0
            Assert.AreEqual(1, counts.Ranks[0].DivisionParticipantCounts.Count);
            Assert.AreEqual(Options.DivisionMaxParticipantCount, counts.Ranks[0].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(0, counts.Ranks[0].NonFullDivisions.Count);

            // Check rank 1
            Assert.AreEqual(3, counts.Ranks[1].DivisionParticipantCounts.Count);
            Assert.AreEqual(2, counts.Ranks[1].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(2, counts.Ranks[1].DivisionParticipantCounts[1].Count);
            Assert.AreEqual(2, counts.Ranks[1].DivisionParticipantCounts[2].Count);
            Assert.AreEqual(3, counts.Ranks[1].NonFullDivisions.Count);

            // Check rank 2
            Assert.AreEqual(4, counts.Ranks[2].DivisionParticipantCounts.Count);
            Assert.AreEqual(1, counts.Ranks[2].DivisionParticipantCounts[0].Count);
            Assert.AreEqual(0, counts.Ranks[2].DivisionParticipantCounts[1].Count);
            Assert.AreEqual(0, counts.Ranks[2].DivisionParticipantCounts[2].Count);
            Assert.AreEqual(1, counts.Ranks[2].DivisionParticipantCounts[3].Count);
            Assert.AreEqual(4, counts.Ranks[2].NonFullDivisions.Count);
        }

        [Test]
        public void TestInitializeFromPersisted()
        {
            LeagueDivisionParticipantCountState counts = new LeagueDivisionParticipantCountState(Options, 3);
            counts.AddParticipant(0, 99);
            counts.AddParticipant(1, 9);
            counts.AddParticipant(2, 0);

            for (int i = 0; i < 50 * Options.DivisionMaxParticipantCount; i++)
            {
                Assert.True(counts.TryGetNonFullDivisionForRank(0, out int div));
                counts.AddParticipant(0, div);
            }

            for (int i = 1; i < 10 * Options.DivisionMaxParticipantCount; i++)
            {
                Assert.True(counts.TryGetNonFullDivisionForRank(1, out int div));
                counts.AddParticipant(1, div);
            }

            Assert.False(counts.TryGetNonFullDivisionForRank(1, out _));
            Assert.AreEqual(0, counts.Ranks[1].NonFullDivisions.Count);

            List<PersistedDivisionCounts> persisted = counts.ToPersisted();

            Assert.AreEqual(3, persisted.Count);

            Assert.True(BlobCompress.IsCompressed(persisted[0].CompressedDivisionCounts));
            Assert.True(BlobCompress.IsCompressed(persisted[1].CompressedDivisionCounts));
            Assert.True(BlobCompress.IsCompressed(persisted[2].CompressedDivisionCounts));

            LeagueDivisionParticipantCountState counts2 = new LeagueDivisionParticipantCountState(Options, persisted);

            Assert.AreEqual(3, counts2.Ranks.Count);

            Assert.That(counts2.Ranks[0].DivisionParticipantCounts, Is.EquivalentTo(counts.Ranks[0].DivisionParticipantCounts));
            Assert.That(counts2.Ranks[1].DivisionParticipantCounts, Is.EquivalentTo(counts.Ranks[1].DivisionParticipantCounts));
            Assert.That(counts2.Ranks[2].DivisionParticipantCounts, Is.EquivalentTo(counts.Ranks[2].DivisionParticipantCounts));

            Assert.That(counts2.Ranks[0].NonFullDivisions, Is.EquivalentTo(counts.Ranks[0].NonFullDivisions));
            Assert.That(counts2.Ranks[1].NonFullDivisions, Is.EquivalentTo(counts.Ranks[1].NonFullDivisions));
            Assert.That(counts2.Ranks[2].NonFullDivisions, Is.EquivalentTo(counts.Ranks[2].NonFullDivisions));
        }
    }
}
