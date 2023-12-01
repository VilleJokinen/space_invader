// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Identifies a single Division within a League. Consists of the League id, Season number (time),
    /// Rank number (league level), and the division number within the league.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializable]
    public struct DivisionIndex : IEquatable<DivisionIndex>
    {
        // Bottom 26 bits for Division
        // 8 bits for rank
        // 4 bits for league
        // EntityId.KindShift - 38 bits for season

        const int DivisionBits = 26;
        const int RankBits     = 8;
        const int LeagueBits   = 4;
        const int SeasonBits   = EntityId.KindShift - DivisionBits - RankBits - LeagueBits;

        public const ulong DivisionMax = 1UL << DivisionBits;
        public const ulong RankMax     = 1UL << RankBits;
        public const ulong LeagueMax   = 1UL << LeagueBits;
        public const ulong SeasonMax   = 1UL << SeasonBits;


        const ulong DivisionMask = DivisionMax - 1;
        const ulong RankMask     = RankMax - 1;
        const int   RankShift    = DivisionBits;
        const ulong LeagueMask   = LeagueMax - 1;
        const int   LeagueShift  = DivisionBits + RankBits;
        const ulong SeasonMask   = SeasonMax - 1;
        const int   SeasonShift  = DivisionBits + RankBits + LeagueBits;

        /// <summary>
        /// The league number of this division.
        /// </summary>
        [MetaMember(1)] public int League;

        /// <summary>
        /// The Season of the division. This is determines the season start and
        /// end dates, based on the season calendar configuration. For example this
        /// could be 1 on a certain week, and then 2 on the following week.
        /// </summary>
        [MetaMember(2)] public int Season;

        /// <summary>
        /// The League level of the division. This is the rank of the division, and higher
        /// levels are harder. For example, this could be 0 for the bronze league and 3 for
        /// gold league. Of 0-9 for various bronze league level brackets, and 20-29 for gold.
        /// </summary>
        [MetaMember(3)] public int Rank;

        /// <summary>
        /// The division number in the league.
        /// </summary>
        [MetaMember(4)] public int Division;

        public DivisionIndex(int league, int season, int rank, int division)
        {
            if ((ulong)division >= DivisionMax || division < 0)
                throw new ArgumentOutOfRangeException($"Division out of range: {division}");
            if ((ulong)rank >= RankMax || rank < 0)
                throw new ArgumentOutOfRangeException($"Rank out of range: {rank}");
            if ((ulong)season >= SeasonMax || season < 0)
                throw new ArgumentOutOfRangeException($"Season out of range: {season}");
            if ((ulong)league >= LeagueMax || league < 0)
                throw new ArgumentOutOfRangeException($"League out of range: {league}");

            League = league;
            Season = season;
            Rank = rank;
            Division = division;
        }

        public DivisionIndex(EntityId divisionId)
        {
            MetaDebug.Assert(divisionId.IsOfKind(EntityKindCore.Division), "Cannot create a division index from anything else than Division EntityKind");
            Division = (int)(divisionId.Value & DivisionMask);
            Rank     = (int)((divisionId.Value >> RankShift) & RankMask);
            League   = (int)((divisionId.Value >> LeagueShift) & LeagueMask);
            Season   = (int)((divisionId.Value >> SeasonShift) & SeasonMask);
        }

        public override readonly string ToString()
        {
            return Invariant($"(l:{League} s:{Season} r:{Rank} d:{Division})");
        }

        public override readonly bool Equals(object obj)
        {
            return obj is DivisionIndex id && Equals(id);
        }

        public readonly bool Equals(DivisionIndex other)
        {
            return League == other.League &&
                   Season == other.Season &&
                   Rank == other.Rank &&
                   Division == other.Division;
        }

        public override readonly int GetHashCode()
        {
            return Util.CombineHashCode(League, Season, Rank, Division);
        }

        public static bool operator ==(DivisionIndex left, DivisionIndex right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DivisionIndex left, DivisionIndex right)
        {
            return !(left == right);
        }

        public readonly EntityId ToEntityId()
        {
            if ((ulong)Division >= DivisionMax ||
                (ulong)Rank >= RankMax ||
                (ulong)Season >= SeasonMax ||
                (ulong)League >= LeagueMax)
                throw new InvalidOperationException($"DivisionIndex out of range: {ToString()}");

            ulong entityId = (ulong)Division;
            entityId |= (ulong)Rank   << RankShift;
            entityId |= (ulong)League << LeagueShift;
            entityId |= (ulong)Season << SeasonShift;

            return EntityId.Create(EntityKindCore.Division, entityId);
        }

        public static DivisionIndex FromEntityId(EntityId divisionEntity)
            => new DivisionIndex(divisionEntity);
    }
}
