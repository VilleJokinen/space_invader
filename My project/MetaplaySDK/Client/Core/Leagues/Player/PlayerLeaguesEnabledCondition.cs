// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// Enabled if per-player Leagues are enabled.
    /// </summary>
    public class PlayerLeaguesEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled => MetaplayCore.Options.FeatureFlags.EnablePlayerLeagues;
    }
}
