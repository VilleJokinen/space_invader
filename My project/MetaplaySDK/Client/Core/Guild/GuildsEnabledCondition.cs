// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// METAPLAY_ENABLE_GUILDS was changed to METAPLAY_DISABLE_GUILDS in Metaplay v21 to reflect the new default of having guilds enabled.
// Generate a compile time error if the old define is encountered.
#if METAPLAY_ENABLE_GUILDS
#error METAPLAY_ENABLE_GUILDS is no longer supported, replace it with !METAPLAY_DISABLE_GUILDS and update any usage of the old define.
#endif

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Feature condition for Metaplay guild system.
    /// </summary>
    public class GuildsEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        #if !METAPLAY_DISABLE_GUILDS
        public override bool IsEnabled => IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.FeatureFlags.EnableGuilds;
        #else
        public override bool IsEnabled => false;
        #endif
    }
}
