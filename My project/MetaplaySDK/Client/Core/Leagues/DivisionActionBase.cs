// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Base class for all Division actions.
    /// </summary>
    [MetaSerializable]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    [MetaImplicitMembersRange(101, 200)]
    public abstract class DivisionActionBase : ModelAction<IDivisionModel>
    {
        /// <summary>
        /// The ID of the participant which invoked this action. If this action is invoked by
        /// server, the ID is <c>None</c>.
        /// </summary>
        [MetaMember(101)] public EntityId InvokingParticipantId;

        /// <summary>
        /// The ID of the player which invoked this action. If this action is invoked by
        /// server or a non-player participant, the ID is <c>None</c>.
        /// </summary>
        [MetaMember(102)] public EntityId InvokingPlayerId;
    }


    [ModelAction(ActionCodesCore.DivisionConclude)]
    public class DivisionConclude : DivisionActionBase
    {
        public DivisionConclude() { }

        public override MetaActionResult InvokeExecute(IDivisionModel division, bool commit)
        {
            if (commit)
            {
                division.IsConcluded = true;
                division.ClientListenerCore.OnSeasonConcluded();
            }

            return MetaActionResult.Success;
        }
    }
}

