// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.League
{
    public interface IDivisionClientState
    {
        EntityId                           CurrentDivision     { get; set; }
        IEnumerable<IDivisionHistoryEntry> HistoricalDivisions { get; }
        
        int CurrentDivisionParticipantIdx { get; set; }
        
        void AddHistoricalDivision(IDivisionHistoryEntry historicalDivision);
    }

    [MetaReservedMembers(100, 200)]
    public abstract class DivisionClientStateBase<TDivisionHistoryEntry> : PlayerSubClientStateBase, IDivisionClientState
        where TDivisionHistoryEntry: class, IDivisionHistoryEntry
    {
        [MetaMember(100), NoChecksum] public EntityId        CurrentDivision     { get; set; }
        [MetaMember(101)] public List<TDivisionHistoryEntry> HistoricalDivisions { get; set; } = new List<TDivisionHistoryEntry>();

        /// <summary>
        /// If the player is currently participating in a division, this is the participant index of the player in the division. Otherwise -1.
        /// </summary>
        [MetaMember(102), Transient, NoChecksum] public int CurrentDivisionParticipantIdx { get; set; } = -1;
        
        
        /// <summary>
        /// The current division's <see cref="DivisionIndex"/>.
        /// </summary>
        public DivisionIndex CurrentDivisionIndex => CurrentDivision.IsValid ? DivisionIndex.FromEntityId(CurrentDivision) : new DivisionIndex();

        /// <summary>
        /// True if player was promoted from last season.
        /// </summary>
        public bool WasPromoted
        {
            get
            {
                if (!CurrentDivision.IsValid  || HistoricalDivisions.Count == 0)
                    return false;

                return CurrentDivisionIndex.Rank > HistoricalDivisions[HistoricalDivisions.Count - 1].DivisionIndex.Rank;
            }
        }

        /// <summary>
        /// True if player was demoted from last season.
        /// </summary>
        public bool WasDemoted
        {
            get
            {
                if (!CurrentDivision.IsValid || HistoricalDivisions.Count == 0)
                    return false;

                return CurrentDivisionIndex.Rank < HistoricalDivisions[HistoricalDivisions.Count - 1].DivisionIndex.Rank;
            }
        }

        IEnumerable<IDivisionHistoryEntry> IDivisionClientState.HistoricalDivisions => HistoricalDivisions;

        void IDivisionClientState.AddHistoricalDivision(IDivisionHistoryEntry historicalDivision)
        {
            if(HistoricalDivisions.All(entry => entry.DivisionId != historicalDivision.DivisionId))
                HistoricalDivisions.Add(historicalDivision as TDivisionHistoryEntry);
        }
    }
}
