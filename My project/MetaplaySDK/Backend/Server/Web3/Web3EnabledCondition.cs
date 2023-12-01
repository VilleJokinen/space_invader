// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Web3;

namespace Metaplay.Server.Web3
{
    public class Web3EnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled => IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.FeatureFlags.EnableWeb3;
    }
}
