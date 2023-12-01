// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.League.Player
{
    [ModelAction(ActionCodesCore.PlayerDivisionAddOrUpdateParticipant)]
    public class PlayerDivisionAddOrUpdateParticipant : PlayerDivisionActionBase
    {
        /// <summary>
        /// The index of the participant to add or update.
        /// If the index is -1, the index will be set to <see cref="IDivisionModel.NextParticipantIdx"/>, and the next participant index will be incremented.
        /// </summary>
        public int                      ParticipantIndex { get; private set; }

        /// <summary>
        /// The actual EntityId of the participant. This value is not available on the client.
        /// </summary>
        [ServerOnly]
        public EntityId                 ParticipantId    { get; private set; }
        public PlayerDivisionAvatarBase PlayerAvatar     { get; private set; }

        PlayerDivisionAddOrUpdateParticipant() { }
        public PlayerDivisionAddOrUpdateParticipant(int participantIndex, EntityId participantId, PlayerDivisionAvatarBase playerAvatar)
        {
            ParticipantIndex = participantIndex;
            ParticipantId    = participantId;
            PlayerAvatar     = playerAvatar;
        }

        public override MetaActionResult InvokeExecute(IPlayerDivisionModel division, bool commit)
        {
            if (commit)
            {
                int participantIdx = ParticipantIndex;

                if (participantIdx == -1) // New participant
                {
                    participantIdx = division.NextParticipantIdx++;

                    if (division.ServerModel != null && ParticipantId.IsValid)
                        division.ServerModel.ParticipantIndexToEntityId[participantIdx] = ParticipantId;

                    division.Log.Debug("Adding new participant {ParticipantIndex} with Id {ParticipantId} in division {DivisionIndex}", ParticipantIndex, ParticipantId, division.DivisionIndex);
                    _ = division.AddOrUpdateParticipant(participantIdx, ParticipantId, PlayerAvatar);
                }
                else // Existing participant
                {
                    division.Log.Debug("Updating participant {ParticipantIndex} with Id {ParticipantId} in division {DivisionIndex}", ParticipantIndex, ParticipantId, division.DivisionIndex);
                    _ = division.AddOrUpdateParticipant(participantIdx, ParticipantId, PlayerAvatar);
                }
                // Participant list potentially changed so need to recompute indices.
                division.RefreshScores();
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.PlayerDivisionUpdateContribution)]
    public class PlayerDivisionUpdateContribution : PlayerDivisionActionBase
    {
        public int                      ParticipantIndex  { get; set; }
        public IDivisionContribution    Contribution      { get; set; }

        PlayerDivisionUpdateContribution() { }
        public PlayerDivisionUpdateContribution(int participantIndex, IDivisionContribution contribution)
        {
            ParticipantIndex = participantIndex;
            Contribution     = contribution;
        }

        public override MetaActionResult InvokeExecute(IPlayerDivisionModel division, bool commit)
        {
            if (!division.TryGetParticipant(ParticipantIndex, out IPlayerDivisionParticipantState participant))
                return MetaActionResult.NoSuchDivisionParticipant;

            if (commit)
            {
                participant.PlayerContribution = Contribution;
                division.RefreshScores();
            }

            return MetaActionResult.Success;
        }
    }
}
