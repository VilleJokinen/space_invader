// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.League.Guild;
using Metaplay.Core.League.Player;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Enabled if per-player or per-guild Leagues are enabled or general League tooling is enabled.
    /// </summary>
    public class LeaguesEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled =>
            (new PlayerLeaguesEnabledCondition()).IsEnabled
            || (new GuildLeaguesEnabledCondition()).IsEnabled;
    }
}
