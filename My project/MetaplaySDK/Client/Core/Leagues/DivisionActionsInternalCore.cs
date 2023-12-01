// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.League
{
    [ModelAction(ActionCodesCore.DivisionParticipantRemove)]
    public class DivisionParticipantRemove : DivisionActionBase
    {
        public int ParticipantIndex { get; private set; }

        DivisionParticipantRemove() { }
        public DivisionParticipantRemove(int participantIndex)
        {
            ParticipantIndex = participantIndex;
        }

        public override MetaActionResult InvokeExecute(IDivisionModel division, bool commit)
        {
            if (commit)
            {
                division.Log.Debug("Participant {ParticipantId} removed from division {DivisionIndex}", ParticipantIndex, division.DivisionIndex);
                division.RemoveParticipant(ParticipantIndex);
                division.RefreshScores();
            }

            return MetaActionResult.Success;
        }
    }
}
