// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// Force the given activable (identified by activable kind <see cref="KindId"/> and
    /// activable id <see cref="ActivableIdStr"/> within that kind) into a given phase
    /// (or remove a forced phase, if <see cref="Phase"/> is null) for development purposes.
    ///
    /// This should only be used for development purposes in development environments.
    /// Forcing a phase for an activable bypasses the normal scheduling and targeting
    /// configurations of the activable, and may thus interfere with testing the normal
    /// behavior of the activable on the same player.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerDebugForceSetActivablePhase)]
    [DevelopmentOnlyAction]
    public class PlayerDebugForceSetActivablePhase : PlayerActionCore<IPlayerModelBase>
    {
        public MetaActivableKindId              KindId;
        public string                           ActivableIdStr; // \note #activable-id-type
        public MetaActivableState.DebugPhase?   Phase;

        PlayerDebugForceSetActivablePhase(){ }
        public PlayerDebugForceSetActivablePhase(MetaActivableKindId kindId, string activableIdStr, MetaActivableState.DebugPhase? phase)
        {
            KindId = kindId;
            ActivableIdStr = activableIdStr;
            Phase = phase;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.DebugForceSetActivablePhase(KindId, ActivableIdStr, Phase);
            }

            return MetaActionResult.Success;
        }
    }

    /// <inheritdoc cref="PlayerDebugForceSetActivablePhase"/>
    /// <remark>
    /// This is a server-invoked variant of <see cref="PlayerDebugForceSetActivablePhase"/>.
    /// </remark>
    [ModelAction(ActionCodesCore.PlayerServerDebugForceSetActivablePhase)]
    [DevelopmentOnlyAction]
    public class PlayerServerDebugForceSetActivablePhase : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public MetaActivableKindId              KindId;
        public string                           ActivableIdStr; // \note #activable-id-type
        public MetaActivableState.DebugPhase?   Phase;

        PlayerServerDebugForceSetActivablePhase(){ }
        public PlayerServerDebugForceSetActivablePhase(MetaActivableKindId kindId, string activableIdStr, MetaActivableState.DebugPhase? phase)
        {
            KindId = kindId;
            ActivableIdStr = activableIdStr;
            Phase = phase;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.DebugForceSetActivablePhase(KindId, ActivableIdStr, Phase);
            }

            return MetaActionResult.Success;
        }
    }
}
