// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.League.Actions
{
    [DevelopmentOnlyAction]
    [LeaguesEnabledCondition]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerSynchronized)]
    public abstract class DivisionDebugAction : DivisionActionBase
    {
    }

    [ModelAction(ActionCodesCore.DivisionSetSeasonStartsAtDebug)]
    public class DivisionSetSeasonStartsAtDebug : DivisionDebugAction
    {
        public MetaTime StartsAt { get; private set; }

        DivisionSetSeasonStartsAtDebug() { }
        public DivisionSetSeasonStartsAtDebug(MetaTime startsAt)
        {
            StartsAt = startsAt;
        }

        public override MetaActionResult InvokeExecute(IDivisionModel division, bool commit)
        {
            if (commit)
            {
                division.StartsAt = StartsAt;
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.DivisionSetSeasonEndsAtDebug)]
    public class DivisionSetSeasonEndsAtDebug : DivisionDebugAction
    {
        public MetaTime EndsAt { get; private set; }

        DivisionSetSeasonEndsAtDebug() { }
        public DivisionSetSeasonEndsAtDebug(MetaTime endsAt)
        {
            EndsAt = endsAt;
        }

        public override MetaActionResult InvokeExecute(IDivisionModel division, bool commit)
        {
            if (commit)
            {
                division.EndsAt = EndsAt;
                if (ModelUtil.TimeAtTick(division.CurrentTick, division.TimeAtFirstTick, division.TicksPerSecond) >= EndsAt)
                {
                    division.ServerListenerCore.OnSeasonDebugEnded();
                }
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodesCore.DivisionConcludeSeasonDebug)]
    public class DivisionConcludeSeasonDebug : DivisionDebugAction
    {
        public DivisionConcludeSeasonDebug() { }

        public override MetaActionResult InvokeExecute(IDivisionModel division, bool commit)
        {
            if (commit)
            {
                if (!division.IsConcluded)
                {
                    division.ServerListenerCore.OnSeasonDebugConcluded();
                }
            }

            return MetaActionResult.Success;
        }
    }
}
