// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [TestFixture]
    public class DivisionIndexTests
    {
        [Test]
        public void Conversions()
        {
            int[] LeaguesToTest = new[]
            {
                0,
                1,
                2,
                3,
                (int)DivisionIndex.LeagueMax - 1,
            };

            int[] SeasonsToTest = new[]
            {
                0,
                1,
                100,
                (int)DivisionIndex.SeasonMax / 2,
                (int)DivisionIndex.SeasonMax - 2,
                (int)DivisionIndex.SeasonMax - 1,
            };

            int[] RanksToTest = new[]
            {
                0,
                1,
                2,
                10,
                16,
                24,
                64,
                (int)DivisionIndex.RankMax / 2,
                (int)DivisionIndex.RankMax - 2,
                (int)DivisionIndex.RankMax - 1,
            };

            int[] DivisionsToTest = new[]
            {
                0,
                1,
                2,
                (int)DivisionIndex.DivisionMax / 2,
                (int)DivisionIndex.DivisionMax - 2,
                (int)DivisionIndex.DivisionMax - 1,
            };

            foreach (int league in LeaguesToTest)
            {
                foreach (int season in SeasonsToTest)
                {
                    foreach (int rank in RanksToTest)
                    {
                        foreach (int div in DivisionsToTest)
                        {
                            DivisionIndex originalDivisionIndex = new DivisionIndex(league, season, rank, div);
                            Assert.AreEqual(originalDivisionIndex.League, league);
                            Assert.AreEqual(originalDivisionIndex.Season, season);
                            Assert.AreEqual(originalDivisionIndex.Rank, rank);
                            Assert.AreEqual(originalDivisionIndex.Division, div);

                            EntityId entityIdDivision = originalDivisionIndex.ToEntityId();

                            DivisionIndex indexFromEntityId = DivisionIndex.FromEntityId(entityIdDivision);

                            Assert.AreEqual(indexFromEntityId.League, league, "League was different from expected");
                            Assert.AreEqual(indexFromEntityId.Season, season, "Season was different from expected");
                            Assert.AreEqual(indexFromEntityId.Rank, rank, "Rank was different from expected");
                            Assert.AreEqual(indexFromEntityId.Division, div, "Division was different from expected");
                        }
                    }
                }
            }
        }
    }
}
