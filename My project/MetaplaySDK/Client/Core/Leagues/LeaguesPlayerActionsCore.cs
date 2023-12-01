// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using System.Linq;

namespace Metaplay.Core.League
{
    [ModelAction(ActionCodesCore.PlayerClaimHistoricalPlayerDivisionRewards)]
    [LeaguesEnabledCondition]
    public class PlayerClaimHistoricalPlayerDivisionRewards : PlayerActionCore<IPlayerModelBase>
    {
        public EntityId HistoricDivisionId { get; private set; }

        public PlayerClaimHistoricalPlayerDivisionRewards(EntityId historicDivisionId)
        {
            HistoricDivisionId = historicDivisionId;
        }

        PlayerClaimHistoricalPlayerDivisionRewards() { }

        /// <inheritdoc />
        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase subClientState))
                return MetaActionResult.InvalidDivisionState;

            if (!(subClientState is IDivisionClientState divisionClientState))
                return MetaActionResult.InvalidDivisionState;

            IDivisionHistoryEntry foundHistoricDivision = divisionClientState.HistoricalDivisions.FirstOrDefault(division => division.DivisionId == HistoricDivisionId);
            if (foundHistoricDivision == null)
                return MetaActionResult.NoSuchDivision;

            if (foundHistoricDivision.Rewards == null || foundHistoricDivision.Rewards.IsClaimed)
                return MetaActionResult.RewardAlreadyClaimed;

            if (commit)
            {
                foundHistoricDivision.Rewards.Apply(player);
                foundHistoricDivision.Rewards.IsClaimed = true;
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.PlayerSetCurrentDivision)]
    [LeaguesEnabledCondition]
    public class PlayerSetCurrentDivision : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public EntityId NewCurrentDivision { get; private set; }
        public int      ParticipantIndex   { get; private set; }

        public PlayerSetCurrentDivision(EntityId newCurrentDivision, int participantIndex)
        {
            NewCurrentDivision = newCurrentDivision;
            ParticipantIndex   = participantIndex;
        }

        PlayerSetCurrentDivision() { }

        /// <inheritdoc />
        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase subClientState))
                return MetaActionResult.InvalidDivisionState;

            if (!(subClientState is IDivisionClientState divisionClientState))
                return MetaActionResult.InvalidDivisionState;

            if (commit)
            {
                divisionClientState.CurrentDivision               = NewCurrentDivision;
                divisionClientState.CurrentDivisionParticipantIdx = ParticipantIndex;
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.PlayerAddHistoricalDivisionEntry)]
    [LeaguesEnabledCondition]
    public class PlayerAddHistoricalDivisionEntry : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public IDivisionHistoryEntry NewHistoryEntry       { get; private set; }
        public bool                  RemoveCurrentDivision { get; private set; }

        public PlayerAddHistoricalDivisionEntry(IDivisionHistoryEntry newHistoryEntry, bool removeCurrentDivision)
        {
            NewHistoryEntry       = newHistoryEntry;
            RemoveCurrentDivision = removeCurrentDivision;
        }

        PlayerAddHistoricalDivisionEntry() { }

        /// <inheritdoc />
        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivision, out PlayerSubClientStateBase subClientState))
                return MetaActionResult.InvalidDivisionState;

            if (!(subClientState is IDivisionClientState divisionClientState))
                return MetaActionResult.InvalidDivisionState;

            if(NewHistoryEntry == null || !NewHistoryEntry.DivisionId.IsValid)
                return MetaActionResult.InvalidDivisionHistoryEntry;

            if(divisionClientState.HistoricalDivisions.Any(division => division.DivisionId == NewHistoryEntry.DivisionId))
                return MetaActionResult.DuplicateDivisionHistoryEntry;

            if (commit)
            {
                divisionClientState.AddHistoricalDivision(NewHistoryEntry);

                // \todo [nomi] Is this needed? If so, all samples and projects need to be updated.
                //player.ClientListenerCore.OnNewPlayerDivisionHistoryEntryAvailable(NewHistoryEntry.DivisionId);

                if(RemoveCurrentDivision)
                    divisionClientState.CurrentDivision = EntityId.None;
            }

            return MetaActionResult.Success;
        }
    }
}
