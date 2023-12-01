// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.League.Guild
{
    /// <summary>
    /// Enabled if per-guild Leagues are enabled.
    /// </summary>
    public class GuildLeaguesEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled => false;
    }
}
